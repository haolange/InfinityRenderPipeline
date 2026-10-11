using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component.Editor
{
    [InitializeOnLoad]
    public static class InfinityLightFactory
    {
        static InfinityLightFactory()
        {
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        // Additional data is the only per-Camera/Light Infinity storage. It is added with Undo when the
        // user (or a GameObject menu) adds the native component, never during asset load.
        static void OnComponentAdded(UnityEngine.Component component)
        {
            if (component == null
                || !(GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset)
                || !component.gameObject.scene.IsValid()
                || (component.hideFlags & HideFlags.DontSave) != 0
                || (component.gameObject.hideFlags & HideFlags.DontSave) != 0)
            {
                return;
            }

            if (component is Light light)
            {
                InfinityAdditionalLightData.GetOrCreate(light, true);
            }
            else if (component is Camera camera)
            {
                InfinityAdditionalCameraData.GetOrCreate(camera, true);
            }
        }
    }
}
