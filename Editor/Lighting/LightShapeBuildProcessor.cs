using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component.Editor
{
    // A player has no native area size or sun angle, so it cannot derive a light's shape the way the Editor does. Each
    // scene a player build processes freezes the Editor-derived shape into the light's InfinityAdditionalLightData. A
    // light without one gets one with no distance fade, which renders exactly like a light without the component.
    // Only the built copy of the scene changes; source scenes are not modified or saved.
    public sealed class LightShapeBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!BuildPipeline.isBuildingPlayer)
                return;
            if (!(GraphicsSettings.defaultRenderPipeline is InfinityRenderPipelineAsset || QualitySettings.renderPipeline is InfinityRenderPipelineAsset))
                return;
            Freeze(scene.GetRootGameObjects());
        }

        internal static void Freeze(GameObject[] roots)
        {
            foreach (GameObject root in roots)
            {
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.TryGetComponent(out InfinityAdditionalLightData data))
                    {
                        if (!data.shapeAuthored)
                            data.FreezeNativeShape();
                        continue;
                    }
                    data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
                    data.AdoptNativeShape();
                    data.maxDrawDistance = 0;
                }
            }
        }
    }
}
