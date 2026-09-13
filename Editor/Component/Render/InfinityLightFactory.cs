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

        static void OnComponentAdded(UnityEngine.Component component)
        {
            if (component is Light light
                && GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset
                && light.gameObject.scene.IsValid()
                && (light.hideFlags & HideFlags.DontSave) == 0
                && (light.gameObject.hideFlags & HideFlags.DontSave) == 0)
            {
                InfinityAdditionalLightData.GetOrCreate(light, true);
            }
        }
    }
}
