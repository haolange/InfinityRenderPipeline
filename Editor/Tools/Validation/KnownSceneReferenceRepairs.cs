using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Editor
{
    // Explicit T02b recipes. No scene opens, saves or recovery run during editor loading.
    internal static class KnownSceneReferenceRepairs
    {
        const string Run = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation";
        const string Spazon = "Assets/Scene/Spazon/Scene_Spazon.unity";
        const string Landscape = "Assets/Scene/Landscape/Scene_Landscape.unity";
        const string SpazonHash = "c0ff33dc07b54f48fc7f7c0e0143bc03f24bdc5d444e942f2315467fb5204923";
        const string LandscapeHash = "41543a9c90175ad0de758dc612bd2a48f36589565251ffaea8866fe495fc991a";
        const string SpazonMetaHash = "36fb9d0329fc735eca7d4c5548b21000d75bc11c5d24744a92f7a6822f8154c1";
        const string LandscapeMetaHash = "b77f307afd9ad2c1b53ed1062c1752120a5b26593309de4c3389ac768b2feb60";
        const ulong AnimationId = 3506690171519106946;
        const string ClipGuid = "6705595d420bf244bb005e5dcb642dea";
        const long ClipId = -8122253741631232824;
        static bool s_Running;
        static readonly Dictionary<ulong, Type> s_Colliders = new Dictionary<ulong, Type>
        {
            {391624127, typeof(BoxCollider)}, {754246934, typeof(BoxCollider)},
            {1356408413, typeof(CapsuleCollider)}, {1527684400, typeof(SphereCollider)},
            {1577035127, typeof(CapsuleCollider)}, {1799731504, typeof(BoxCollider)},
            {1901996457, typeof(BoxCollider)}, {1924641251, typeof(BoxCollider)},
            {2025500247, typeof(SphereCollider)}, {2109532098, typeof(BoxCollider)}
        };

        [Serializable] sealed class Evidence
        {
            public string utc;
            public string scene;
            public string beforeHash;
            public string afterHash;
            public string metaHash;
            public string sceneGuid;
            public string nativeBeforeHash;
            public string nativeBeforeFloatBitsHash;
            public string nativeAfterPath;
            public string nativeAfterFloatBitsPath;
            public string nativeAfterHash;
            public string nativeAfterFloatBitsHash;
            public string status;
            public string error;
            public string sceneHandleBefore;
            public string sceneHandleReopened;
            public int changedComponents;
            public int hash128EncodingCases;
            public bool noOp;
            public bool reopened;
            public bool unrelatedLiveFieldsUnchanged;
            public SceneState beforeSaveState;
            public SceneState afterSaveState;
            public SceneState failureState;
        }

        [Serializable] sealed class SceneState
        {
            public string expectedPath;
            public bool targetFound;
            public string targetPath;
            public string targetHandle;
            public bool targetLoaded;
            public bool targetDirty;
            public int sceneCount;
            public string activePath;
            public string activeHandle;
        }

        public static void RepairActiveScene()
        {
            if (s_Running) throw new InvalidOperationException("A known-reference repair is already running.");
            s_Running = true;
            string directory = null;
            Evidence evidence = null;
            bool saveAttempted = false;
            int undoGroup = -1;
            try
            {
                if (!Directory.Exists(Run)) throw new DirectoryNotFoundException("The authorized persistent evidence directory must exist: " + Run);
                Scene scene = RequireScene();
                int hash128EncodingCases = VerifyHash128Encoding();
                string scenePath = scene.path;
                string beforeHash = Hash(scenePath);
                string metaHash = Hash(scenePath + ".meta");
                string originalHash = scenePath == Spazon ? SpazonHash : LandscapeHash;
                string originalMetaHash = scenePath == Spazon ? SpazonMetaHash : LandscapeMetaHash;
                if (metaHash != originalMetaHash) throw new InvalidOperationException("Scene metadata changed; re-inventory before this exact recipe.");
                string pending = Pointer(scenePath, "pending");
                if (File.Exists(pending)) { RecoverPending(scene, File.ReadAllText(pending)); return; }
                string before = Snapshot(scene);
                bool noOp = beforeHash != originalHash;
                if (noOp)
                {
                    string prior = ReadOwnedDirectory(Pointer(scenePath, "latest"));
                    Evidence receipt = JsonUtility.FromJson<Evidence>(File.ReadAllText(Path.Combine(prior, "repair.json")));
                    if (!File.Exists(Path.Combine(prior, "VERIFIED-LIVE.txt")) || receipt.scene != scenePath || receipt.afterHash != beforeHash ||
                        receipt.metaHash != metaHash || File.ReadAllText(Path.Combine(prior, "non-target-after.txt")) != before)
                        throw new InvalidOperationException("Changed scene bytes do not match the previous verified repair receipt.");
                    ValidateLayout(scene, true);
                }
                else ValidateLayout(scene, false);

                directory = Path.Combine(Run, "known-scene-repair-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(directory)) throw new IOException("Evidence run collision; no existing run may be overwritten.");
                Directory.CreateDirectory(directory);
                evidence = new Evidence { utc = DateTime.UtcNow.ToString("O"), scene = scenePath, beforeHash = beforeHash,
                    metaHash = metaHash, sceneGuid = AssetDatabase.AssetPathToGUID(scenePath), status = "Prepared", noOp = noOp,
                    sceneHandleBefore = scene.handle.ToString(), hash128EncodingCases = hash128EncodingCases };
                WriteDurableBytes(Path.Combine(directory, "scene.before"), File.ReadAllBytes(scenePath));
                WriteDurableBytes(Path.Combine(directory, "scene.meta.before"), File.ReadAllBytes(scenePath + ".meta"));
                if (Hash(Path.Combine(directory, "scene.before")) != beforeHash || Hash(Path.Combine(directory, "scene.meta.before")) != metaHash)
                    throw new IOException("Fresh byte backup verification failed.");
                WriteDurableText(Path.Combine(directory, "non-target-before.txt"), before);
                CaptureNative(Path.Combine(directory, "scene.before"), directory, "before", scenePath, noOp, evidence);
                WriteDurableText(Path.Combine(directory, "intent.json"), JsonUtility.ToJson(evidence, true));
                WriteDurableText(Path.Combine(directory, "intent.sha256"), Hash(Path.Combine(directory, "intent.json")));
                if (!noOp)
                {
                    // This durable pointer is installed last, before the first in-memory/source mutation.
                    WriteDurableText(pending, directory);
                    if (Hash(scenePath) != beforeHash || Hash(scenePath + ".meta") != metaHash)
                        throw new InvalidOperationException("Source changed after fresh backup; mutation refused.");
                    Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Repair diagnosed missing references");
                    evidence.changedComponents = Apply(scene);
                    ValidateLayout(scene, true);
                    if (Snapshot(scene) != before) throw new InvalidOperationException("A non-target serialized field changed before saving.");
                    // Finalize all edit/Undo bookkeeping before the source persistence boundary.
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.IncrementCurrentGroup();
                    ValidateLayout(scene, true);
                    if (Snapshot(scene) != before) throw new InvalidOperationException("A non-target field changed while finalizing the edit transaction.");
                    evidence.beforeSaveState = CaptureSceneState(scenePath);
                    saveAttempted = true;
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity did not save the specifically affected scene.");
                    evidence.afterSaveState = CaptureSceneState(scenePath);
                    WriteDurableText(Path.Combine(directory, "post-save.sha256"), Hash(scenePath));
                }
                VerifyByReopening(scene, directory, evidence, before);
                if (noOp && evidence.afterHash != beforeHash) throw new InvalidOperationException("The no-op invocation changed scene bytes.");
                Complete(directory, evidence);
                if (File.Exists(pending)) File.Delete(pending);
            }
            catch (Exception error)
            {
                if (!saveAttempted && undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                if (directory != null)
                {
                    if (evidence != null) { evidence.failureState = CaptureSceneState(evidence.scene); evidence.status = "Failed"; evidence.error = error.ToString(); WriteDurableText(Path.Combine(directory, "repair.json"), JsonUtility.ToJson(evidence, true)); }
                    WriteDurableText(Path.Combine(directory, "FAILED.txt"), error + "\nSource bytes are never automatically restored. Fresh backups and the pending receipt are retained. Recovery requires the active scene to be clean.\n");
                }
                Debug.LogError("[InfinityRP] Known-reference repair failed: " + error + "\nEvidence: " + directory);
            }
            finally { s_Running = false; }
        }

        static SceneState CaptureSceneState(string expectedPath)
        {
            Scene active = SceneManager.GetActiveScene();
            var state = new SceneState { expectedPath = expectedPath, sceneCount = SceneManager.sceneCount,
                activePath = active.path, activeHandle = active.handle.ToString() };
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (candidate.path != expectedPath) continue;
                state.targetFound = true; state.targetPath = candidate.path; state.targetHandle = candidate.handle.ToString();
                state.targetLoaded = candidate.isLoaded; state.targetDirty = candidate.isDirty;
                break;
            }
            return state;
        }

        static Scene RequireScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open only the intended scene in idle EditMode before this explicit repair/reopen operation.");
            Scene scene = SceneManager.GetActiveScene();
            if ((scene.path != Spazon && scene.path != Landscape) || !scene.isLoaded || scene.isDirty)
                throw new InvalidOperationException("The intended Spazon or Landscape scene must be loaded and have no unsaved scene edits.");
            AssetImporter importer = AssetImporter.GetAtPath(scene.path);
            if (importer == null || EditorUtility.IsDirty(importer))
                throw new InvalidOperationException("Scene importer metadata is missing or has unsaved edits.");
            return scene;
        }

        static void RecoverPending(Scene scene, string directory)
        {
            ValidateDirectory(directory);
            if (Hash(Path.Combine(directory, "intent.json")) != File.ReadAllText(Path.Combine(directory, "intent.sha256")))
                throw new InvalidOperationException("Pending repair intent hash changed.");
            Evidence intent = JsonUtility.FromJson<Evidence>(File.ReadAllText(Path.Combine(directory, "intent.json")));
            if (intent.scene != scene.path || Hash(Path.Combine(directory, "scene.before")) != intent.beforeHash ||
                Hash(Path.Combine(directory, "scene.meta.before")) != intent.metaHash || Hash(scene.path + ".meta") != intent.metaHash)
                throw new InvalidOperationException("Pending repair's scene/backup/meta authority changed.");
            if (Hash(Path.Combine(directory, "native-before.txt")) != intent.nativeBeforeHash ||
                Hash(Path.Combine(directory, "native-before-floatbits.txt")) != intent.nativeBeforeFloatBitsHash)
                throw new InvalidOperationException("Pending repair native-before evidence changed.");
            ValidateNative(File.ReadAllText(Path.Combine(directory, "native-before.txt")), intent.scene, false);
            string before = File.ReadAllText(Path.Combine(directory, "non-target-before.txt"));
            if (Snapshot(scene) != before) throw new InvalidOperationException("Pending repair has non-target in-memory changes; refusing to reopen over them.");
            if (Hash(scene.path) == intent.beforeHash)
            {
                ValidateLayout(scene, false);
                WriteDurableText(Path.Combine(directory, "RECOVERED-NOT-SAVED.txt"), "Disk is still the exact original. No source write or reopen was performed. Invoke again to apply the repair.\n");
                File.Delete(Pointer(intent.scene, "pending"));
                return;
            }
            VerifyByReopening(scene, directory, intent, before);
            Complete(directory, intent);
            File.Delete(Pointer(intent.scene, "pending"));
        }

        static void VerifyByReopening(Scene scene, string directory, Evidence evidence, string before)
        {
            if (scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Reopen requires the saved sole scene with no unsaved edits. State: " + JsonUtility.ToJson(CaptureSceneState(evidence.scene)));
            string hash = Hash(scene.path);
            Scene reopened = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            evidence.reopened = true; evidence.sceneHandleReopened = reopened.handle.ToString();
            if (reopened.isDirty || reopened.path != evidence.scene || Hash(reopened.path) != hash || Hash(reopened.path + ".meta") != evidence.metaHash ||
                AssetDatabase.AssetPathToGUID(reopened.path) != evidence.sceneGuid)
                throw new InvalidOperationException("Reopening changed source bytes, metadata, GUID or dirty state.");
            ValidateLayout(reopened, true);
            string after = Snapshot(reopened);
            if (after != before) throw new InvalidOperationException("Reopening changed a non-target field or persistent object identity.");
            WriteDurableText(Path.Combine(directory, "non-target-after.txt"), after);
            CaptureNative(reopened.path, directory, "after", reopened.path, true, evidence);
            if (Hash(reopened.path) != hash) throw new IOException("Scene changed during native-after capture.");
            evidence.unrelatedLiveFieldsUnchanged = true; evidence.afterHash = hash;
        }

        static void Complete(string directory, Evidence evidence)
        {
            evidence.status = evidence.noOp ? "VerifiedNoOpPendingTerra" : "ReopenedVerifiedPendingTerra";
            WriteDurableText(Path.Combine(directory, "repair.json"), JsonUtility.ToJson(evidence, true));
            WriteDurableText(Path.Combine(directory, "VERIFIED-LIVE.txt"), "PASS live targets, reopen, stable GUID/local IDs and non-target loaded-object semantics. Independent official native diff and visual/functional verification remain required.\n");
            WriteDurableText(Pointer(evidence.scene, "latest"), directory);
            Debug.Log("[InfinityRP] Known-reference repair " + evidence.status + ": " + directory + ". Invoke the same menu again for the independent no-op run.");
        }

        static int Apply(Scene scene)
        {
            Object[] objects = EnumerateObjects(scene).ToArray();
            if (scene.path == Spazon)
            {
                Animation animation = objects.OfType<Animation>().Single(value => Id(value) == AnimationId);
                Undo.RecordObject(animation, "Remove only the diagnosed missing animation registration");
                using (var serialized = new SerializedObject(animation))
                {
                    SerializedProperty clips = serialized.FindProperty("m_Animations");
                    clips.DeleteArrayElementAtIndex(0);
                    if (clips.arraySize == 2) clips.DeleteArrayElementAtIndex(0);
                    if (clips.arraySize != 1) throw new InvalidOperationException("Exactly one existing clip must remain.");
                    serialized.ApplyModifiedProperties();
                }
                return 1;
            }
            foreach (Collider collider in objects.OfType<Collider>().Where(value => s_Colliders.ContainsKey(Id(value))))
            {
                Undo.RecordObject(collider, "Clear only the diagnosed missing PhysicsMaterial reference");
                using (var serialized = new SerializedObject(collider))
                {
                    serialized.FindProperty("m_Material").objectReferenceValue = null;
                    serialized.ApplyModifiedProperties();
                }
            }
            return s_Colliders.Count;
        }

        static void ValidateLayout(Scene scene, bool repaired)
        {
            Object[] objects = EnumerateObjects(scene).ToArray();
            if (scene.path == Spazon)
            {
                Animation animation = objects.OfType<Animation>().Single(value => Id(value) == AnimationId);
                AssertValidClip(animation.clip);
                using (var serialized = new SerializedObject(animation))
                {
                    SerializedProperty clips = serialized.FindProperty("m_Animations");
                    if (clips == null || clips.arraySize != (repaired ? 1 : 2)) throw new InvalidOperationException("Unexpected animation registration count.");
                    if (!repaired && clips.GetArrayElementAtIndex(0).objectReferenceValue != null) throw new InvalidOperationException("The diagnosed missing clip now resolves; refusing to remove it.");
                    AssertValidClip(clips.GetArrayElementAtIndex(repaired ? 0 : 1).objectReferenceValue as AnimationClip);
                }
                return;
            }
            Collider[] colliders = objects.OfType<Collider>().Where(value => s_Colliders.ContainsKey(Id(value))).ToArray();
            if (colliders.Length != s_Colliders.Count) throw new InvalidOperationException("Expected the ten exact Collider identities.");
            foreach (Collider collider in colliders)
            {
                if (collider.GetType() != s_Colliders[Id(collider)]) throw new InvalidOperationException("A diagnosed Collider type changed.");
                using (var serialized = new SerializedObject(collider))
                {
                    SerializedProperty material = serialized.FindProperty("m_Material");
                    if (material == null || material.objectReferenceValue != null || (repaired && material.objectReferenceEntityIdValue != default(EntityId)))
                        throw new InvalidOperationException("PhysicsMaterial does not match the diagnosed missing/explicit-null state.");
                }
            }
        }


        static void CaptureNative(string input, string directory, string phase, string scenePath, bool repaired, Evidence evidence)
        {
            string name = "native-" + phase + (phase == "after" ? "-" + Guid.NewGuid().ToString("N") : "");
            string plain = Path.Combine(directory, name + ".txt");
            string precise = Path.Combine(directory, name + "-floatbits.txt");
            RunReader(input, plain, false);
            RunReader(input, precise, true);
            ValidateNative(File.ReadAllText(plain), scenePath, repaired);
            if (phase == "before") { evidence.nativeBeforeHash = Hash(plain); evidence.nativeBeforeFloatBitsHash = Hash(precise); }
            else { evidence.nativeAfterPath = plain; evidence.nativeAfterFloatBitsPath = precise; evidence.nativeAfterHash = Hash(plain); evidence.nativeAfterFloatBitsHash = Hash(precise); }
        }

        static void RunReader(string input, string output, bool floatBits)
        {
            string reader = Path.Combine(EditorApplication.applicationContentsPath, "Helpers/binary2text");
            if (!File.Exists(reader)) throw new FileNotFoundException("The matching official Unity reader is required.", reader);
            if (File.Exists(output)) throw new IOException("Native evidence output already exists: " + output);
            string fullInput = Path.GetFullPath(input);
            if (fullInput.Contains("\"") || output.Contains("\"")) throw new IOException("Unexpected quote in native reader path.");
            var start = new System.Diagnostics.ProcessStartInfo(reader,
                "\"" + fullInput + "\" \"" + output + "\" -largebinaryhashonly" + (floatBits ? " -hexfloat" : ""))
                { UseShellExecute = false, CreateNoWindow = true };
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
            {
                if (process == null) throw new IOException("Could not start official native reader.");
                if (!process.WaitForExit(30000))
                {
                    process.Kill(); process.WaitForExit();
                    throw new TimeoutException("Official native reader exceeded 30 seconds; worker drained, source mutation refused.");
                }
                if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
                    throw new IOException("Official native reader did not produce complete evidence: " + output);
            }
            using (var file = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) file.Flush(true);
        }

        static void ValidateNative(string dump, string scenePath, bool repaired)
        {
            // Reuse the preflight's decimal object/PPtr format. Full raw dumps remain evidence;
            // -hexfloat is never an identity authority because it also changes later integer formatting.
            var externals = new Dictionary<int, string>();
            foreach (Match external in Regex.Matches(dump, @"^path\((\d+)\): .* GUID: ([0-9a-fA-F]{32}) Type: ", RegexOptions.Multiline))
                externals.Add(int.Parse(external.Groups[1].Value), external.Groups[2].Value.ToLowerInvariant());
            MatchCollection headers = Regex.Matches(dump, @"^ID: (\d+) \(ClassID: (\d+)\) (\w+)\r?$", RegexOptions.Multiline);
            var blocks = new Dictionary<ulong, string>();
            var classes = new Dictionary<ulong, int>();
            var names = new Dictionary<ulong, string>();
            for (int index = 0; index < headers.Count; index++)
            {
                Match header = headers[index]; ulong id = ulong.Parse(header.Groups[1].Value);
                blocks.Add(id, dump.Substring(header.Index + header.Length, (index + 1 < headers.Count ? headers[index + 1].Index : dump.Length) - header.Index - header.Length));
                classes.Add(id, int.Parse(header.Groups[2].Value)); names.Add(id, header.Groups[3].Value);
            }
            if (blocks.Count == 0) throw new InvalidDataException("No native objects in official dump.");
            if (scenePath == Spazon)
            {
                if (!classes.TryGetValue(AnimationId, out int classId) || classId != 111 || names[AnimationId] != "Animation")
                    throw new InvalidDataException("The exact diagnosed native Animation object/type is absent.");
                string block = blocks[AnimationId];
                Match main = Regex.Match(block, @"\tm_Animation  \(PPtr<AnimationClip>\)\r?\n\t\tm_FileID (\d+) \(int\)\r?\n\t\tm_PathID (-?\d+) \(SInt64\)");
                AssertNativeReference(main, externals, ClipGuid, ClipId);
                Match array = Regex.Match(block, @"\tm_Animations  \(vector\)\r?\n\t\tsize (\d+) \(int\)([\s\S]*?)(?=\r?\n\tm_|\z)");
                if (!array.Success || int.Parse(array.Groups[1].Value) != (repaired ? 1 : 2)) throw new InvalidDataException("Unexpected native animation array layout.");
                MatchCollection clips = Regex.Matches(array.Groups[2].Value, @"\t\tdata  \(PPtr<AnimationClip>\)\r?\n\t\t\tm_FileID (\d+) \(int\)\r?\n\t\t\tm_PathID (-?\d+) \(SInt64\)");
                if (clips.Count != (repaired ? 1 : 2)) throw new InvalidDataException("Incomplete native animation array PPtrs.");
                if (!repaired) AssertNativeReference(clips[0], externals, "f1e72ae3d08805b4b916651568cba4e4", ClipId);
                AssertNativeReference(clips[repaired ? 0 : 1], externals, ClipGuid, ClipId);
                return;
            }
            foreach (var expected in s_Colliders)
            {
                int expectedClass = expected.Value == typeof(BoxCollider) ? 65 : expected.Value == typeof(SphereCollider) ? 135 : 136;
                if (!classes.TryGetValue(expected.Key, out int classId) || classId != expectedClass || names[expected.Key] != expected.Value.Name)
                    throw new InvalidDataException("Exact diagnosed native Collider localID/type mismatch: " + expected.Key);
                Match material = Regex.Match(blocks[expected.Key], @"\tm_Material  \(PPtr<PhysicsMaterial>\)\r?\n\t\tm_FileID (\d+) \(int\)\r?\n\t\tm_PathID (-?\d+) \(SInt64\)");
                AssertNativeReference(material, externals, repaired ? null : "2698563f883d8aa48a56f511f171ac89", repaired ? 0 : 13400000);
            }
        }

        static void AssertNativeReference(Match reference, Dictionary<int, string> externals, string guid, long localId)
        {
            if (!reference.Success || long.Parse(reference.Groups[2].Value) != localId)
                throw new InvalidDataException("Native PPtr localID mismatch.");
            int fileId = int.Parse(reference.Groups[1].Value);
            if (localId == 0 ? fileId != 0 : !externals.TryGetValue(fileId, out string actualGuid) || actualGuid != guid)
                throw new InvalidDataException("Native PPtr external GUID mismatch.");
        }

        static void AssertValidClip(AnimationClip clip)
        {
            if (clip == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId) || guid != ClipGuid || localId != ClipId)
                throw new InvalidOperationException("The valid Mesh_Anim default/registered clip GUID and localID must be preserved.");
        }

        static IEnumerable<Object> EnumerateObjects(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    yield return transform.gameObject;
                    foreach (UnityEngine.Component component in transform.GetComponents<UnityEngine.Component>())
                    {
                        if (component == null) throw new InvalidOperationException("An unresolved component requires separate diagnosis before saving.");
                        yield return component;
                    }
                }
        }

        static int VerifyHash128Encoding()
        {
            // Exercise all 128 bit positions through the same complete Unity string encoding used below.
            var encodings = new HashSet<string>(StringComparer.Ordinal) { new Hash128(0, 0, 0, 0).ToString() };
            for (int bit = 0; bit < 128; bit++)
            {
                uint mask = 1u << (bit % 32);
                var value = new Hash128(bit < 32 ? mask : 0u, bit >= 32 && bit < 64 ? mask : 0u,
                    bit >= 64 && bit < 96 ? mask : 0u, bit >= 96 ? mask : 0u);
                string encoded = value.ToString();
                if (encoded.Length != 32 || !encodings.Add(encoded))
                    throw new InvalidOperationException("Hash128 snapshot encoding did not preserve a distinct 128-bit value at bit " + bit);
            }
            return encodings.Count;
        }

        static string Snapshot(Scene scene)
        {
            var objects = new SortedDictionary<string, string>(StringComparer.Ordinal);
            int exclusions = 0;
            foreach (Object value in EnumerateObjects(scene))
            {
                string key = StableKey(value);
                var text = new StringBuilder(value.GetType().AssemblyQualifiedName + "\n");
                using (var serialized = new SerializedObject(value))
                {
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = property.propertyType != SerializedPropertyType.ObjectReference && property.propertyType != SerializedPropertyType.String;
                        text.Append(property.propertyPath).Append('|').Append(property.type).Append('|');
                        bool allowed = scene.path == Spazon && value is Animation && Id(value) == AnimationId && property.propertyPath == "m_Animations" ||
                            scene.path == Landscape && value is Collider && s_Colliders.ContainsKey(Id(value)) && property.propertyPath == "m_Material";
                        if (allowed) { exclusions++; enterChildren = false; text.AppendLine("<only-approved-reference-change>"); continue; }
                        switch (property.propertyType)
                        {
                            case SerializedPropertyType.Generic: text.Append("container"); break;
                            case SerializedPropertyType.ObjectReference:
                                Object reference = property.objectReferenceValue;
                                if (reference != null) text.Append(StableKey(reference));
                                else if (property.objectReferenceEntityIdValue == default(EntityId)) text.Append("null");
                                else throw new InvalidOperationException("Unresolved non-target reference: " + key + "/" + property.propertyPath);
                                break;
                            case SerializedPropertyType.Integer: text.Append(property.longValue); break;
                            case SerializedPropertyType.ArraySize:
                            case SerializedPropertyType.Enum:
                            case SerializedPropertyType.LayerMask: text.Append(property.intValue); break;
                            case SerializedPropertyType.Float: text.Append(property.doubleValue.ToString("R", CultureInfo.InvariantCulture)); break;
                            case SerializedPropertyType.Boolean: text.Append(property.boolValue); break;
                            case SerializedPropertyType.Hash128: text.Append(property.hash128Value.ToString()); break;
                            case SerializedPropertyType.String: text.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(property.stringValue))); break;
                            case SerializedPropertyType.Color:
                            case SerializedPropertyType.Vector2:
                            case SerializedPropertyType.Vector3:
                            case SerializedPropertyType.Vector4:
                            case SerializedPropertyType.Quaternion:
                            case SerializedPropertyType.Rect:
                            case SerializedPropertyType.Bounds: text.Append(JsonUtility.ToJson(property.boxedValue)); break;
                            default: throw new InvalidOperationException("Unsupported scene field: " + key + "/" + property.propertyPath + " / " + property.propertyType);
                        }
                        text.AppendLine();
                    }
                }
                objects.Add(key, text.ToString());
            }
            if (exclusions != (scene.path == Spazon ? 1 : s_Colliders.Count)) throw new InvalidOperationException("The exact approved field exclusion set was not found.");
            var result = new StringBuilder();
            foreach (var pair in objects) result.AppendLine(pair.Key).AppendLine(pair.Value);
            return result.ToString();
        }

        static string StableKey(Object value)
        {
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(value);
            if (id.identifierType != 0) return id.ToString();
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId) && !string.IsNullOrEmpty(guid)) return guid + ":" + localId;
            throw new InvalidOperationException("Nonpersistent object/reference in scene semantics: " + value.name);
        }

        static string Pointer(string scenePath, string kind) => Path.Combine(Run, Path.GetFileNameWithoutExtension(scenePath) + "-repair-" + kind + ".txt");
        static string ReadOwnedDirectory(string pointer) { string path = File.ReadAllText(pointer); ValidateDirectory(path); return path; }
        static void ValidateDirectory(string path)
        {
            if (!Path.GetFullPath(path).StartsWith(Run + "/known-scene-repair-", StringComparison.Ordinal) || !Directory.Exists(path))
                throw new InvalidOperationException("Unexpected repair evidence directory.");
        }
        static ulong Id(Object value) => GlobalObjectId.GetGlobalObjectIdSlow(value).targetObjectId;
        static void WriteDurableText(string path, string value) => WriteDurableBytes(path, new UTF8Encoding(false).GetBytes(value));
        static void WriteDurableBytes(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static string Hash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}
