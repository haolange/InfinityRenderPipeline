using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Editor.Validation
{
    internal static class LoadedScriptDiagnostics
    {
        [Serializable] sealed class MissingComponent
        {
            public string scene, hierarchy, objectId, assetPath, hideFlags;
            public int componentIndex;
        }
        [Serializable] sealed class SceneEvidence
        {
            public string path;
            public bool dirty;
        }
        [Serializable] sealed class Report
        {
            public string unity, utc, activeScene;
            public int objectsChecked;
            public List<SceneEvidence> scenes = new List<SceneEvidence>();
            public List<MissingComponent> missing = new List<MissingComponent>();
        }

        static void Diagnose()
        {
            var report = new Report { unity = Application.unityVersion,
                utc = DateTime.UtcNow.ToString("O"), activeScene = SceneManager.GetActiveScene().path };
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                report.scenes.Add(new SceneEvidence { path = scene.path, dirty = scene.isDirty });
            }
            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                report.objectsChecked++;
                UnityEngine.Component[] components = gameObject.GetComponents<UnityEngine.Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null) continue;
                    string hierarchy = gameObject.name;
                    for (Transform parent = gameObject.transform.parent; parent != null; parent = parent.parent)
                        hierarchy = parent.name + "/" + hierarchy;
                    report.missing.Add(new MissingComponent { scene = gameObject.scene.path,
                        hierarchy = hierarchy, objectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString(),
                        assetPath = AssetDatabase.GetAssetPath(gameObject), hideFlags = gameObject.hideFlags.ToString(), componentIndex = i });
                }
            }
            for (int i = 0; i < report.scenes.Count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path != report.scenes[i].path || scene.isDirty != report.scenes[i].dirty)
                    throw new InvalidOperationException("Loaded scene state changed during read-only diagnosis.");
            }
            if (SceneManager.GetActiveScene().path != report.activeScene)
                throw new InvalidOperationException("Active scene changed during read-only diagnosis.");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "loaded-scripts-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log($"[InfinityRP] Read-only loaded script diagnosis: objects={report.objectsChecked}, missing={report.missing.Count}, report={directory}");
        }
    }
}
