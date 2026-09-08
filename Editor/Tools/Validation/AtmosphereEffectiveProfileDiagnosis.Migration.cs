using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    internal static partial class AtmosphereEffectiveProfileDiagnosis
    {
        const string DiagnosisRun = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation/atmosphere-effective-20260906T1055571538840Z-788d2d64ab0f4b88bb76e4a0073c9cb2";
        const string DiagnosisReportHash = "248737c38b65c7ba5a1984444ad31ff2c9da5bf078c990e083244625799afdda";
        const string DiagnosisDeltaHash = "00eff2c0fa79538b43f6b390f99ed7847337a9c059d048c236cf93ab6b8e2bc8";
        const string CanonicalSourceHash = "c39b95596d4f6c385e583c54c37fa28dc4865b4aa35201ef17a0c1dfc67daa04";
        [Serializable] sealed class MigrationReport
        {
            public string utc, status, error, diagnosticReportHash, diagnosticDeltaHash, sourceBeforeHash, sourceAfterHash, metadataHash;
            public string pipelinePath, pipelineHash, pipelineMetadataHash, sourceIdentity, pipelineIdentity, sceneStateBefore, sceneStateAfter;
            public string firstSaveHash, secondSaveHash, profileSnapshotHash, parameterSnapshotHash;
            public bool noOp, recoveredPersistedSave, effectiveValuesPreserved, referencePreserved, metadataPreserved;
            public int sourceDirtyBefore, sourceDirtyAfter, pipelineDirtyBefore, pipelineDirtyAfter;
        }

        [MenuItem("Infinity/Validation/Migration/Canonicalize Validated Atmosphere Source")]
        public static void CanonicalizeSource()
        {
            if (s_Running) throw new InvalidOperationException("An Atmosphere operation is already running.");
            s_Running = true;
            string directory = null;
            string originalMigrationDirectory = null;
            string pending = Path.Combine(Root, "atmosphere-schema-pending.txt");
            string latest = Path.Combine(Root, "atmosphere-schema-latest.txt");
            var result = new MigrationReport { utc = DateTime.UtcNow.ToString("O"), status = "Preparing",
                diagnosticReportHash = DiagnosisReportHash, diagnosticDeltaHash = DiagnosisDeltaHash };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Canonicalization requires idle EditMode.");
                if (Hash(Path.Combine(DiagnosisRun, "report.json")) != DiagnosisReportHash || Hash(Path.Combine(DiagnosisRun, "legacy-field-delta.json")) != DiagnosisDeltaHash)
                    throw new InvalidOperationException("The accepted diagnosis/report delta changed.");
                Report diagnosis = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(DiagnosisRun, "report.json")));
                if (diagnosis.status != "EffectiveValuesEqualPendingTerra" || diagnosis.error.Length != 0 || !diagnosis.sourceValid ||
                    !diagnosis.currentPipelineUsesSource || !diagnosis.importedCopyEffectiveEquals || !diagnosis.canonicalCopyEffectiveEquals ||
                    !diagnosis.secondSaveByteIdentical || !diagnosis.sourcePreserved || !diagnosis.stageDeleted)
                    throw new InvalidOperationException("The diagnosis did not complete every required effective-equivalence gate.");
                string expected = File.ReadAllText(Path.Combine(DiagnosisRun, "source-effective.txt"));
                string parameters = File.ReadAllText(Path.Combine(DiagnosisRun, "source-parameters.txt"));
                if (TextHash(expected) != diagnosis.sourceProfileSnapshotHash || TextHash(parameters) != diagnosis.sourceParameterSnapshotHash)
                    throw new IOException("Accepted effective snapshots changed.");
                AtmosphericalProfile profile = AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(Source);
                var pipeline = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
                if (profile == null || pipeline == null || pipeline.atmosphericalProfile != profile || AssetDatabase.AssetPathToGUID(Source) != SourceGuid)
                    throw new InvalidOperationException("Current pipeline/profile reference authority changed.");
                AtmosphereParameter.FromProfile(profile).ThrowIfInvalid();
                if (Snapshot(profile) != expected || Parameters(profile) != parameters) throw new InvalidOperationException("Current effective values changed since the accepted diagnosis.");
                result.sourceBeforeHash = Hash(Source); result.metadataHash = Hash(Source + ".meta");
                if (result.metadataHash != SourceMetaHash || (result.sourceBeforeHash != SourceHash && result.sourceBeforeHash != CanonicalSourceHash))
                    throw new InvalidOperationException("Unexpected current source or metadata bytes.");
                result.sourceIdentity = Identity(profile); result.pipelineIdentity = Identity(pipeline);
                result.pipelinePath = AssetDatabase.GetAssetPath(pipeline); result.pipelineHash = Hash(result.pipelinePath); result.pipelineMetadataHash = Hash(result.pipelinePath + ".meta");
                string pipelineSnapshot = Snapshot(pipeline);
                result.sceneStateBefore = SceneState(); result.sourceDirtyBefore = EditorUtility.GetDirtyCount(profile); result.pipelineDirtyBefore = EditorUtility.GetDirtyCount(pipeline);
                result.profileSnapshotHash = TextHash(expected); result.parameterSnapshotHash = TextHash(parameters);
                if (result.sourceDirtyBefore != 0 || result.pipelineDirtyBefore != 0)
                    throw new InvalidOperationException("The validated profile and RP Asset must have no unsaved edits before the targeted source save.");
                result.noOp = result.sourceBeforeHash == CanonicalSourceHash;
                if (result.noOp)
                {
                    originalMigrationDirectory = ReadOriginalMigrationIntent(File.Exists(pending) ? pending : latest);
                    result.recoveredPersistedSave = File.Exists(pending);
                }
                if (!result.noOp && File.Exists(pending))
                {
                    ReadOriginalMigrationIntent(pending);
                    throw new InvalidOperationException("The original pre-save attempt is still pending. Its pointer and evidence are retained; new source writes are refused.");
                }
                directory = Path.Combine(Root, "atmosphere-schema-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(directory)) throw new IOException("Unique migration evidence already exists.");
                Directory.CreateDirectory(directory);
                WriteBytes(Path.Combine(directory, "source.before.asset"), File.ReadAllBytes(Source));
                WriteBytes(Path.Combine(directory, "source.before.asset.meta"), File.ReadAllBytes(Source + ".meta"));
                WriteBytes(Path.Combine(directory, "pipeline.before.asset"), File.ReadAllBytes(result.pipelinePath));
                WriteBytes(Path.Combine(directory, "pipeline.before.asset.meta"), File.ReadAllBytes(result.pipelinePath + ".meta"));
                if (Hash(Path.Combine(directory, "source.before.asset")) != result.sourceBeforeHash || Hash(Path.Combine(directory, "source.before.asset.meta")) != result.metadataHash ||
                    Hash(Path.Combine(directory, "pipeline.before.asset")) != result.pipelineHash || Hash(Path.Combine(directory, "pipeline.before.asset.meta")) != result.pipelineMetadataHash)
                    throw new IOException("Fresh durable asset backups failed verification.");
                Write(directory, "source-effective-before.txt", expected); Write(directory, "source-parameters-before.txt", parameters); Write(directory, "pipeline-effective-before.txt", pipelineSnapshot);
                Write(directory, "intent.json", JsonUtility.ToJson(result, true)); Write(directory, "intent.sha256", Hash(Path.Combine(directory, "intent.json")));
                if (!result.noOp)
                {
                    WriteMigrationPointer(pending, directory);
                    ValidateCurrent();
                    if (Hash(Source) != result.sourceBeforeHash) throw new IOException("Source changed after fresh backup.");
                    AssetDatabase.ForceReserializeAssets(new[] { Source }, ForceReserializeAssetsOptions.ReserializeAssets);
                    result.firstSaveHash = Hash(Source);
                    Write(directory, "first-save.sha256", result.firstSaveHash);
                    if (result.firstSaveHash != CanonicalSourceHash) throw new InvalidOperationException("Source canonical bytes differ from the accepted canonical copy; inspect retained evidence, no automatic overwrite is performed.");
                    AssetDatabase.ImportAsset(Source, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    ValidateCurrent();
                    AssetDatabase.ForceReserializeAssets(new[] { Source }, ForceReserializeAssetsOptions.ReserializeAssets);
                    result.secondSaveHash = Hash(Source);
                    if (result.secondSaveHash != result.firstSaveHash) throw new InvalidOperationException("Second source save is not byte-identical.");
                }
                else
                {
                    // A separate no-op/recovery invocation verifies the persisted state without writing it again.
                    result.firstSaveHash = result.secondSaveHash = result.sourceBeforeHash;
                }
                ValidateCurrent();
                Native(directory, "source-after", Source);
                ValidateCurrent();
                result.sourceAfterHash = Hash(Source);
                if (result.sourceAfterHash != CanonicalSourceHash) throw new IOException("Source bytes changed during final native capture.");
                result.status = result.recoveredPersistedSave ? "RecoveredPersistedCanonicalSourcePendingTerra" : result.noOp ? "VerifiedNoOpPendingTerra" : "CanonicalSourceVerifiedPendingTerra";
                Write(directory, "effective-after.txt", Snapshot(AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(Source)));
                Write(directory, "parameters-after.txt", Parameters(AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(Source)));
                Write(directory, "PASS-LIVE.txt", "Effective fields/FromProfile bits, current pipeline reference, metadata and scene state preserved; native schema delta and final acceptance require independent Terra.\n");
                WriteMigrationPointer(latest, result.noOp ? originalMigrationDirectory : directory);
                if (File.Exists(pending)) File.Delete(pending);

                void ValidateCurrent()
                {
                    AtmosphericalProfile current = AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(Source);
                    AtmosphereParameter.FromProfile(current).ThrowIfInvalid();
                    if (Snapshot(current) != expected || Parameters(current) != parameters) throw new InvalidOperationException("Effective source values changed.");
                    if (GraphicsSettings.currentRenderPipeline != pipeline || pipeline.atmosphericalProfile != current || Identity(current) != result.sourceIdentity ||
                        Snapshot(pipeline) != pipelineSnapshot || Hash(result.pipelinePath) != result.pipelineHash || Hash(result.pipelinePath + ".meta") != result.pipelineMetadataHash)
                        throw new InvalidOperationException("RP Asset reference or data changed.");
                    if (Hash(Source + ".meta") != result.metadataHash || AssetDatabase.AssetPathToGUID(Source) != SourceGuid || SceneState() != result.sceneStateBefore)
                        throw new InvalidOperationException("Source metadata/GUID or scene state changed.");
                    result.sourceDirtyAfter = EditorUtility.GetDirtyCount(current); result.pipelineDirtyAfter = EditorUtility.GetDirtyCount(pipeline);
                    if (result.sourceDirtyAfter != result.sourceDirtyBefore || result.pipelineDirtyAfter != result.pipelineDirtyBefore)
                        throw new InvalidOperationException("Source/RP Asset acquired unsaved edits.");
                    result.sceneStateAfter = SceneState(); result.effectiveValuesPreserved = result.referencePreserved = result.metadataPreserved = true;
                }
            }
            catch (Exception error) { result.status = "Failed"; result.error = error.ToString(); throw; }
            finally
            {
                s_Running = false;
                if (directory != null) Write(directory, "report.json", JsonUtility.ToJson(result, true));
                Debug.Log("[InfinityRP] Atmosphere source schema " + result.status + ": " + directory);
            }
        }
        static string ReadOriginalMigrationIntent(string pointer)
        {
            string prior = ReadMigrationPointer(pointer);
            string intentPath = Path.Combine(prior, "intent.json");
            if (Hash(intentPath) != File.ReadAllText(Path.Combine(prior, "intent.sha256"))) throw new IOException("Original migration intent changed.");
            MigrationReport intent = JsonUtility.FromJson<MigrationReport>(File.ReadAllText(intentPath));
            if (intent.sourceBeforeHash != SourceHash || intent.noOp || intent.diagnosticReportHash != DiagnosisReportHash ||
                intent.diagnosticDeltaHash != DiagnosisDeltaHash || intent.metadataHash != SourceMetaHash)
                throw new InvalidOperationException("Receipt is not the original accepted source migration intent.");
            return prior;
        }
        static string ReadMigrationPointer(string path)
        {
            string target = File.ReadAllText(path);
            if (!Path.GetFullPath(target).StartsWith(Root + "/atmosphere-schema-", StringComparison.Ordinal) || !Directory.Exists(target))
                throw new IOException("Unexpected migration receipt ownership.");
            return target;
        }
        static void WriteMigrationPointer(string path, string value)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try { WriteBytes(temporary, Encoding.UTF8.GetBytes(value)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
