using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Editor
{
    internal static class SceneSchemaBatchMigration
    {
        const string Root = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation";
        const string ManifestPath = "/private/tmp/InfinityRP-validation-20260905T164500Z-T02/prepared-scene-schema/scene-schema-manifest.json";
        const string ManifestHash = "e28e6396c1b9e54ed3126b9d61b52df64729ffda3973c931ef868d101eb154eb";
        const string BoxManifestPath = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation/N01-20260906T134157Z/box-schema-manifest.json";
        const string BoxManifestHash = "583e902580f9e9203e879b8f1b7f47fa11d54be8898289434d3c78fd48f17bbc";
        static bool s_Running;
        [Serializable] sealed class Artifact { public string path, hash; }
        [Serializable] sealed class RemovedField { public string objectID, scriptGUID, field, beforeNative; public int classID; }
        [Serializable] sealed class ProjectionProof { public string summaryPath, summaryHash, semanticHash, result; public int objectCount; }
        [Serializable] sealed class ScriptProof { public string path, hash, guid, metaHash; }
        [Serializable] sealed class AssetEntry
        {
            public string path, guid, sourceHash, metaHash, sourceSemanticHash, canonicalSemanticHash, group;
            public bool allowed;
            public int objectCount;
            public List<Artifact> proofs;
            public Artifact nativeDeltaProof;
            public List<ScriptProof> componentSources;
            public List<RemovedField> removedFields;
            public ProjectionProof nonTargetProof;
            public ScriptProof lightComponentSource;
        }
        [Serializable] sealed class ValueChange { public string objectID, field, before, after; }
        [Serializable] sealed class NativeDeltaProof
        {
            public string result, sourceHash, canonicalSemanticHash;
            public int canonicalObjects;
            public bool preciseSharedValuesPreserved;
            public List<ValueChange> sharedValueChanges;
        }
        [Serializable] sealed class Manifest { public string sourceReport, sourceReportHash; public List<AssetEntry> assets; }
        [Serializable] sealed class NativeSummary { public int objectCount; public string semanticHash; }
        [Serializable] sealed class AssetResult
        {
            public string path, guid, beforeHash, afterHash, metadataHash, firstSaveHash, secondSaveHash;
            public string nativeSummaryPath, nativeSummaryHash, nativeSemanticHash;
            public bool passed;
        }
        [Serializable] sealed class BatchReport
        {
            public string utc, group, status, error, manifestHash, beforeSceneState, afterSceneState;
            public bool noOp, sourcePreservedOutsideDeclaredSchema;
            public List<AssetResult> assets = new List<AssetResult>();
        }

        public static void ValidationScenes() => Run("Validation");
        public static void BoxScenes() => Run("BoxMatrix");

        static void Run(string group)
        {
            if (s_Running) throw new InvalidOperationException("A scene-schema batch is already running.");
            s_Running = true;
            string directory = null;
            string pending = Path.Combine(Root, "scene-schema-" + group + "-pending.txt");
            string latest = Path.Combine(Root, "scene-schema-" + group + "-latest.txt");
            string manifestPath = group == "BoxMatrix" ? BoxManifestPath : ManifestPath;
            string manifestHash = group == "BoxMatrix" ? BoxManifestHash : ManifestHash;
            var report = new BatchReport { utc = DateTime.UtcNow.ToString("O"), group = group, status = "Preparing", manifestHash = manifestHash };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Use an idle EditMode editor.");
                if (Hash(manifestPath) != manifestHash) throw new InvalidDataException("Frozen scene schema plan changed.");
                Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
                if (Hash(manifest.sourceReport) != manifest.sourceReportHash) throw new InvalidDataException("Source checkpoint evidence changed.");
                AssetEntry[] selected = manifest.assets.Where(value => value.group == group).ToArray();
                if (selected.Length != 3 || selected.Any(value => !value.allowed))
                    throw new InvalidOperationException("This scene group has no complete authorized migration manifest.");
                report.beforeSceneState = CurrentSceneState();
                if (File.Exists(pending))
                {
                    ReadIntentPointer(pending);
                    throw new InvalidOperationException("The original scene-schema batch is still pending; retain its evidence and inspect it before any further source writes.");
                }
                BatchReport prior = null; string originalDirectory = null;
                if (selected.Any(value => Hash(value.path) != value.sourceHash))
                {
                    originalDirectory = ReadIntentPointer(latest);
                    prior = JsonUtility.FromJson<BatchReport>(File.ReadAllText(Path.Combine(originalDirectory, "report.json")));
                    if (prior.status != "SourceSchemaBatchVerifiedPendingTerra" || prior.noOp || prior.manifestHash != manifestHash || prior.group != group || prior.assets.Count != 3 || !prior.sourcePreservedOutsideDeclaredSchema)
                        throw new InvalidDataException("Current changed scene bytes are not bound to the original completed schema batch.");
                    report.noOp = true;
                }
                foreach (AssetEntry entry in selected)
                {
                    VerifyStaticProof(entry);
                    for (int index = 0; index < SceneManager.sceneCount; index++)
                        if (SceneManager.GetSceneAt(index).path == entry.path)
                            throw new InvalidOperationException("Keep these target scenes unloaded; the batch never activates or overwrites loaded scene state.");
                    AssetImporter importer = AssetImporter.GetAtPath(entry.path);
                    if (importer == null || EditorUtility.IsDirty(importer) || Hash(entry.path + ".meta") != entry.metaHash || AssetDatabase.AssetPathToGUID(entry.path) != entry.guid)
                        throw new InvalidOperationException("Target importer metadata or GUID changed: " + entry.path);
                    string current = Hash(entry.path);
                    if (!report.noOp && current != entry.sourceHash) throw new InvalidDataException("Source hash changed: " + entry.path);
                    if (report.noOp)
                    {
                        AssetResult previous = prior.assets.Single(value => value.path == entry.path);
                        if (!previous.passed || previous.afterHash != current || previous.guid != entry.guid || previous.metadataHash != entry.metaHash || previous.nativeSemanticHash != entry.canonicalSemanticHash)
                            throw new InvalidDataException("Scene is not the exact previously accepted canonical output: " + entry.path);
                    }
                    report.assets.Add(new AssetResult { path = entry.path, guid = entry.guid, beforeHash = current, metadataHash = entry.metaHash });
                }
                directory = Path.Combine(Root, "scene-schema-" + group + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(directory)) throw new IOException("Unique scene batch evidence already exists.");
                Directory.CreateDirectory(directory);
                foreach (AssetResult item in report.assets)
                {
                    string folder = Path.Combine(directory, "before", item.path); Directory.CreateDirectory(Path.GetDirectoryName(folder));
                    WriteBytes(folder, File.ReadAllBytes(item.path)); WriteBytes(folder + ".meta", File.ReadAllBytes(item.path + ".meta"));
                    if (Hash(folder) != item.beforeHash || Hash(folder + ".meta") != item.metadataHash) throw new IOException("Fresh durable backup failed.");
                }
                Write(Path.Combine(directory, "schema-plan.json"), File.ReadAllText(manifestPath));
                Write(Path.Combine(directory, "intent.json"), JsonUtility.ToJson(report, true));
                Write(Path.Combine(directory, "intent.sha256"), Hash(Path.Combine(directory, "intent.json")));
                if (!report.noOp) Write(pending, directory);
                foreach (AssetEntry entry in selected)
                {
                    AssetResult result = report.assets.Single(value => value.path == entry.path);
                    RequireSceneState(report.beforeSceneState);
                    if (Hash(entry.path) != result.beforeHash || Hash(entry.path + ".meta") != entry.metaHash)
                        throw new IOException("Source changed after backup: " + entry.path);
                    string prefix = Path.GetFileNameWithoutExtension(entry.path);
                    if (!report.noOp)
                    {
                        AssetDatabase.ForceReserializeAssets(new[] { entry.path }, ForceReserializeAssetsOptions.ReserializeAssets);
                        result.firstSaveHash = Hash(entry.path);
                        Write(Path.Combine(directory, prefix + "-first-save.sha256"), result.firstSaveHash);
                        AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    }
                    NativeSummary summary = CaptureNative(entry.path, directory, prefix, entry.guid);
                    if (summary.semanticHash != entry.canonicalSemanticHash || summary.objectCount != entry.objectCount)
                        throw new InvalidDataException("Source changed outside its exact accepted object/field schema delta: " + entry.path);
                    if (!report.noOp)
                    {
                        AssetDatabase.ForceReserializeAssets(new[] { entry.path }, ForceReserializeAssetsOptions.ReserializeAssets);
                        result.secondSaveHash = Hash(entry.path);
                        if (result.secondSaveHash != result.firstSaveHash) throw new InvalidDataException("Second source save changed bytes: " + entry.path);
                    }
                    else result.firstSaveHash = result.secondSaveHash = result.beforeHash;
                    result.afterHash = Hash(entry.path); result.nativeSemanticHash = summary.semanticHash;
                    result.nativeSummaryPath = Path.Combine(directory, prefix + ".summary.json"); result.nativeSummaryHash = Hash(result.nativeSummaryPath);
                    if (Hash(entry.path + ".meta") != entry.metaHash || AssetDatabase.AssetPathToGUID(entry.path) != entry.guid ||
                        EditorUtility.IsDirty(AssetImporter.GetAtPath(entry.path)) || (report.noOp && result.beforeHash != result.afterHash))
                        throw new InvalidDataException("Scene metadata/GUID or no-op bytes changed: " + entry.path);
                    RequireSceneState(report.beforeSceneState); result.passed = true;
                    Write(Path.Combine(directory, prefix + "-result.json"), JsonUtility.ToJson(result, true));
                }
                report.afterSceneState = CurrentSceneState(); RequireSceneState(report.beforeSceneState);
                report.sourcePreservedOutsideDeclaredSchema = true;
                report.status = report.noOp ? "VerifiedBatchNoOpPendingTerra" : "SourceSchemaBatchVerifiedPendingTerra";
                Write(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                Write(latest, report.noOp ? originalDirectory : directory);
                if (!report.noOp) File.Delete(pending);
            }
            catch (Exception error) { report.status = "Failed"; report.error = error.ToString(); throw; }
            finally
            {
                s_Running = false;
                if (directory != null) Write(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[InfinityRP] Scene schema batch " + report.status + ": " + directory);
            }
        }

        static void VerifyStaticProof(AssetEntry entry)
        {
            foreach (Artifact proof in entry.proofs) if (Hash(proof.path) != proof.hash) throw new InvalidDataException("Accepted native proof changed: " + proof.path);
            if (entry.group == "BoxMatrix")
            {
                VerifyBoxProof(entry);
                return;
            }
            if (entry.nonTargetProof == null || entry.nonTargetProof.result != "PASS" || Hash(entry.nonTargetProof.summaryPath) != entry.nonTargetProof.summaryHash ||
                entry.nonTargetProof.semanticHash != entry.canonicalSemanticHash || entry.nonTargetProof.objectCount != entry.objectCount ||
                Hash(entry.lightComponentSource.path) != entry.lightComponentSource.hash ||
                Hash(entry.lightComponentSource.path + ".meta") != entry.lightComponentSource.metaHash ||
                AssetDatabase.AssetPathToGUID(entry.lightComponentSource.path) != entry.lightComponentSource.guid || entry.removedFields.Count == 0)
                throw new InvalidDataException("Exact unused-field/non-target proof is incomplete: " + entry.path);
        }
        static void VerifyBoxProof(AssetEntry entry)
        {
            if (entry.nativeDeltaProof == null || Hash(entry.nativeDeltaProof.path) != entry.nativeDeltaProof.hash ||
                entry.nonTargetProof == null || entry.nonTargetProof.result != "PASS" ||
                Hash(entry.nonTargetProof.summaryPath) != entry.nonTargetProof.summaryHash ||
                entry.nonTargetProof.semanticHash != entry.canonicalSemanticHash ||
                entry.nonTargetProof.objectCount != entry.objectCount || entry.componentSources == null || entry.componentSources.Count != 3)
                throw new InvalidDataException("Box native delta proof is incomplete: " + entry.path);
            foreach (ScriptProof source in entry.componentSources)
                if (Hash(source.path) != source.hash || Hash(source.path + ".meta") != source.metaHash ||
                    AssetDatabase.AssetPathToGUID(source.path) != source.guid)
                    throw new InvalidDataException("Box component schema source changed: " + source.path);
            NativeDeltaProof proof = JsonUtility.FromJson<NativeDeltaProof>(File.ReadAllText(entry.nativeDeltaProof.path));
            if (proof.result != "PASS" || proof.sourceHash != entry.sourceHash || proof.canonicalSemanticHash != entry.canonicalSemanticHash ||
                proof.canonicalObjects != entry.objectCount || !proof.preciseSharedValuesPreserved || proof.sharedValueChanges == null)
                throw new InvalidDataException("Box proof does not bind this source/canonical pair: " + entry.path);
            bool mesh = entry.path.EndsWith("/BoxMatix_MeshPipeline.unity", StringComparison.Ordinal);
            if (proof.sharedValueChanges.Count != (mesh ? 2 : 1))
                throw new InvalidDataException("Unexpected approved GI delta count: " + entry.path);
            foreach (ValueChange change in proof.sharedValueChanges)
            {
                bool allowed = change.objectID == "780319870" && (mesh
                    ? (change.field == "/m_PVRFilteringGaussRadiusDirect" && change.before == "1 (float)" && change.after == "5 (float)") ||
                      (change.field == "/m_PVRFilteringGaussRadiusAO" && change.before == "2 (float)" && change.after == "5 (float)")
                    : change.field == "/m_PVRMinBounces" && change.before == "1 (int)" && change.after == "2 (int)");
                if (!allowed) throw new InvalidDataException("GI delta is outside the approved values: " + entry.path);
            }
        }

        static NativeSummary CaptureNative(string path, string directory, string prefix, string guid)
        {
            string plain = Path.Combine(directory, prefix + ".native.txt"), precise = Path.Combine(directory, prefix + ".floatbits.txt");
            InvokeHelper("RunReader", path, plain, false); InvokeHelper("RunReader", path, precise, true);
            MethodInfo parser = typeof(AssemblyMigrationPreflight).GetMethod("ReadNativeDump", BindingFlags.Static | BindingFlags.NonPublic);
            object result = parser.Invoke(null, new object[] { plain, precise, guid, guid, CancellationToken.None });
            string json = JsonUtility.ToJson(result, true); Write(Path.Combine(directory, prefix + ".summary.json"), json);
            return JsonUtility.FromJson<NativeSummary>(json);
        }
        static string ReadIntentPointer(string path)
        {
            string directory = File.ReadAllText(path);
            if (!Path.GetFullPath(directory).StartsWith(Root + "/scene-schema-", StringComparison.Ordinal) || !Directory.Exists(directory) ||
                Hash(Path.Combine(directory, "intent.json")) != File.ReadAllText(Path.Combine(directory, "intent.sha256")))
                throw new InvalidDataException("Original scene-schema receipt ownership/hash is invalid.");
            return directory;
        }
        static string CurrentSceneState() => (string)typeof(AssemblyMigrationPreflight).GetMethod("SceneState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        static void RequireSceneState(string expected) { if (CurrentSceneState() != expected) throw new InvalidOperationException("Already-loaded scene setup/dirty state changed; no scene is reopened or cleared."); }
        static object InvokeHelper(string name, params object[] args) => typeof(KnownSceneReferenceRepairs).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        static string Hash(string path) => (string)InvokeHelper("Hash", path);
        static void Write(string path, string value) => InvokeHelper("WriteDurableText", path, value);
        static void WriteBytes(string path, byte[] value) => InvokeHelper("WriteDurableBytes", path, value);
    }
}
