using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor.Validation
{
    // A single explicitly reviewed asset operation; never called on load, validation or Play entry.
    public static class SpazonTintMigration
    {
        const string k_Asset = "Assets/Profile/PostProcessProfile.asset";
        const string k_Guid = "6120dfbb55a89ad41877ce889bb04749";
        const string k_ApprovedHash = "e5b9ffaa63da9165f435f6912d8d7dc4052add7aa81b02b6689b951662dabe92";
        const string k_ABRun = "InfinityRP-T06a-TintAB-20260905T2007119133930Z";
        const string k_Review = "/private/tmp/InfinityRP-T06a-TintAB-20260905T2007119133930Z/terra_t06a_verify_20260906T0417Z/verdict.md";
        const string k_Latest = "InfinityRP-T06a1-TintMigration-latest.txt";
        const string k_Pending = "InfinityRP-T06a1-TintMigration-pending.txt";
        const string k_ValidatedCapture = "InfinityRP-T06a1-DirtyInspection-20260905T2058457137530Z/live.json";
        const string k_ValidatedCaptureHash = "64d43efa566829e329b923da268b044fd070ddb734f31391a9241c1aaa1401b5";

        public static void Migrate() => Run(false);

        public static void MigrateWithPostSaveTestFailure() => Run(true);

        static void Run(bool injectPostSaveFailure)
        {
            string run = null;
            ColorGrading changed = null;
            bool saveAttempted = false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Run the explicit Tint migration only in settled Edit mode.");
                if (AssetDatabase.AssetPathToGUID(k_Asset) != k_Guid)
                    throw new InvalidOperationException("Exact source profile GUID/path does not match the approved target.");
                string ab = Path.Combine(Path.GetTempPath(), k_ABRun);
                VerifyABReceipt(ab);
                if (TryRecoverPending()) return;
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Asset);
                if (profile == null || !profile.TryGet(out ColorGrading grading) || !grading.Tint.overrideState)
                    throw new InvalidOperationException("Expected ColorGrading with Tint override enabled is missing.");
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(k_Asset);
                SpazonProfileInspection.Report liveBefore = grading.Tint.value == 1
                    ? ValidateApprovedLiveState() : SpazonProfileInspection.Capture();
                if (grading.Tint.value != 1 && grading.Tint.value != 0)
                    throw new InvalidOperationException("Expected approved Tint 1, or previously migrated Tint 0.");

                string assetPath = Path.GetFullPath(k_Asset);
                string metaPath = assetPath + ".meta";
                string beforeHash = Hash(assetPath), beforeMetaHash = Hash(metaPath);
                string before = Snapshot(assets);
                bool noOp = grading.Tint.value == 0;
                if (noOp && injectPostSaveFailure)
                    throw new InvalidOperationException("Post-save failure injection requires the original Tint 1 migration.");
                if (!noOp && beforeHash != k_ApprovedHash)
                    throw new InvalidOperationException("Source bytes changed since the verified A/B; refusing a stale migration.");
                if (noOp)
                {
                    string previous = File.ReadAllText(Path.Combine(Path.GetTempPath(), k_Latest));
                    if (!File.Exists(Path.Combine(previous, "PASS.txt")) ||
                        File.ReadAllText(Path.Combine(previous, "after.sha256")) != beforeHash ||
                        File.ReadAllText(Path.Combine(previous, "non-tint-after.txt")) != before)
                        throw new InvalidOperationException("Tint 0 does not match the previous successful migration receipt.");
                }
                var scenes = new Dictionary<Scene, (bool dirty, string hash)>();
                for (int i = 0; i < SceneManager.sceneCount; ++i)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    scenes.Add(scene, (scene.isDirty, string.IsNullOrEmpty(scene.path) ? null : Hash(Path.GetFullPath(scene.path))));
                }
                run = Path.Combine(Path.GetTempPath(), "InfinityRP-T06a1-TintMigration-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
                Directory.CreateDirectory(run);
                WriteDurableBytes(Path.Combine(run, "PostProcessProfile.asset.before"), File.ReadAllBytes(assetPath));
                WriteDurableBytes(Path.Combine(run, "PostProcessProfile.asset.meta.before"), File.ReadAllBytes(metaPath));
                if (Hash(Path.Combine(run, "PostProcessProfile.asset.before")) != beforeHash ||
                    Hash(Path.Combine(run, "PostProcessProfile.asset.meta.before")) != beforeMetaHash)
                    throw new IOException("Fresh byte backup hash verification failed.");
                WriteDurableText(Path.Combine(run, "before.sha256"), beforeHash);
                WriteDurableText(Path.Combine(run, "non-tint-before.txt"), before);
                WriteDurableText(Path.Combine(run, "scenes-before.txt"), SceneSnapshot());
                WriteDurableText(Path.Combine(run, "live-before-dirty.json"), JsonUtility.ToJson(liveBefore, true));
                WriteDurableBytes(Path.Combine(run, "approved-AB-verdict.md"), File.ReadAllBytes(k_Review));
                WriteDurableText(Path.Combine(run, "manifest.txt"), $"UTC={DateTime.UtcNow:O}\nUnity={Application.unityVersion}\nasset={assetPath}\nGUID={k_Guid}\nA-B-receipt={ab}\nA-B-review={k_Review}\nA-B-review-SHA256={Hash(k_Review)}\nnoOp={noOp}\nonly permitted change=ColorGrading.Tint.value 1 to 0; overrideState stays true\nassetBefore={beforeHash}\nmetaBefore={beforeMetaHash}\nvalidatedLiveCaptureSHA256={k_ValidatedCaptureHash}\nsourceDirtyBefore={DirtySummary(liveBefore)}\n");

                if (!noOp)
                {
                    // Durable recovery authority exists before the first source mutation.
                    WriteDurableText(Path.Combine(Path.GetTempPath(), k_Pending), run);
                    if (Hash(assetPath) != beforeHash || Hash(metaPath) != beforeMetaHash)
                        throw new InvalidOperationException("Source disk changed after receipt verification; refusing mutation.");
                    changed = grading;
                    grading.Tint.value = 0;
                    if (Snapshot(assets) != before) throw new InvalidOperationException("A non-Tint field changed before saving.");
                    EditorUtility.SetDirty(grading);
                    saveAttempted = true;
                    AssetDatabase.SaveAssetIfDirty(grading);
                    WriteDurableText(Path.Combine(run, "post-save.sha256"), Hash(assetPath));
                    if (injectPostSaveFailure)
                        throw new IOException("Injected post-save test failure; invoke the normal migration menu to verify recovery without rewriting the asset.");
                }
                AssetDatabase.ImportAsset(k_Asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var reopened = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Asset);
                if (reopened == null || !reopened.TryGet(out ColorGrading persisted) || persisted.Tint.value != 0 || !persisted.Tint.overrideState)
                    throw new InvalidOperationException("Reimported source Tint/override failed verification.");
                UnityEngine.Object[] afterAssets = AssetDatabase.LoadAllAssetsAtPath(k_Asset);
                string after = Snapshot(afterAssets);
                if (after != before) throw new InvalidOperationException("Reimport changed a non-Tint serialized field or object identity.");
                WriteDurableText(Path.Combine(run, "live-after-dirty.json"), JsonUtility.ToJson(SpazonProfileInspection.Capture(), true));
                if (Hash(metaPath) != beforeMetaHash || AssetDatabase.AssetPathToGUID(k_Asset) != k_Guid)
                    throw new InvalidOperationException("Source meta/GUID changed.");
                foreach (var pair in scenes)
                    if (!pair.Key.IsValid() || pair.Key.isDirty != pair.Value.dirty ||
                        (pair.Value.hash != null && Hash(Path.GetFullPath(pair.Key.path)) != pair.Value.hash))
                        throw new InvalidOperationException("An original scene's bytes or dirty state changed.");
                string afterHash = Hash(assetPath);
                if (noOp && afterHash != beforeHash) throw new InvalidOperationException("Second invocation changed source bytes.");
                WriteDurableText(Path.Combine(run, "after.sha256"), afterHash);
                WriteDurableText(Path.Combine(run, "non-tint-after.txt"), after);
                WriteDurableText(Path.Combine(run, "PASS.txt"), $"PASS targeted source Tint {(noOp ? "no-op" : "migration")}\npersistedTint=0\noverrideState=True\nnonTintFieldsAndGuidLocalIdsUnchanged=True\nsourceDirtyStateRecorded=True\nmetaUnchanged=True\nsceneBytesAndDirtyUnchanged=True\nnoOpBytesUnchanged={noOp}\n");
                WriteDurableText(Path.Combine(Path.GetTempPath(), k_Latest), run);
                ClearPending(run);
                Debug.Log($"Spazon source Tint {(noOp ? "no-op" : "migration")} PASS. Run the same menu again for the independent no-op invocation, then Play / Observe Source Only for 120 real frames. {run}");
            }
            catch (Exception error)
            {
                if (changed != null && !saveAttempted)
                {
                    changed.Tint.value = 1;
                }
                if (run != null) WriteDurableText(Path.Combine(run, "FAILED.txt"), error + "\nThe exact before bytes and durable pending receipt are preserved. Invoke the normal menu to verify and resume an interrupted save. No automatic whole-asset restore was attempted.\n");
                Debug.LogError($"Spazon Tint migration failed: {error}\nEvidence: {run ?? "preflight only; no source write"}");
            }
        }

        // Recovery never writes the source. First prove the in-memory non-Tint fields are
        // unchanged, then synchronously reopen disk and compare against the pre-write authority.
        static bool TryRecoverPending()
        {
            string pointer = Path.Combine(Path.GetTempPath(), k_Pending);
            if (!File.Exists(pointer)) return false;
            string run = File.ReadAllText(pointer);
            string assetPath = Path.GetFullPath(k_Asset);
            string metaPath = assetPath + ".meta";
            string backup = Path.Combine(run, "PostProcessProfile.asset.before");
            string backupMeta = Path.Combine(run, "PostProcessProfile.asset.meta.before");
            if (Hash(backup) != k_ApprovedHash || File.ReadAllText(Path.Combine(run, "before.sha256")) != k_ApprovedHash ||
                Hash(backupMeta) != Hash(metaPath))
                throw new InvalidOperationException("Pending migration backup/meta no longer matches the approved source; refusing recovery.");
            string expected = File.ReadAllText(Path.Combine(run, "non-tint-before.txt"));
            string expectedScenes = File.ReadAllText(Path.Combine(run, "scenes-before.txt"));
            var loaded = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Asset);
            if (loaded == null || !loaded.TryGet(out ColorGrading loadedGrading) || !loadedGrading.Tint.overrideState ||
                (loadedGrading.Tint.value != 0 && loadedGrading.Tint.value != 1) ||
                Snapshot(AssetDatabase.LoadAllAssetsAtPath(k_Asset)) != expected || SceneSnapshot() != expectedScenes)
                throw new InvalidOperationException("Concurrent/user non-Tint, source identity, Tint, or scene changes block recovery; no source write performed.");
            string diskBefore = Hash(assetPath);
            string savedHashPath = Path.Combine(run, "post-save.sha256");
            if (File.Exists(savedHashPath) && File.ReadAllText(savedHashPath) != diskBefore)
                throw new InvalidOperationException("Source disk changed after the recorded save; refusing concurrent-change recovery.");
            AssetDatabase.ImportAsset(k_Asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var reopened = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_Asset);
            if (reopened == null || !reopened.TryGet(out ColorGrading persisted) || !persisted.Tint.overrideState)
                throw new InvalidOperationException("Pending migration persisted profile contract is invalid.");
            string after = Snapshot(AssetDatabase.LoadAllAssetsAtPath(k_Asset));
            if (Hash(assetPath) != diskBefore || Hash(metaPath) != Hash(backupMeta) ||
                AssetDatabase.AssetPathToGUID(k_Asset) != k_Guid || after != expected || SceneSnapshot() != expectedScenes)
                throw new InvalidOperationException("Pending disk state changed or failed preserved-field checks; recovery made no source write.");
            WriteDurableText(Path.Combine(run, "recovery-live-dirty.json"), JsonUtility.ToJson(SpazonProfileInspection.Capture(), true));
            if (persisted.Tint.value == 1 && diskBefore == k_ApprovedHash)
            {
                WriteDurableText(Path.Combine(run, "RECOVERED-ORIGINAL.txt"), "Source disk is still the exact approved original. No source write was performed during recovery. A fresh backed-up migration may proceed.\n");
                ClearPending(run);
                return false;
            }
            if (persisted.Tint.value != 0)
                throw new InvalidOperationException("Pending disk is neither the exact original nor a verified Tint-0 result.");
            WriteDurableText(Path.Combine(run, "RECOVERY.txt"), $"UTC={DateTime.UtcNow:O}\nrecoverySourceWrites=0\nreimportBytesUnchanged=True\nassetSHA256={diskBefore}\npreWriteNonTintManifestMatches=True\nmetaAndSceneUnchanged=True\nOriginal FAILED.txt is retained for the audit.\n");
            WriteDurableText(Path.Combine(run, "after.sha256"), diskBefore);
            WriteDurableText(Path.Combine(run, "non-tint-after.txt"), after);
            WriteDurableText(Path.Combine(run, "PASS.txt"), "PASS recovered targeted source Tint migration\npersistedTint=0\noverrideState=True\nnonTintFieldsAndGuidLocalIdsUnchanged=True\nsourceDirtyStateRecorded=True\nmetaUnchanged=True\nsceneBytesAndDirtyUnchanged=True\nrecoverySourceWrites=0\nSeparate no-op invocation is still required.\n");
            WriteDurableText(Path.Combine(Path.GetTempPath(), k_Latest), run);
            ClearPending(run);
            Debug.Log("Spazon Tint migration recovery PASS without source writes. Invoke the normal menu once more for the separate no-op gate. " + run);
            return true;
        }

        static void ClearPending(string run)
        {
            string pointer = Path.Combine(Path.GetTempPath(), k_Pending);
            if (File.Exists(pointer) && File.ReadAllText(pointer) == run) File.Delete(pointer);
        }

        static string SceneSnapshot()
        {
            var result = new StringBuilder();
            result.AppendLine("sceneCount=" + SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                result.AppendLine($"name={scene.name}; path={scene.path}; dirty={scene.isDirty}; SHA256={(string.IsNullOrEmpty(scene.path) ? "unsaved" : Hash(Path.GetFullPath(scene.path)))}");
            }
            return result.ToString();
        }

        static SpazonProfileInspection.Report ValidateApprovedLiveState()
        {
            string capturePath = Path.Combine(Path.GetTempPath(), k_ValidatedCapture);
            if (Hash(capturePath) != k_ValidatedCaptureHash)
                throw new InvalidOperationException("The independently verified live/disk equality receipt changed.");
            var approved = JsonUtility.FromJson<SpazonProfileInspection.Report>(File.ReadAllText(capturePath));
            var current = SpazonProfileInspection.Capture();
            string expected = PersistentLiveSignature(approved, out int approvedFields);
            string actual = PersistentLiveSignature(current, out int currentFields);
            if (approved.assetSHA256 != k_ApprovedHash || current.assetSHA256 != k_ApprovedHash ||
                current.assetPath != approved.assetPath || current.metaSHA256 != approved.metaSHA256 ||
                approved.objects.Count != 6 || current.objects.Count != 6 || approvedFields != 279 || currentFields != 279 || expected != actual)
                throw new InvalidOperationException("Fresh live profile differs from the certified 6-object/279-field disk equality proof. No source write permitted.");
            return current;
        }

        static string PersistentLiveSignature(SpazonProfileInspection.Report report, out int semanticFields)
        {
            semanticFields = 0;
            var objects = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in report.objects)
            {
                var atomic = new List<string>();
                foreach (var field in item.fields)
                    if (field.type.StartsWith("ObjectReference:", StringComparison.Ordinal) || field.type.StartsWith("String:", StringComparison.Ordinal)) atomic.Add(field.path);
                var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in item.fields)
                {
                    bool internalChild = false;
                    foreach (string parent in atomic)
                        if (field.path.StartsWith(parent + ".", StringComparison.Ordinal)) { internalChild = true; break; }
                    if (internalChild) continue;
                    if (field.value != "container") ++semanticFields;
                    fields.Add(field.path, JsonUtility.ToJson(field));
                }
                var value = new StringBuilder();
                value.AppendLine(item.type).AppendLine(item.name);
                foreach (var pair in fields) value.AppendLine(pair.Value);
                objects.Add(item.guid + "|" + item.localId, value.ToString());
            }
            var result = new StringBuilder();
            foreach (var pair in objects) result.AppendLine(pair.Key).AppendLine(pair.Value);
            return result.ToString();
        }

        static string DirtySummary(SpazonProfileInspection.Report report)
        {
            var result = new StringBuilder();
            foreach (var item in report.objects) result.Append(item.name).Append(':').Append(item.dirty).Append('/').Append(item.dirtyCount).Append(';');
            return result.ToString();
        }

        static void VerifyABReceipt(string run)
        {
            if (!File.Exists(k_Review) || !File.ReadAllText(k_Review).Contains("**Verdict: PASS for the bounded Tint diagnostic only.**"))
                throw new InvalidOperationException("Independent A/B verdict is missing.");
            string baseline = File.ReadAllText(Path.Combine(run, "before-source-integrity.txt"));
            if (!baseline.Contains(Path.GetFullPath(k_Asset) + " SHA256=" + k_ApprovedHash))
                throw new InvalidOperationException("A/B receipt does not bind the approved source bytes.");
            string[] phases = { "A-original", "B-Tint-zero", "A-restored" };
            for (int i = 0; i < phases.Length; ++i)
            {
                string ready = File.ReadAllText(Path.Combine(run, phases[i] + "-READY.txt"));
                if (!ready.Contains("MainCamera successful normal frames=120") || !ready.Contains("actualCameraStackTint=" + (i == 1 ? "0" : "1") + "\n") ||
                    File.ReadAllText(Path.Combine(run, phases[i] + "-source-integrity.txt")) != baseline)
                    throw new InvalidOperationException("A/B/A successful-frame/Tint/source-integrity receipt is incomplete.");
            }
            if (File.ReadAllText(Path.Combine(run, "after-cleanup-source-integrity.txt")) != baseline)
                throw new InvalidOperationException("A/B source cleanup differs from baseline.");
            string end = File.ReadAllText(Path.Combine(run, "END.txt"));
            if (!end.Contains("diagnosticSessionFailed=False") || !end.Contains("cleanupObjectsDestroyed=True"))
                throw new InvalidOperationException("A/B cleanup did not pass.");
        }

        // Canonical serialized leaf values include persistent GUID/local-ID references rather than
        // ephemeral instance IDs, so reimport and a second menu invocation remain comparable.
        static string Snapshot(UnityEngine.Object[] assets)
        {
            var objects = new SortedDictionary<long, string>();
            int gradingCount = 0;
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id) || guid != k_Guid)
                    throw new InvalidOperationException("Unexpected object identity in the target asset.");
                var text = new StringBuilder(asset.GetType().AssemblyQualifiedName + "\n");
                using (var serialized = new SerializedObject(asset))
                {
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        // Persistent references and full strings are already represented atomically.
                        // Their native runtime-ID / character children are not persistent fields.
                        enterChildren = property.propertyType != SerializedPropertyType.ObjectReference &&
                            property.propertyType != SerializedPropertyType.String;
                        text.Append(property.propertyPath).Append('|').Append(property.type).Append('|');
                        if (asset is ColorGrading && property.propertyPath == "Tint.m_Value")
                        {
                            ++gradingCount;
                            text.AppendLine("<only-approved-change>");
                            continue;
                        }
                        switch (property.propertyType)
                        {
                            case SerializedPropertyType.Generic: text.Append("container"); break;
                            case SerializedPropertyType.ObjectReference:
                                UnityEngine.Object reference = property.objectReferenceValue;
                                if (reference == null) text.Append("null");
                                else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string refGuid, out long refId)) text.Append(refGuid).Append(':').Append(refId);
                                else throw new InvalidOperationException("Nonpersistent serialized reference at " + property.propertyPath);
                                break;
                            case SerializedPropertyType.Integer: text.Append(property.longValue); break;
                            case SerializedPropertyType.ArraySize:
                            case SerializedPropertyType.Enum:
                            case SerializedPropertyType.LayerMask: text.Append(property.intValue); break;
                            case SerializedPropertyType.Float: text.Append(property.doubleValue.ToString("R", CultureInfo.InvariantCulture)); break;
                            case SerializedPropertyType.Boolean: text.Append(property.boolValue); break;
                            case SerializedPropertyType.String: text.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(property.stringValue))); break;
                            case SerializedPropertyType.Color:
                            case SerializedPropertyType.Vector2:
                            case SerializedPropertyType.Vector3:
                            case SerializedPropertyType.Vector4:
                            case SerializedPropertyType.Quaternion:
                            case SerializedPropertyType.Rect:
                            case SerializedPropertyType.Bounds: text.Append(JsonUtility.ToJson(property.boxedValue)); break;
                            default: throw new InvalidOperationException("Unsupported serialized field in targeted migration: " + property.propertyPath + " / " + property.propertyType);
                        }
                        text.AppendLine();
                    }
                }
                objects.Add(id, text.ToString());
            }
            if (gradingCount != 1) throw new InvalidOperationException("Exactly one serialized ColorGrading Tint value must exist.");
            var result = new StringBuilder();
            foreach (var pair in objects) result.AppendLine($"GUID={k_Guid}; localID={pair.Key}\n{pair.Value}");
            return result.ToString();
        }

        // Evidence files are flushed before their atomic installation. The pending pointer is
        // written last, after all pre-write backups/manifests, before changing the source value.
        static void WriteDurableText(string path, string text)
            => WriteDurableBytes(path, new UTF8Encoding(false).GetBytes(text));

        static void WriteDurableBytes(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
