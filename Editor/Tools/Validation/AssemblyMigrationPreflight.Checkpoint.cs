using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Editor
{
    internal static partial class AssemblyMigrationPreflight
    {
        const string CheckpointInput = "/private/tmp/InfinityRP-validation-20260905T164500Z-T02/prepared-checkpoint/checkpoint-input.json";
        const string CheckpointInputHash = "1b8ba3a195baa605144e9538b41cbb694ea452f5f896d7c1bebc2a86b4a18c67";
        [Serializable] sealed class ArtifactProof { public string path, hash; }
        [Serializable] sealed class SourceProof
        {
            public string assetPath, sourceSummary, sourceSummaryHash;
            public List<ArtifactProof> completedCopyArtifacts;
        }
        [Serializable] sealed class CheckpointInputData
        {
            public string reportPath, reportHash, originalImplementationHash, checkpointImplementationHash;
            public string replacementReceiptPath, replacementReceiptHash;
            public List<SourceProof> sourceProofs;
        }
        [Serializable] sealed class AtmosphereReplacementReceipt
        {
            public string gate, verdict, path, originalFullReportHash;
            public string sourceBeforeHash, sourceAfterHash, sourceBeforeMetaHash, sourceAfterMetaHash;
            public string sourceBackup, sourceBackupHash, schemaDeltaPath, schemaDeltaHash;
            public string beforeNativeSummaryPath, beforeNativeSummaryHash, afterNativeSummaryPath, afterNativeSummaryHash;
            public string migrationReportPath, migrationReportHash, noOpReportPath, noOpReportHash;
            public List<ArtifactProof> nativeArtifacts;
            public bool sourceEffectiveValuesPreserved, currentPipelineReferencePreserved, sceneStatePreserved;
        }
        [Serializable] sealed class AtmosphereMigrationState
        {
            public string status, error, sourceBeforeHash, sourceAfterHash, metadataHash, sourceIdentity;
            public string pipelinePath, pipelineHash, pipelineMetadataHash, pipelineIdentity, sceneStateBefore, sceneStateAfter;
            public string profileSnapshotHash, parameterSnapshotHash;
            public bool noOp, effectiveValuesPreserved, referencePreserved, metadataPreserved;
            public int sourceDirtyBefore, sourceDirtyAfter, pipelineDirtyBefore, pipelineDirtyAfter;
        }
        [Serializable] sealed class SummaryHeader { public string semanticHash; public int objectCount; }
        [Serializable] sealed class CheckpointAssetEvidence
        {
            public string path, sourceHash, metadataHash, sourceReport, sourceReportHash, sourceSummary, sourceSummaryHash;
            public string copyProof, status, error, replacementReceipt, replacementReceiptHash;
        }
        [Serializable] sealed class CheckpointComposite
        {
            public string sourceFailedReport, sourceFailedReportHash, checkpointManifestHash, extensionHash;
            public int reusedSourceNativeCount, reusedCompletedCopyCount, pendingCopyCount;
            public List<CheckpointAssetEvidence> assets = new List<CheckpointAssetEvidence>();
        }

        public static void ResumeCheckpoint()
        {
            if (s_Active != null) throw new InvalidOperationException("A preflight is already running.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Resume requires an idle EditMode editor.");
            s_Active = new PreflightSession(true);
            s_Active.Start();
        }

        sealed partial class PreflightSession
        {
            readonly bool m_IsCheckpoint;
            CheckpointComposite m_Composite;

            IEnumerable<int> CheckpointSteps()
            {
                if (HashFile(CheckpointInput) != CheckpointInputHash) throw new InvalidDataException("Frozen checkpoint manifest changed.");
                CheckpointInputData checkpoint = JsonUtility.FromJson<CheckpointInputData>(File.ReadAllText(CheckpointInput));
                if (HashFile(checkpoint.reportPath) != checkpoint.reportHash || m_Report.implementationHash != checkpoint.checkpointImplementationHash)
                    throw new InvalidDataException("Frozen source report or reviewed checkpoint implementation changed.");
                Report sourceReport = JsonUtility.FromJson<Report>(File.ReadAllText(checkpoint.reportPath));
                if (sourceReport.status != "Failed" || sourceReport.mode != "full" || sourceReport.passed || !sourceReport.stageRemoved ||
                    sourceReport.implementationHash != checkpoint.originalImplementationHash || sourceReport.errors.Count != 1 ||
                    !sourceReport.errors[0].StartsWith("Assets/Profile/AtmosphericalProfile.asset | Canonical copy save and reload |", StringComparison.Ordinal))
                    throw new InvalidDataException("Checkpoint does not describe the independently reviewed isolated Atmosphere failure.");
                m_Composite = new CheckpointComposite { sourceFailedReport = checkpoint.reportPath, sourceFailedReportHash = checkpoint.reportHash,
                    checkpointManifestHash = CheckpointInputHash, extensionHash = HashFile(Package + "Editor/Tools/Validation/AssemblyMigrationPreflight.Checkpoint.cs") };
                var proofs = checkpoint.sourceProofs.ToDictionary(value => value.assetPath, StringComparer.Ordinal);
                foreach (ScriptableObject value in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                {
                    if (IsProjectInput(AssetDatabase.GetAssetPath(value))) m_LoadedSourceDirty[value] = EditorUtility.GetDirtyCount(value);
                    yield return 0;
                }
                m_Report.phase = "Verify frozen current source and parsed evidence";
                foreach (AssetRecord record in sourceReport.assets)
                {
                    m_Report.currentPath = record.path;
                    if (!record.selected || !record.unchanged || HashFile(record.backup) != record.sourceHash || HashFile(record.backup + ".meta") != record.metaHash ||
                        HashFile(record.path + ".meta") != record.metaHash) throw new InvalidDataException("Source/backup metadata authority changed: " + record.path);
                    string current = HashFile(record.path);
                    SourceProof replacementProof = null;
                    if (current != record.sourceHash)
                    {
                        // The sole intentional replacement of an old input is this reviewed orchestration patch.
                        // ReadNativeDump/ReaderSteps and the shared copy body remain the original implementation.
                        if (record.path == "Assets/Profile/AtmosphericalProfile.asset")
                            replacementProof = ValidateAtmosphereReplacement(checkpoint, record, current, proofs[record.path]);
                        else if (record.path != Package + "Editor/Tools/Validation/AssemblyMigrationPreflight.cs" || current != checkpoint.checkpointImplementationHash)
                            throw new InvalidDataException("Checkpoint source bytes changed without a separate accepted replacement proof: " + record.path);
                        record.sourceHash = current;
                        if (replacementProof == null) record.semanticHash = current;
                        record.backup = Path.Combine(m_Output, "bytes", record.path);
                        Directory.CreateDirectory(Path.GetDirectoryName(record.backup));
                        File.Copy(record.path, record.backup, false); File.Copy(record.path + ".meta", record.backup + ".meta", false);
                    }
                    m_Report.assets.Add(record);
                    var evidence = new CheckpointAssetEvidence { path = record.path, sourceHash = record.sourceHash, metadataHash = record.metaHash,
                        sourceReport = checkpoint.reportPath, sourceReportHash = checkpoint.reportHash, status = "ReusedUnchangedSource" };
                    if (record.native)
                    {
                        SourceProof proof = replacementProof ?? (proofs.TryGetValue(record.path, out SourceProof originalProof) ? originalProof : null);
                        if (proof == null || HashFile(proof.sourceSummary) != proof.sourceSummaryHash)
                            throw new InvalidDataException("Frozen parsed source summary changed: " + record.path);
                        SummaryHeader header = JsonUtility.FromJson<SummaryHeader>(File.ReadAllText(proof.sourceSummary));
                        if (header.objectCount <= 0 || header.semanticHash != record.semanticHash)
                            throw new InvalidDataException("Source summary semantic authority mismatch: " + record.path);
                        if (replacementProof != null) { evidence.replacementReceipt = checkpoint.replacementReceiptPath; evidence.replacementReceiptHash = checkpoint.replacementReceiptHash; }
                        evidence.sourceSummary = proof.sourceSummary; evidence.sourceSummaryHash = proof.sourceSummaryHash;
                        m_Composite.reusedSourceNativeCount++;
                        if (CopyAlreadyPassed(record))
                        {
                            if (proof.completedCopyArtifacts == null || proof.completedCopyArtifacts.Count != 3)
                                throw new InvalidDataException("Completed copy has incomplete frozen evidence: " + record.path);
                            foreach (ArtifactProof artifact in proof.completedCopyArtifacts)
                                if (HashFile(artifact.path) != artifact.hash) throw new InvalidDataException("Completed copy artifact changed: " + artifact.path);
                            evidence.copyProof = checkpoint.reportPath + "#" + record.path;
                            evidence.status = "ReusedCompletedCopy"; m_Composite.reusedCompletedCopyCount++;
                        }
                    }
                    m_Composite.assets.Add(evidence);
                    VerifyPreservation(record);
                    SaveComposite();
                    yield return 0;
                }
                // Only explicitly added diagnostic source files may extend the old AssetDatabase input list.
                var existing = new HashSet<string>(m_Report.assets.Select(value => value.path), StringComparer.Ordinal);
                var additions = new HashSet<string>(StringComparer.Ordinal)
                {
                    Package + "Editor/Tools/Validation/AtmosphereEffectiveProfileDiagnosis.cs",
                    Package + "Editor/Tools/Validation/AtmosphereEffectiveProfileDiagnosis.Migration.cs",
                    Package + "Editor/Tools/Validation/AssemblyMigrationPreflight.Checkpoint.cs"
                };
                foreach (string path in AssetDatabase.GetAllAssetPaths().Where(IsProjectInput).OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (existing.Contains(path)) continue;
                    if (!additions.Contains(path)) throw new InvalidDataException("New input needs explicit classification: " + path);
                    AssetImporter importer = AssetImporter.GetAtPath(path);
                    if (!(importer is MonoImporter)) throw new InvalidDataException("Expected the explicitly added diagnostic MonoScript: " + path);
                    var record = new AssetRecord { path = path, guid = AssetDatabase.AssetPathToGUID(path), importer = importer.GetType().FullName,
                        native = false, selected = true, sourceHash = HashFile(path), metaHash = HashFile(path + ".meta"), classification = "New explicit diagnostic MonoScript; importer metadata checked" };
                    var values = new List<string>(); AppendObject(importer, values, record, false, new HashSet<string>(), record.guid);
                    if (record.affected) throw new InvalidDataException("New imported input requires a separate migration gate: " + path);
                    record.semanticHash = record.sourceHash; record.backup = Path.Combine(m_Output, "bytes", path);
                    Directory.CreateDirectory(Path.GetDirectoryName(record.backup)); File.Copy(path, record.backup, false); File.Copy(path + ".meta", record.backup + ".meta", false);
                    m_Report.assets.Add(record); VerifyPreservation(record);
                    yield return 0;
                }
                m_Report.inputCount = m_Report.assets.Count;
                m_Report.coveredImporters = m_Report.assets.Select(value => value.importer).Distinct().OrderBy(value => value, StringComparer.Ordinal).ToList();
                AssetRecord[] pending = m_Report.assets.Where(value => value.native && value.affected && !CopyAlreadyPassed(value)).ToArray();
                m_Composite.pendingCopyCount = pending.Length;
                if (m_Composite.reusedSourceNativeCount != 88 || m_Composite.reusedCompletedCopyCount != 1 || pending.Length != 15)
                    throw new InvalidDataException("The exact 88-source/one-completed/15-pending checkpoint set changed.");
                SaveComposite();
                string stage = m_Report.stage;
                if (Directory.Exists(stage) || AssetDatabase.IsValidFolder(stage)) throw new IOException("Checkpoint stage collision.");
                string guid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(stage));
                if (string.IsNullOrEmpty(guid) || AssetDatabase.GUIDToAssetPath(guid) != stage) throw new IOException("Checkpoint stage creation failed.");
                m_Report.stageCreated = true;
                using (var stream = new FileStream(stage + "/owner.txt", FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) { writer.Write(m_Token); writer.Flush(); stream.Flush(true); }
                foreach (AssetRecord record in pending)
                {
                    var evidence = m_Composite.assets.Single(value => value.path == record.path);
                    record.initialCopyHash = record.roundtripHash = record.savedBytesHash = record.secondSaveBytesHash = null;
                    Exception failure = null;
                    using (IEnumerator<int> operation = CopyAssetSteps(record).GetEnumerator())
                    {
                        while (true)
                        {
                            bool more = false;
                            try { more = operation.MoveNext(); }
                            catch (Exception error) { failure = error; }
                            if (failure != null || !more) break;
                            yield return 0;
                        }
                    }
                    if (failure != null)
                    {
                        // Continue only after ordinary per-asset comparison failure with no live worker.
                        // Cancellation/timeouts/worker errors retain the original whole-session drain path.
                        if (failure is OperationCanceledException || failure is TimeoutException || m_ReaderProcess != null ||
                            (m_ParseTask != null && (!m_ParseTask.IsCompleted || m_ParseTask.IsFaulted)) || m_WorkerClock != null)
                            throw failure;
                        evidence.status = "FailedCopyGate"; evidence.error = failure.ToString();
                        m_Report.errors.Add(record.path + " | " + m_Report.phase + " | " + failure);
                    }
                    else
                    {
                        if (!CopyAlreadyPassed(record)) throw new InvalidDataException("Copy operation ended without its full gate: " + record.path);
                        evidence.status = "FreshCompletedCopy"; evidence.copyProof = Path.Combine(m_Output, "report.json") + "#" + record.path;
                    }
                    VerifyPreservation(record); SaveComposite(); yield return 0;
                }
                m_Report.phase = "Verify composite source preservation";
                foreach (AssetRecord record in m_Report.assets) { VerifyPreservation(record); yield return 0; }
                SaveComposite();
            }

            SourceProof ValidateAtmosphereReplacement(CheckpointInputData checkpoint, AssetRecord record, string currentHash, SourceProof original)
            {
                if (string.IsNullOrEmpty(checkpoint.replacementReceiptPath) || string.IsNullOrEmpty(checkpoint.replacementReceiptHash) ||
                    HashFile(checkpoint.replacementReceiptPath) != checkpoint.replacementReceiptHash)
                    throw new InvalidDataException("The exact Atmosphere source replacement lacks frozen acceptance evidence.");
                AtmosphereReplacementReceipt receipt = JsonUtility.FromJson<AtmosphereReplacementReceipt>(File.ReadAllText(checkpoint.replacementReceiptPath));
                if (receipt.gate != "T02c-atmosphere-schema-migration" || receipt.verdict != "PASS" || receipt.path != "Assets/Profile/AtmosphericalProfile.asset" ||
                    receipt.originalFullReportHash != checkpoint.reportHash || receipt.sourceBeforeHash != record.sourceHash || receipt.sourceAfterHash != currentHash ||
                    receipt.sourceBeforeMetaHash != record.metaHash || receipt.sourceAfterMetaHash != record.metaHash ||
                    receipt.sourceBackupHash != record.sourceHash || HashFile(receipt.sourceBackup) != receipt.sourceBackupHash ||
                    receipt.beforeNativeSummaryPath != original.sourceSummary || receipt.beforeNativeSummaryHash != original.sourceSummaryHash ||
                    HashFile(receipt.beforeNativeSummaryPath) != receipt.beforeNativeSummaryHash || HashFile(receipt.afterNativeSummaryPath) != receipt.afterNativeSummaryHash ||
                    receipt.schemaDeltaHash != "00eff2c0fa79538b43f6b390f99ed7847337a9c059d048c236cf93ab6b8e2bc8" || HashFile(receipt.schemaDeltaPath) != receipt.schemaDeltaHash ||
                    HashFile(receipt.migrationReportPath) != receipt.migrationReportHash || !receipt.sourceEffectiveValuesPreserved ||
                    !receipt.currentPipelineReferencePreserved || !receipt.sceneStatePreserved)
                    throw new InvalidDataException("Atmosphere replacement certificate does not bind the exact accepted source/schema/state transition.");
                if (string.IsNullOrEmpty(receipt.noOpReportPath) || HashFile(receipt.noOpReportPath) != receipt.noOpReportHash)
                    throw new InvalidDataException("Accepted independent Atmosphere no-op receipt changed.");
                AtmosphereMigrationState migration = JsonUtility.FromJson<AtmosphereMigrationState>(File.ReadAllText(receipt.migrationReportPath));
                AtmosphereMigrationState noOp = JsonUtility.FromJson<AtmosphereMigrationState>(File.ReadAllText(receipt.noOpReportPath));
                if (migration.status != "CanonicalSourceVerifiedPendingTerra" || migration.noOp || !string.IsNullOrEmpty(migration.error) ||
                    migration.sourceBeforeHash != receipt.sourceBeforeHash || migration.sourceAfterHash != receipt.sourceAfterHash ||
                    noOp.status != "VerifiedNoOpPendingTerra" || !noOp.noOp || !string.IsNullOrEmpty(noOp.error) ||
                    noOp.sourceBeforeHash != receipt.sourceAfterHash || noOp.sourceAfterHash != receipt.sourceAfterHash ||
                    noOp.metadataHash != receipt.sourceAfterMetaHash || migration.metadataHash != receipt.sourceBeforeMetaHash ||
                    !noOp.effectiveValuesPreserved || !noOp.referencePreserved || !noOp.metadataPreserved ||
                    !migration.effectiveValuesPreserved || !migration.referencePreserved || !migration.metadataPreserved ||
                    noOp.sourceDirtyBefore != 0 || noOp.sourceDirtyAfter != 0 || noOp.pipelineDirtyBefore != 0 || noOp.pipelineDirtyAfter != 0 ||
                    migration.sourceDirtyBefore != 0 || migration.sourceDirtyAfter != 0 || migration.pipelineDirtyBefore != 0 || migration.pipelineDirtyAfter != 0 ||
                    string.IsNullOrEmpty(noOp.sceneStateBefore) || noOp.sceneStateBefore != noOp.sceneStateAfter || migration.sceneStateBefore != migration.sceneStateAfter ||
                    noOp.sourceIdentity != record.guid + ":11400000" || noOp.sourceIdentity != migration.sourceIdentity ||
                    noOp.pipelinePath != migration.pipelinePath || noOp.pipelineHash != migration.pipelineHash ||
                    noOp.pipelineMetadataHash != migration.pipelineMetadataHash || noOp.pipelineIdentity != migration.pipelineIdentity ||
                    noOp.profileSnapshotHash != migration.profileSnapshotHash || noOp.parameterSnapshotHash != migration.parameterSnapshotHash)
                    throw new InvalidDataException("Atmosphere migration/no-op no longer proves the exact persisted/effective/reference/dirty/scene transition.");
                string artifactRoot = Path.GetDirectoryName(receipt.afterNativeSummaryPath);
                var requiredArtifacts = new HashSet<string>(StringComparer.Ordinal)
                {
                    Path.Combine(artifactRoot, "source-after.native.txt"), Path.Combine(artifactRoot, "source-after.floatbits.txt"),
                    receipt.afterNativeSummaryPath, Path.Combine(artifactRoot, "summary-extraction-proof.json"), Path.Combine(artifactRoot, "summary-parser-exact.cs")
                };
                if (receipt.nativeArtifacts == null || receipt.nativeArtifacts.Count != 5 ||
                    !requiredArtifacts.SetEquals(receipt.nativeArtifacts.Select(artifact => artifact.path)))
                    throw new InvalidDataException("Atmosphere native evidence must contain exactly the five accepted artifacts.");
                foreach (ArtifactProof artifact in receipt.nativeArtifacts)
                {
                    if (HashFile(artifact.path) != artifact.hash || (artifact.path == receipt.afterNativeSummaryPath && artifact.hash != receipt.afterNativeSummaryHash))
                        throw new InvalidDataException("Accepted Atmosphere native artifact changed: " + artifact.path);
                }
                ReaderResult before = JsonUtility.FromJson<ReaderResult>(File.ReadAllText(receipt.beforeNativeSummaryPath));
                ReaderResult after = JsonUtility.FromJson<ReaderResult>(File.ReadAllText(receipt.afterNativeSummaryPath));
                if (before.objectCount != 1 || after.objectCount != before.objectCount || before.semanticHash != record.semanticHash ||
                    !before.objectIds.SequenceEqual(after.objectIds) || !before.scripts.SequenceEqual(after.scripts) ||
                    after.managedMetadata.Count != 0 || !before.classIdentifiers.SequenceEqual(after.classIdentifiers) || string.IsNullOrEmpty(after.semanticHash))
                    throw new InvalidDataException("Atmosphere replacement changed object/script identity or lacks complete new native semantics.");
                record.semanticHash = after.semanticHash; record.objectKeys = after.objectIds;
                record.classIdentifiers = after.classIdentifiers; record.managedReferences = after.managedMetadata;
                return new SourceProof { assetPath = record.path, sourceSummary = receipt.afterNativeSummaryPath,
                    sourceSummaryHash = receipt.afterNativeSummaryHash, completedCopyArtifacts = new List<ArtifactProof>() };
            }

            static bool CopyAlreadyPassed(AssetRecord record) => record.native && record.affected && !string.IsNullOrEmpty(record.semanticHash) &&
                record.semanticHash == record.initialCopyHash && record.semanticHash == record.roundtripHash &&
                !string.IsNullOrEmpty(record.savedBytesHash) && record.savedBytesHash == record.secondSaveBytesHash;
            void SaveComposite() => File.WriteAllText(Path.Combine(m_Output, "composite.json"), JsonUtility.ToJson(m_Composite, true));
            IEnumerable<int> CopyAssetSteps(AssetRecord record)
            {
                string stage = m_Report.stage;
                    m_Report.currentPath = record.path;
                    m_Report.phase = "Import isolated native copy";
                    string index = Index(record);
                    string directory = stage + "/" + index;
                    string directoryGuid = AssetDatabase.CreateFolder(stage, index);
                    if (string.IsNullOrEmpty(directoryGuid) || AssetDatabase.GUIDToAssetPath(directoryGuid) != directory) throw new IOException("Copy folder creation failed.");
                    string copy = directory + "/" + Path.GetFileName(record.path);
                    File.Copy(record.backup, copy, false);
                    if (HashFile(copy) != record.sourceHash) throw new IOException("Copy differs from source backup.");
                    yield return 0;
                    AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                    string copyGuid = AssetDatabase.AssetPathToGUID(copy);
                    if (string.IsNullOrEmpty(copyGuid) || copyGuid == record.guid) throw new InvalidDataException("Copy GUID is invalid.");
                    yield return 0;
                    m_Report.phase = "Compare source to imported copy";
                    foreach (int step in ReaderSteps(copy, copyGuid, record.guid, new AssetRecord(), index + "-copy-import")) yield return step;
                    record.initialCopyHash = m_ParseTask.Result.semanticHash;
                    if (record.semanticHash != record.initialCopyHash) throw new InvalidDataException("Source-to-copy object IDs, metadata or semantics differ.");
                    m_Report.phase = "Canonical copy save and reload";
                    AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);
                    yield return 0;
                    AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    yield return 0;
                    foreach (int step in ReaderSteps(copy, copyGuid, record.guid, new AssetRecord(), index + "-copy-roundtrip")) yield return step;
                    record.roundtripHash = m_ParseTask.Result.semanticHash;
                    if (record.semanticHash != record.roundtripHash) throw new InvalidDataException("Canonical copy save/reload changed semantics.");
                    record.savedBytesHash = HashFile(copy);
                    File.Copy(copy, Path.Combine(m_Output, index + "-canonical-copy" + Path.GetExtension(copy)), false);
                    yield return 0;
                    m_Report.phase = "Second canonical copy save";
                    AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);
                    record.secondSaveBytesHash = HashFile(copy);
                    if (record.savedBytesHash != record.secondSaveBytesHash) throw new InvalidDataException("Second copy save is not byte-idempotent.");
                    yield return 0;
            }
        }
    }
}
