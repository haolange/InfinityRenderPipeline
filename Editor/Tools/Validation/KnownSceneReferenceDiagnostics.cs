using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Editor
{
    internal static class KnownSceneReferenceDiagnostics
    {
        const string Run = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation/known-scene-repair-20260906T0859179842870Z-c7b5e3fee498408e873a9b9e105ba83b";
        [Serializable] sealed class SceneState
        {
            public string path;
            public string handle;
            public bool isLoaded;
            public bool isDirty;
            public bool active;
        }
        [Serializable] sealed class Report
        {
            public string utc;
            public int sceneCount;
            public string activePath;
            public string activeHandle;
            public SceneState[] scenes;
            public string diskHashBefore;
            public string diskHashAfter;
            public string originalPostSaveHash;
            public bool nonTargetSnapshotMatches;
            public bool repairedLayoutMatches;
            public string error;
            public string[] dirtyObjects;
        }
        public static void Diagnose()
        {
            Scene active = SceneManager.GetActiveScene();
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), sceneCount = SceneManager.sceneCount,
                activePath = active.path, activeHandle = active.handle.ToString(),
                scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(index =>
                { Scene scene = SceneManager.GetSceneAt(index); return new SceneState { path = scene.path, handle = scene.handle.ToString(), isLoaded = scene.isLoaded, isDirty = scene.isDirty, active = scene == active }; }).ToArray() };
            string prefix = Path.Combine(Run, "readonly-live-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (active.path != "Assets/Scene/Spazon/Scene_Spazon.unity") throw new InvalidOperationException("This diagnostic is scoped to the current pending Spazon run.");
                report.diskHashBefore = Hash(active.path);
                report.originalPostSaveHash = File.ReadAllText(Path.Combine(Run, "post-save.sha256"));
                Type helper = typeof(KnownSceneReferenceRepairs);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                string snapshot = (string)helper.GetMethod("Snapshot", flags).Invoke(null, new object[] { active });
                Write(prefix + "-snapshot.txt", snapshot);
                report.nonTargetSnapshotMatches = snapshot == File.ReadAllText(Path.Combine(Run, "non-target-before.txt"));
                var objects = (IEnumerable<Object>)helper.GetMethod("EnumerateObjects", flags).Invoke(null, new object[] { active });
                report.dirtyObjects = objects.Where(EditorUtility.IsDirty).Select(value => GlobalObjectId.GetGlobalObjectIdSlow(value) + " | " + value.GetType().FullName + " | " + value.name).ToArray();
                helper.GetMethod("ValidateLayout", flags).Invoke(null, new object[] { active, true });
                report.repairedLayoutMatches = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                if (File.Exists(active.path)) report.diskHashAfter = Hash(active.path);
                Write(prefix + ".json", JsonUtility.ToJson(report, true));
                Debug.Log("[InfinityRP] Read-only pending scene diagnosis: " + prefix + ".json");
            }
        }
        static void Write(string path, string text)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(file)) { writer.Write(text); writer.Flush(); file.Flush(true); }
        }
        static string Hash(string path)
        {
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}
