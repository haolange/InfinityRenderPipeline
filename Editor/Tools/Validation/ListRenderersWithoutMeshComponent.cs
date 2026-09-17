using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class ListRenderersWithoutMeshComponent
    {
        [MenuItem("Window/Infinity/Mesh/List Renderers Without MeshComponent")]
        static void ListLoaded()
        {
            var report = new StringBuilder();
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                        {
                            continue;
                        }

                        if (renderer.GetComponent<MeshComponent>() != null)
                        {
                            continue;
                        }

                        count++;
                        report.Append(scene.path)
                            .Append(" / ")
                            .Append(renderer.GetType().Name)
                            .Append(" ")
                            .Append(GetPath(renderer.transform))
                            .Append('\n');
                    }
                }
            }

            Debug.Log($"[InfinityRP] Renderers without MeshComponent: {count}\n{report}");
        }

        static string GetPath(Transform transform)
        {
            if (transform.parent == null)
            {
                return transform.name;
            }

            return GetPath(transform.parent) + "/" + transform.name;
        }
    }
}
