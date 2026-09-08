using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Editor
{
    internal static class AssemblyIdentityMigration
    {
        const string Root = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation";
        const string OldAssembly = "Unity.RenderPipelines.HighDefinition.Runtime";
        const string NewAssembly = "Unity.RenderPipelines.Infinity.Runtime";
        static readonly string[] Targets =
        {
            "Assets/Scene/Validation/Validation_Decal.unity",
            "Assets/Scene/Validation/Validation_LocalLights.unity",
            "Assets/Scene/Validation/Validation_Output.unity",
            "Assets/Scene/Validation/Validation_Temporal.unity",
            "Assets/Scene/Validation/Validation_Translucent.unity",
            "Assets/Scene/Validation/Validation_Volume.unity",
            "Packages/com.infinity.render-pipeline/Runtime/Resources/InfinityDefaultVolumeProfile.asset"
        };

        [Serializable] sealed class NativeSummary
        {
            public int objectCount;
            public string semanticHash;
            public List<string> scripts, classIdentifiers, managedMetadata;
        }
        [Serializable] sealed class Entry
        {
            public string path, guid, beforeHash, metaHash, expectedSemanticHash, afterHash;
            public int objectCount, identityCount;
        }
        [Serializable] sealed class Manifest
        {
            public string status, sceneState, directory;
            public List<Entry> entries = new List<Entry>();
        }
        [Serializable] sealed class Result
        {
            public string status, manifestHash, sceneState, error, recoveredFrom;
            public bool noOp;
            public List<Entry> entries = new List<Entry>();
        }
        [Serializable] sealed class ReferenceAsset { public string path; public List<string> scriptReferences; }
        [Serializable] sealed class ReferenceReport { public List<ReferenceAsset> assets; }
        [Serializable] sealed class ResolvedScript { public string guid, path, type, assembly; }
        [Serializable] sealed class TypeResult
        {
            public string status, sourceReportHash, sceneState;
            public List<ResolvedScript> scripts = new List<ResolvedScript>();
        }

        [MenuItem("Infinity/Validation/Migration/Verify Migrated MonoScript Types")]
        static void VerifyScriptTypes()
        {
            RequireIdle(NewAssembly);
            const string reportPath = Root + "/serialization-20260906T111704174Z-0d3ff3eb9a2b43ec856a7dace9536b21/report.json";
            if (Hash(reportPath) != "4c3af779bcd268c523a30093669d1956a50949deb2d7bebb1b9e962ba6c50c02")
                throw new InvalidDataException("Original script-reference inventory changed.");
            ReferenceReport inventory = JsonUtility.FromJson<ReferenceReport>(File.ReadAllText(reportPath));
            var result = new TypeResult { sourceReportHash = Hash(reportPath), sceneState = SceneState() };
            var visited = new HashSet<string>();
            foreach (ReferenceAsset asset in inventory.assets)
                foreach (string reference in asset.scriptReferences)
                {
                    if (reference.StartsWith("ImporterMeta/", StringComparison.Ordinal))
                    {
                        if (!visited.Add(reference)) continue;
                        string globalIdText = reference.Substring(reference.LastIndexOf('|') + 1).Trim();
                        if (!GlobalObjectId.TryParse(globalIdText, out GlobalObjectId globalId) ||
                            !(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) is MonoScript importerScript) || importerScript.GetClass() == null)
                            throw new InvalidDataException("Unresolved inventoried importer script: " + reference);
                        Type importerType = importerScript.GetClass();
                        result.scripts.Add(new ResolvedScript { guid = globalIdText, path = reference, type = importerType.FullName, assembly = importerType.Assembly.GetName().Name });
                        continue;
                    }
                    string guid = reference.Split('|')[0];
                    if (!visited.Add(guid)) continue;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    Type type = script == null ? null : script.GetClass();
                    if (type == null) throw new InvalidDataException("Unresolved inventoried MonoScript: " + guid + " / " + path);
                    string assembly = type.Assembly.GetName().Name;
                    if (assembly == OldAssembly || (path.StartsWith("Packages/com.infinity.render-pipeline/Runtime/", StringComparison.Ordinal) && assembly != NewAssembly))
                        throw new InvalidDataException("Incorrect migrated MonoScript assembly: " + path);
                    result.scripts.Add(new ResolvedScript { guid = guid, path = path, type = type.FullName, assembly = assembly });
                }
            RequireSceneState(result.sceneState);
            result.status = "InventoriedMonoScriptTypesResolved";
            string directory = NewRun("identity-type-verify");
            Write(Path.Combine(directory, "result.json"), JsonUtility.ToJson(result, true));
            Debug.Log("[InfinityRP] Migrated MonoScript types verified: " + result.scripts.Count + " / " + directory);
        }

        [MenuItem("Infinity/Validation/Migration/Prepare Serialized Infinity Identities")]
        static void Prepare()
        {
            RequireIdle(OldAssembly);
            if (File.Exists(Path.Combine(Root, "identity-native-pending.txt")))
                throw new InvalidOperationException("An identity migration is already pending; preserve its evidence before proceeding.");
            string directory = NewRun("identity-native-prepare");
            var manifest = new Manifest { status = "Preparing", sceneState = SceneState(), directory = directory };
            foreach (string path in Targets)
            {
                RequireUnloadedScene(path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw new InvalidDataException("Missing asset GUID: " + path);
                var entry = new Entry { path = path, guid = guid, beforeHash = Hash(path), metaHash = Hash(path + ".meta") };
                Backup(directory, path);
                string prefix = Path.GetFileNameWithoutExtension(path);
                NativeSummary before = Capture(path, directory, prefix, guid);
                if (before.managedMetadata.Count != 0) throw new InvalidDataException("Unexpected managed-reference metadata: " + path);
                string nativePath = Path.Combine(directory, prefix + ".native.txt");
                string projectedPath = Path.Combine(directory, prefix + "-projected.native.txt");
                string[] lines = File.ReadAllLines(nativePath);
                for (int i = 0; i < lines.Length; ++i)
                {
                    if (!lines[i].Contains(OldAssembly)) continue;
                    if (!lines[i].TrimStart().StartsWith("m_EditorClassIdentifier \"", StringComparison.Ordinal))
                        throw new InvalidDataException("Old identity outside the reviewed identifier field: " + path);
                    lines[i] = lines[i].Replace(OldAssembly, NewAssembly);
                    entry.identityCount++;
                }
                if (entry.identityCount == 0) throw new InvalidDataException("Expected mapped identity absent: " + path);
                Write(projectedPath, string.Join("\n", lines) + "\n");
                NativeSummary expected = Parse(projectedPath, Path.Combine(directory, prefix + ".floatbits.txt"), guid);
                entry.expectedSemanticHash = expected.semanticHash;
                entry.objectCount = expected.objectCount;
                if (entry.objectCount != before.objectCount || Hash(path) != entry.beforeHash || Hash(path + ".meta") != entry.metaHash)
                    throw new InvalidDataException("Source changed during identity preparation: " + path);
                manifest.entries.Add(entry);
            }
            RequireSceneState(manifest.sceneState);
            manifest.status = "PreparedNoSourceWrites";
            string manifestPath = Path.Combine(directory, "manifest.json");
            Write(manifestPath, JsonUtility.ToJson(manifest, true));
            Write(Path.Combine(Root, "identity-native-prepared.txt"), manifestPath);
            Write(Path.Combine(Root, "identity-native-prepared.sha256"), Hash(manifestPath));
            Debug.Log("[InfinityRP] Serialized identity preparation complete: " + manifestPath);
        }

        [MenuItem("Infinity/Validation/Migration/Apply or Verify Serialized Infinity Identities")]
        static void ApplyOrVerify()
        {
            RequireIdle(NewAssembly);
            string manifestPath = File.ReadAllText(Path.Combine(Root, "identity-native-prepared.txt"));
            string manifestHash = Hash(manifestPath);
            if (manifestHash != File.ReadAllText(Path.Combine(Root, "identity-native-prepared.sha256")))
                throw new InvalidDataException("Prepared identity manifest changed.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest.status != "PreparedNoSourceWrites" || manifest.entries.Count != Targets.Length)
                throw new InvalidDataException("Incomplete identity manifest.");
            string pending = Path.Combine(Root, "identity-native-pending.txt");
            string failedDirectory = File.Exists(pending) ? File.ReadAllText(pending) : null;
            if (failedDirectory != null)
            {
                Result failed = JsonUtility.FromJson<Result>(File.ReadAllText(Path.Combine(failedDirectory, "result.json")));
                if (failed.status != "Failed" || failed.manifestHash != manifestHash)
                    throw new InvalidOperationException("Pending identity result is not a bound, terminal failure.");
            }
            string copyProofPath = File.ReadAllText(Path.Combine(Root, "identity-copy-passed.txt"));
            if (Hash(copyProofPath) != File.ReadAllText(Path.Combine(Root, "identity-copy-passed.sha256")) ||
                JsonUtility.FromJson<Result>(File.ReadAllText(copyProofPath)).status != "CopyUpdateVerified")
                throw new InvalidDataException("Explicit serialized-property update has no verified copy result.");
            string accepted = Path.Combine(Root, "identity-native-applied.txt");
            if (File.Exists(accepted) && Hash(File.ReadAllText(accepted)) != File.ReadAllText(accepted + ".sha256"))
                throw new InvalidDataException("Previous identity result changed.");
            Result previous = File.Exists(accepted) ? JsonUtility.FromJson<Result>(File.ReadAllText(File.ReadAllText(accepted))) : null;
            if (previous != null && (previous.status != "VerifiedPendingTerra" || previous.manifestHash != manifestHash))
                throw new InvalidDataException("Previous identity result is not bound to this manifest.");
            string directory = NewRun("identity-native-apply");
            var result = new Result { status = "Preparing", manifestHash = manifestHash, sceneState = SceneState(), noOp = previous != null, recoveredFrom = failedDirectory };
            var alreadyUpdated = new HashSet<string>();
            try
            {
                for (int i = 0; i < manifest.entries.Count; ++i)
                {
                    Entry entry = manifest.entries[i];
                    if (entry.path != Targets[i]) throw new InvalidDataException("Identity target list changed.");
                    RequireUnloadedScene(entry.path);
                    string expectedHash = previous == null ? entry.beforeHash : previous.entries[i].afterHash;
                    if (Hash(entry.path + ".meta") != entry.metaHash || AssetDatabase.AssetPathToGUID(entry.path) != entry.guid)
                        throw new InvalidDataException("Identity input drift: " + entry.path);
                    if (Hash(entry.path) != expectedHash)
                    {
                        if (failedDirectory == null || previous != null) throw new InvalidDataException("Unexplained identity source drift: " + entry.path);
                        string prefix = Path.GetFileNameWithoutExtension(entry.path);
                        NativeSummary current = Capture(entry.path, directory, "recovery-" + prefix, entry.guid);
                        NativeSummary original = JsonUtility.FromJson<NativeSummary>(File.ReadAllText(Path.Combine(manifest.directory, prefix + ".summary.json")));
                        if (current.objectCount != entry.objectCount ||
                            (current.semanticHash != original.semanticHash && current.semanticHash != entry.expectedSemanticHash))
                            throw new InvalidDataException("Pending source is neither original-equivalent nor exactly migrated: " + entry.path);
                        if (current.semanticHash == entry.expectedSemanticHash) alreadyUpdated.Add(entry.path);
                    }
                    Backup(directory, entry.path);
                    entry.beforeHash = Hash(entry.path);
                }
                Write(Path.Combine(directory, "manifest.json"), File.ReadAllText(manifestPath));
                Write(Path.Combine(directory, "intent.json"), JsonUtility.ToJson(result, true));
                if (!result.noOp) Write(pending, directory);
                foreach (Entry entry in manifest.entries)
                {
                    RequireSceneState(result.sceneState);
                    if (Hash(entry.path) != entry.beforeHash || Hash(entry.path + ".meta") != entry.metaHash)
                        throw new InvalidDataException("Identity source changed after fresh backup: " + entry.path);
                    if (!result.noOp && !alreadyUpdated.Contains(entry.path))
                    {
                        UpdateIdentifiers(entry.path, entry.identityCount);
                        AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    }
                    string firstHash = Hash(entry.path);
                    NativeSummary actual = Capture(entry.path, directory, Path.GetFileNameWithoutExtension(entry.path), entry.guid);
                    if (actual.semanticHash != entry.expectedSemanticHash || actual.objectCount != entry.objectCount ||
                        actual.classIdentifiers.Exists(value => value.Contains(OldAssembly)))
                        throw new InvalidDataException("Serialized identity/non-target data mismatch: " + entry.path);
                    foreach (string script in actual.scripts)
                    {
                        string scriptPath = AssetDatabase.GUIDToAssetPath(script.Split('|')[0]);
                        MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                        if (monoScript == null || monoScript.GetClass() == null)
                            throw new InvalidDataException("Missing MonoScript type after identity migration: " + scriptPath);
                    }
                    if (!result.noOp) AssetDatabase.ForceReserializeAssets(new[] { entry.path }, ForceReserializeAssetsOptions.ReserializeAssets);
                    entry.afterHash = Hash(entry.path);
                    if (entry.afterHash != firstHash || Hash(entry.path + ".meta") != entry.metaHash)
                        throw new InvalidDataException("Identity second-save/no-op or metadata drift: " + entry.path);
                    RequireSceneState(result.sceneState);
                    result.entries.Add(entry);
                    Write(Path.Combine(directory, Path.GetFileNameWithoutExtension(entry.path) + "-result.json"), JsonUtility.ToJson(entry, true));
                }
                result.status = result.noOp ? "VerifiedNoOpPendingTerra" : "VerifiedPendingTerra";
                string resultPath = Path.Combine(directory, "result.json");
                Write(resultPath, JsonUtility.ToJson(result, true));
                if (!result.noOp)
                {
                    Write(accepted, resultPath);
                    Write(accepted + ".sha256", Hash(resultPath));
                    File.Delete(pending);
                }
            }
            catch (Exception exception) { result.status = "Failed"; result.error = exception.ToString(); throw; }
            finally
            {
                Write(Path.Combine(directory, "result.json"), JsonUtility.ToJson(result, true));
                Debug.Log("[InfinityRP] Serialized identity migration " + result.status + ": " + directory);
            }
        }

        [MenuItem("Infinity/Validation/Migration/Test Serialized Identity Update On Copy")]
        static void TestCopy()
        {
            RequireIdle(NewAssembly);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(File.ReadAllText(Path.Combine(Root, "identity-native-prepared.txt"))));
            string directory = NewRun("identity-copy-test");
            string stage = "Assets/InfinityIdentityCopy_" + Guid.NewGuid().ToString("N");
            string state = SceneState();
            var result = new Result { status = "Preparing", sceneState = state };
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(stage));
                foreach (Entry entry in new[] { manifest.entries[0], manifest.entries[manifest.entries.Count - 1] })
                {
                    string sourceHash = Hash(entry.path);
                    string copy = stage + "/" + Path.GetFileName(entry.path);
                    if (!AssetDatabase.CopyAsset(entry.path, copy)) throw new IOException("Identity test copy failed.");
                    UpdateIdentifiers(copy, entry.identityCount);
                    NativeSummary actual = Capture(copy, directory, Path.GetFileNameWithoutExtension(copy), entry.guid);
                    if (actual.semanticHash != entry.expectedSemanticHash || actual.objectCount != entry.objectCount || Hash(entry.path) != sourceHash)
                        throw new InvalidDataException("Identity copy projection or source preservation failed: " + entry.path);
                    result.entries.Add(entry);
                }
                RequireSceneState(state);
                result.status = "CopyUpdateVerified";
            }
            catch (Exception exception) { result.status = "Failed"; result.error = exception.ToString(); throw; }
            finally
            {
                if (AssetDatabase.IsValidFolder(stage) && !AssetDatabase.DeleteAsset(stage))
                {
                    result.status = "Failed";
                    result.error += "\nOwned copy stage cleanup failed: " + stage;
                }
                string resultPath = Path.Combine(directory, "result.json");
                Write(resultPath, JsonUtility.ToJson(result, true));
                if (result.status == "CopyUpdateVerified")
                {
                    Write(Path.Combine(Root, "identity-copy-passed.txt"), resultPath);
                    Write(Path.Combine(Root, "identity-copy-passed.sha256"), Hash(resultPath));
                }
                Debug.Log("[InfinityRP] Serialized identity copy test " + result.status + ": " + directory);
            }
        }

        static void UpdateIdentifiers(string path, int expectedCount)
        {
            int changed = 0;
            if (path.EndsWith(".unity", StringComparison.Ordinal))
            {
                Scene active = SceneManager.GetActiveScene();
                Scene editable = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (GameObject root in editable.GetRootGameObjects())
                        foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            if (component == null) throw new InvalidDataException("Missing script in migration scene: " + path);
                            changed += UpdateIdentifier(component);
                        }
                    if (changed != expectedCount) throw new InvalidDataException("Identity update count differs: " + path + " / " + changed);
                    if (!EditorSceneManager.SaveScene(editable, path)) throw new IOException("Could not save explicit identity updates: " + path);
                }
                finally
                {
                    SceneManager.SetActiveScene(active);
                    EditorSceneManager.CloseScene(editable, true);
                }
            }
            else
            {
                foreach (UnityEngine.Object value in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (value is ScriptableObject) changed += UpdateIdentifier(value);
                if (changed != expectedCount) throw new InvalidDataException("Identity update count differs: " + path + " / " + changed);
                foreach (UnityEngine.Object value in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (value is ScriptableObject) AssetDatabase.SaveAssetIfDirty(value);
            }
        }

        static int UpdateIdentifier(UnityEngine.Object value)
        {
            using var serialized = new SerializedObject(value);
            SerializedProperty identifier = serialized.FindProperty("m_EditorClassIdentifier");
            if (identifier == null || !identifier.stringValue.Contains(OldAssembly)) return 0;
            if (value.GetType().Assembly.GetName().Name != NewAssembly)
                throw new InvalidDataException("Old serialized identifier did not reconnect to Infinity: " + value.name);
            identifier.stringValue = identifier.stringValue.Replace(OldAssembly, NewAssembly);
            if (!serialized.ApplyModifiedPropertiesWithoutUndo()) throw new InvalidDataException("Identity property update was not applied.");
            return 1;
        }

        static void RequireIdle(string assembly)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Use the idle existing EditMode editor.");
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/InfinityRenderPipelineAsset.cs");
            if (script == null || script.GetClass() == null || script.GetClass().Assembly.GetName().Name != assembly)
                throw new InvalidOperationException("Required live runtime assembly is not loaded: " + assembly);
        }
        static void RequireUnloadedScene(string path)
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
                if (SceneManager.GetSceneAt(i).path == path) throw new InvalidOperationException("Keep migration scenes unloaded: " + path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null || EditorUtility.IsDirty(importer)) throw new InvalidOperationException("Importer missing or dirty: " + path);
            if (path.EndsWith(".asset", StringComparison.Ordinal))
                foreach (ScriptableObject loaded in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                    if (AssetDatabase.GetAssetPath(loaded) == path && EditorUtility.IsDirty(loaded))
                        throw new InvalidOperationException("Loaded asset has unsaved changes: " + path);
        }
        static void Backup(string directory, string path)
        {
            string target = Path.Combine(directory, "before", path);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            Helper("WriteDurableBytes", target, File.ReadAllBytes(path));
            Helper("WriteDurableBytes", target + ".meta", File.ReadAllBytes(path + ".meta"));
            if (Hash(target) != Hash(path) || Hash(target + ".meta") != Hash(path + ".meta")) throw new IOException("Identity backup mismatch.");
        }
        static NativeSummary Capture(string path, string directory, string prefix, string guid)
        {
            string plain = Path.Combine(directory, prefix + ".native.txt"), precise = Path.Combine(directory, prefix + ".floatbits.txt");
            Helper("RunReader", path, plain, false); Helper("RunReader", path, precise, true);
            NativeSummary summary = Parse(plain, precise, guid, AssetDatabase.AssetPathToGUID(path));
            Write(Path.Combine(directory, prefix + ".summary.json"), JsonUtility.ToJson(summary, true));
            return summary;
        }
        static NativeSummary Parse(string plain, string precise, string guid, string assetGuid = null)
        {
            object value = typeof(AssemblyMigrationPreflight).GetMethod("ReadNativeDump", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { plain, precise, assetGuid ?? guid, guid, CancellationToken.None });
            return JsonUtility.FromJson<NativeSummary>(JsonUtility.ToJson(value));
        }
        static string NewRun(string prefix)
        {
            string path = Path.Combine(Root, prefix + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path); return path;
        }
        static object Helper(string name, params object[] args) => typeof(KnownSceneReferenceRepairs).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        static string Hash(string path) => (string)Helper("Hash", path);
        static void Write(string path, string value) => Helper("WriteDurableText", path, value);
        static string SceneState() => (string)typeof(AssemblyMigrationPreflight).GetMethod("SceneState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        static void RequireSceneState(string expected) { if (SceneState() != expected) throw new InvalidOperationException("Loaded scene/dirty state changed."); }
    }
}
