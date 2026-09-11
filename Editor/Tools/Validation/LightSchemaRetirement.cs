using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Editor.Validation
{
    internal static class LightSchemaRetirement
    {
        [Serializable] sealed class Change
        {
            public long componentId, lightId;
            public uint originalMask, mask;
            public int originalShadows, shadows;
            public bool setArea;
            public string[] removeFields;
        }
        [Serializable] sealed class RendererChange { public long objectId; }
        [Serializable] sealed class Asset
        {
            public string path, beforeHash, metaHash, native, precise, nativeHash, preciseHash;
            public Change[] lightChanges;
            public RendererChange[] rendererChanges;
        }
        [Serializable] sealed class Manifest { public string status, project; public Asset[] assets; }
        [Serializable] sealed class Result
        {
            public string path, guid, beforeHash, afterHash, metaHash, status, error;
            public string beforeSemantic, afterSemantic;
            public bool noOp, secondSave;
        }
        [Serializable] sealed class Report
        {
            public string status, unity, manifestHash, sceneStateBefore, sceneStateAfter;
            public List<Result> assets = new List<Result>();
        }
        [Serializable] sealed class NativeSummary { public int objectCount; public string semanticHash; }

        static readonly HashSet<string> s_Retired = new HashSet<string>(StringComparer.Ordinal)
        {
            "unityLight", "state", "width", "height", "enableIndirect", "indirectIntensity",
            "IESIndex", "IESTexture", "cookieIndex", "cookieTexture", "enableShadow", "nearPlane",
            "minSoftness", "maxSoftness", "shadowType", "shadowLayer", "resolution", "contactShadowLength"
        };

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Light schema retirement requires idle EditMode.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation"));
            string selected = EditorUtility.OpenFilePanel("Select prepared light schema manifest", root, "json");
            if (string.IsNullOrEmpty(selected)) return;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(selected));
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (manifest.status != "Completed" || Path.GetFullPath(manifest.project) != project)
                throw new InvalidDataException("Manifest is incomplete or belongs to another project.");
            string run = Path.Combine(root, "light-schema-retirement-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            File.Copy(selected, Path.Combine(run, "manifest.json"));
            var report = new Report { status = "CopyProof", unity = Application.unityVersion,
                manifestHash = Hash(selected), sceneStateBefore = SceneState() };
            bool failed = false;
            try
            {
                for (int i = 0; i < manifest.assets.Length; ++i)
                {
                    Asset asset = manifest.assets[i];
                    string path = RelativeAssetPath(asset.path, project);
                    string directory = Path.Combine(run, i.ToString());
                    Directory.CreateDirectory(directory);
                    var result = new Result { path = path, guid = AssetDatabase.AssetPathToGUID(path),
                        beforeHash = Hash(path), metaHash = Hash(path + ".meta"), status = "Prepared" };
                    report.assets.Add(result);
                    string copy = "Assets/InfinityLightSchemaProof-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path);
                    try
                    {
                        RequireEquivalentNativeAuthority(asset);
                        RequireClean(path);
                        if (result.beforeHash != asset.beforeHash || result.metaHash != asset.metaHash ||
                            Hash(asset.native) != asset.nativeHash || Hash(asset.precise) != asset.preciseHash)
                            throw new InvalidDataException("Prepared source/native evidence changed: " + path);
                        File.Copy(path, Path.Combine(directory, "before" + Path.GetExtension(path)));
                        File.Copy(path + ".meta", Path.Combine(directory, "before.meta"));
                        File.Copy(asset.native, Path.Combine(directory, "before.native.txt"));
                        File.Copy(asset.precise, Path.Combine(directory, "before.floatbits.txt"));
                        result.beforeSemantic = FilteredSummary(asset.native, asset.precise, directory, "before", result.guid, result.guid, asset);
                        result.noOp = true;
                        foreach (Change change in asset.lightChanges)
                            if (change.removeFields.Length != 0) result.noOp = false;
                        if (!result.noOp)
                        {
                            File.Copy(path, copy);
                            AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                            AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);
                            string copyPlain = Path.Combine(directory, "copy.native.txt"), copyBits = Path.Combine(directory, "copy.floatbits.txt");
                            Dump(copy, copyPlain, false); Dump(copy, copyBits, true);
                            string copySemantic = FilteredSummary(copyPlain, copyBits, directory, "copy", AssetDatabase.AssetPathToGUID(copy), result.guid, asset);
                            if (copySemantic != result.beforeSemantic)
                                throw new InvalidDataException("Copy changed non-retired native fields: " + path);
                            RequireRetired(copyPlain, asset);
                            string first = Hash(copy);
                            AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);
                            if (Hash(copy) != first) throw new InvalidDataException("Copy second serialization is not byte-identical.");
                        }
                        result.status = "CopyProven";
                    }
                    catch (Exception error) { result.status = "Failed"; result.error = error.ToString(); failed = true; }
                    finally
                    {
                        if (File.Exists(copy)) AssetDatabase.DeleteAsset(copy);
                        Write(run, report);
                    }
                }
                if (failed) throw new InvalidDataException("One or more copy proofs failed; no source assets were saved. See the complete batch report.");
                if (SceneState() != report.sceneStateBefore) throw new InvalidOperationException("Scene setup/dirty state changed during copy proofs.");
                report.status = "SourceApply";
                Write(run, report);
                for (int i = 0; i < manifest.assets.Length; ++i)
                {
                    Asset asset = manifest.assets[i];
                    Result result = report.assets[i];
                    string directory = Path.Combine(run, i.ToString());
                    RequireClean(result.path);
                    if (Hash(result.path) != result.beforeHash || Hash(result.path + ".meta") != result.metaHash)
                        throw new InvalidDataException("Source changed after copy proof: " + result.path);
                    if (!result.noOp) AssetDatabase.ForceReserializeAssets(new[] { result.path }, ForceReserializeAssetsOptions.ReserializeAssets);
                    result.afterHash = Hash(result.path);
                    string plain = Path.Combine(directory, "after.native.txt"), bits = Path.Combine(directory, "after.floatbits.txt");
                    Dump(result.path, plain, false); Dump(result.path, bits, true);
                    result.afterSemantic = FilteredSummary(plain, bits, directory, "after", result.guid, result.guid, asset);
                    RequireRetired(plain, asset);
                    if (result.afterSemantic != result.beforeSemantic) throw new InvalidDataException("Source differs outside retired fields: " + result.path);
                    if (!result.noOp) AssetDatabase.ForceReserializeAssets(new[] { result.path }, ForceReserializeAssetsOptions.ReserializeAssets);
                    result.secondSave = Hash(result.path) == result.afterHash;
                    AssetDatabase.ImportAsset(result.path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    if (!result.secondSave || Hash(result.path) != result.afterHash || Hash(result.path + ".meta") != result.metaHash || AssetDatabase.AssetPathToGUID(result.path) != result.guid)
                        throw new InvalidDataException("Reload, metadata or idempotence failed: " + result.path);
                    result.status = result.noOp ? "VerifiedNoOp" : "VerifiedPendingIndependentCheck";
                    Write(run, report);
                }
                report.sceneStateAfter = SceneState();
                if (report.sceneStateAfter != report.sceneStateBefore) throw new InvalidOperationException("Loaded scene state changed; preserve it for inspection.");
                report.status = "Completed";
            }
            catch { report.status = "Failed"; throw; }
            finally { Write(run, report); Debug.Log("[InfinityRP] Light schema retirement " + report.status + ": " + run); }
        }

        static void RequireEquivalentNativeAuthority(Asset asset)
        {
            if (asset.rendererChanges.Length != 0) throw new InvalidDataException("This retirement requires already-canonical native Renderer masks.");
            foreach (Change change in asset.lightChanges)
                if (change.originalMask != change.mask || change.originalShadows != change.shadows || change.setArea)
                    throw new InvalidDataException("Native values require a transfer, not field-only retirement: " + asset.path);
        }

        static void RequireClean(string path)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Preserve unsaved scene edits: " + path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer && EditorUtility.IsDirty(importer)) throw new InvalidOperationException("Preserve dirty importer: " + path);
        }

        static string RelativeAssetPath(string path, string project)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(project + "/Assets/", StringComparison.Ordinal)) throw new InvalidDataException("Target is outside project Assets.");
            return full.Substring(project.Length + 1);
        }

        static string FilteredSummary(string plain, string precise, string directory, string prefix, string guid, string sourceGuid, Asset asset)
        {
            string filtered = Path.Combine(directory, prefix + ".filtered.txt"), bits = Path.Combine(directory, prefix + ".filteredbits.txt");
            File.WriteAllText(filtered, Filter(File.ReadAllText(plain), asset, false));
            File.WriteAllText(bits, Filter(File.ReadAllText(precise), asset, true));
            MethodInfo parser = typeof(AssemblyMigrationPreflight).GetMethod("ReadNativeDump", BindingFlags.Static | BindingFlags.NonPublic);
            object summary;
            try { summary = parser.Invoke(null, new object[] { filtered, bits, guid, sourceGuid, CancellationToken.None }); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw(); throw; }
            string json = JsonUtility.ToJson(summary, true);
            File.WriteAllText(Path.Combine(directory, prefix + ".summary.json"), json);
            NativeSummary parsed = JsonUtility.FromJson<NativeSummary>(json);
            return parsed.objectCount + "|" + parsed.semanticHash;
        }

        static string Filter(string text, Asset asset, bool precise)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Change change in asset.lightChanges)
            {
                targets.Add(change.componentId.ToString());
                if (precise) targets.Add(change.componentId.ToString("x"));
            }
            bool target = false, skip = false;
            var output = new System.Text.StringBuilder();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                Match header = Regex.Match(line, @"^ID: (\S+) \(ClassID:");
                if (header.Success) { target = targets.Contains(header.Groups[1].Value); skip = false; }
                if (line.StartsWith("\t") && !line.StartsWith("\t\t"))
                {
                    int space = line.IndexOf(' ', 1);
                    skip = target && space > 1 && s_Retired.Contains(line.Substring(1, space - 1));
                }
                if (!skip || string.IsNullOrWhiteSpace(line)) output.AppendLine(line);
            }
            return output.ToString();
        }

        static void RequireRetired(string plain, Asset asset)
        {
            string source = File.ReadAllText(plain).Replace("\r\n", "\n");
            string filtered = Filter(source, asset, false);
            if (Regex.Replace(source, @"\s", "") != Regex.Replace(filtered, @"\s", ""))
                throw new InvalidDataException("Retired InfinityAdditionalLightData fields remain in native source.");
        }

        static void Dump(string input, string output, bool precise)
        {
            MethodInfo reader = typeof(KnownSceneReferenceRepairs).GetMethod("RunReader", BindingFlags.Static | BindingFlags.NonPublic);
            try { reader.Invoke(null, new object[] { input, output, precise }); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw(); throw; }
        }
        static string SceneState() => (string)typeof(AssemblyMigrationPreflight).GetMethod("SceneState", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        static void Write(string directory, Report report) => File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
    }
}
