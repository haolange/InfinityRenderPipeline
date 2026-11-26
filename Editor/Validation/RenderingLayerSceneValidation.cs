using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MaterialPipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    public sealed class RenderingLayerSceneValidation : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (GraphicsSettings.defaultRenderPipeline is InfinityRenderPipelineAsset || QualitySettings.renderPipeline is InfinityRenderPipelineAsset)
                Validate(scene);
        }

        [MenuItem("Window/Infinity/Materials/Validate Loaded Scene Layers")]
        static void ValidateLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Validate(SceneManager.GetSceneAt(i));
            }
            Debug.Log("[InfinityRP] Loaded scene rendering layers and surface routes validated.");
        }

        public static void Validate(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    ValidateMask(renderer.renderingLayerMask, scene.path, renderer);
                    foreach (Material material in renderer.sharedMaterials)
                        if (material && material.HasProperty("_SurfaceRoute") && material.HasProperty("_TranslucentStage"))
                            MaterialRouteUtility.Read(material, out _, out _);
                }
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    ValidateMask(unchecked((uint)light.renderingLayerMask), scene.path, light);
                    if (!LightShapeUtility.IsSupportedNativeType(light.type))
                        Debug.LogWarning($"{scene.path}/{light.name}: light type {light.type} is not rendered in realtime.", light);
                }
            }
        }

        static void ValidateMask(uint mask, string scene, UnityEngine.Component component)
        {
            if ((mask & ~RenderingLayerUtility.ValidMask) != 0)
                throw new InvalidOperationException($"Light channels 0x{mask:X8} on {scene}/{component.name} use bits above the four channels. Run Window/Infinity/Lighting/Clamp Light Channels In Open Scenes, then correct the remaining masks.");
        }
    }
}
