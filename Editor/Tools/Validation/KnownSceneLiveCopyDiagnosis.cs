using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Editor
{
    internal static class KnownSceneLiveCopyDiagnosis
    {
        const string PendingRun = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation/known-scene-repair-20260906T0859179842870Z-c7b5e3fee498408e873a9b9e105ba83b";
        const string Source = "Assets/Scene/Spazon/Scene_Spazon.unity";
        static bool s_Running;
        [Serializable] sealed class State
        {
            public string path;
            public string handle;
            public bool dirty;
            public bool loaded;
            public int sceneCount;
            public string activeHandle;
            public string sourceHash;
            public string metadataHash;
            public string snapshotHash;
        }
        [Serializable] sealed class Report
        {
            public string utc;
            public string status;
            public string stage;
            public string ownerToken;
            public string stageGuid;
            public State before;
            public State afterCopy;
            public State afterCleanup;
            public string copyHash;
            public string copyMetadataHash;
            public string nativeHash;
            public string nativeFloatBitsHash;
            public bool sourceUnchangedAfterCopy;
            public bool sourceUnchangedAfterCleanup;
            public bool stageDeleted;
            public string error;
        }

        [MenuItem("Infinity/Validation/Migration/Capture Pending Scene Live Copy")]
        public static void Capture()
        {
            if (s_Running) throw new InvalidOperationException("Live scene copy diagnosis is already running.");
            s_Running = true;
            string token = Guid.NewGuid().ToString("N");
            string stage = "Assets/InfinityRP_LiveCopy_" + token;
            string directory = Path.Combine(PendingRun, "live-copy-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + token);
            string sentinel = stage + "/owner.txt";
            string stageGuid = null;
            bool ownsStage = false;
            Scene scene = default;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), status = "Preparing", stage = stage, ownerToken = token };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || SceneManager.sceneCount != 1)
                    throw new InvalidOperationException("The current sole Spazon scene must remain in idle EditMode.");
                scene = SceneManager.GetActiveScene();
                if (!scene.isLoaded || scene.path != Source) throw new InvalidOperationException("This diagnosis is scoped to the current pending Spazon scene.");
                if (!Directory.Exists(PendingRun) || Directory.Exists(directory)) throw new IOException("Pending evidence root missing or unique run already exists.");
                Directory.CreateDirectory(directory);
                report.before = ReadState(scene);
                if (report.before.sourceHash != File.ReadAllText(Path.Combine(PendingRun, "post-save.sha256")))
                    throw new IOException("Persisted source changed since the pending repair.");
                WriteText(Path.Combine(directory, "source-live-before.txt"), Snapshot(scene));
                WriteText(Path.Combine(directory, "intent.json"), JsonUtility.ToJson(report, true));
                if (Directory.Exists(stage) || File.Exists(stage + ".meta") || AssetDatabase.IsValidFolder(stage))
                    throw new IOException("Unique owned staging path already exists.");
                stageGuid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(stage));
                if (string.IsNullOrEmpty(stageGuid) || !AssetDatabase.IsValidFolder(stage) || AssetDatabase.AssetPathToGUID(stage) != stageGuid)
                    throw new IOException("Unity did not create the requested owned stage.");
                // Cleanup ownership begins only after both the folder GUID and durable sentinel are established.
                WriteText(sentinel, token);
                ownsStage = true; report.stageGuid = stageGuid;
                string copy = stage + "/LiveScene.unity";
                if (!EditorSceneManager.SaveScene(scene, copy, true)) throw new IOException("Unity could not save the full live scene as a separate copy.");
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                report.afterCopy = ReadState(scene);
                RequireUnchanged(report.before, report.afterCopy);
                report.sourceUnchangedAfterCopy = true;
                if (!File.Exists(copy + ".meta")) throw new IOException("The staged scene has no canonical metadata.");
                WriteBytes(Path.Combine(directory, "live-scene.unity"), File.ReadAllBytes(copy));
                WriteBytes(Path.Combine(directory, "live-scene.unity.meta"), File.ReadAllBytes(copy + ".meta"));
                report.copyHash = Hash(copy); report.copyMetadataHash = Hash(copy + ".meta");
                if (Hash(Path.Combine(directory, "live-scene.unity")) != report.copyHash || Hash(Path.Combine(directory, "live-scene.unity.meta")) != report.copyMetadataHash)
                    throw new IOException("The persistent full-scene copy failed byte verification.");
                MethodInfo reader = typeof(KnownSceneReferenceRepairs).GetMethod("RunReader", BindingFlags.Static | BindingFlags.NonPublic);
                if (reader == null) throw new MissingMethodException("Existing official native reader was not found.");
                reader.Invoke(null, new object[] { Path.Combine(directory, "live-scene.unity"), Path.Combine(directory, "native-live.txt"), false });
                reader.Invoke(null, new object[] { Path.Combine(directory, "live-scene.unity"), Path.Combine(directory, "native-live-floatbits.txt"), true });
                report.nativeHash = Hash(Path.Combine(directory, "native-live.txt"));
                report.nativeFloatBitsHash = Hash(Path.Combine(directory, "native-live-floatbits.txt"));
                RequireUnchanged(report.before, ReadState(scene));
                WriteText(Path.Combine(directory, "copy-persisted.json"), JsonUtility.ToJson(report, true));
                report.status = "CopyPersistedAwaitingCleanup";
            }
            catch (Exception error) { report.status = "Failed"; report.error = error.ToString(); }
            finally
            {
                try
                {
                    if (ownsStage)
                    {
                        if (!Directory.Exists(stage) || AssetDatabase.AssetPathToGUID(stage) != stageGuid || !File.Exists(sentinel) || File.ReadAllText(sentinel) != token)
                            throw new IOException("Stage owner identity changed; refusing cleanup.");
                        if (!AssetDatabase.DeleteAsset(stage)) throw new IOException("Unity did not delete this invocation's owned staging directory.");
                        if (Directory.Exists(stage) || File.Exists(stage + ".meta")) throw new IOException("Owned staging files remain after cleanup.");
                        report.stageDeleted = true;
                    }
                    if (report.before != null)
                    {
                        report.afterCleanup = ReadState(scene);
                        RequireUnchanged(report.before, report.afterCleanup);
                        report.sourceUnchangedAfterCleanup = true;
                    }
                    if (report.status == "CopyPersistedAwaitingCleanup" && report.stageDeleted)
                        report.status = "SourcePreservedCopyCapturedAwaitingIndependentNativeDiff";
                }
                catch (Exception error) { report.status = "Failed"; report.error += "\nCleanup/state verification: " + error; }
                s_Running = false;
                if (Directory.Exists(directory)) WriteText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[InfinityRP] Pending live scene copy diagnosis " + report.status + ": " + directory);
            }
        }

        static State ReadState(Scene scene)
        {
            typeof(KnownSceneReferenceRepairs).GetMethod("ValidateLayout", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { scene, true });
            return new State { path = scene.path, handle = scene.handle.ToString(), dirty = scene.isDirty, loaded = scene.isLoaded,
                sceneCount = SceneManager.sceneCount, activeHandle = SceneManager.GetActiveScene().handle.ToString(),
                sourceHash = Hash(Source), metadataHash = Hash(Source + ".meta"), snapshotHash = TextHash(Snapshot(scene)) };
        }
        static void RequireUnchanged(State before, State after)
        {
            if (JsonUtility.ToJson(before) != JsonUtility.ToJson(after)) throw new InvalidOperationException("Source scene path/dirty/loaded/handle/count/active/bytes/meta or loaded-object semantics changed during live-copy diagnosis.");
        }
        static string Snapshot(Scene scene)
        {
            MethodInfo snapshot = typeof(KnownSceneReferenceRepairs).GetMethod("Snapshot", BindingFlags.Static | BindingFlags.NonPublic);
            return (string)snapshot.Invoke(null, new object[] { scene });
        }
        static void WriteText(string path, string text) => WriteBytes(path, new System.Text.UTF8Encoding(false).GetBytes(text));
        static void WriteBytes(string path, byte[] bytes)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        }
        static string TextHash(string text) { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))); }
        static string Hash(string path) { using (var hash = SHA256.Create()) using (var file = File.OpenRead(path)) return Hex(hash.ComputeHash(file)); }
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
