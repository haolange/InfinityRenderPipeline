using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Pipeline.Editor
{
    // Completes the light prefab of CoreRP's IES importer (UnityEditor.Rendering.IESImporter, *.ies) for Infinity: the
    // prefab gets InfinityAdditionalLightData with the profile texture as iesProfile (Cube for point prefabs, 2D for
    // spot prefabs) and, when the importer is set to use the file's maximum intensity, that intensity in candela.
    // Runs only while an Infinity asset is the active pipeline, so another SRP's handler owns the prefab otherwise.
    // TODO(UNVERIFIED): prefab sub-asset creation is not verified in the Editor yet.
    [InitializeOnLoad]
    static class InfinityIESImporter
    {
        static InfinityIESImporter()
        {
            IESImporter.createRenderPipelinePrefabLight -= CreatePrefabLight;
            IESImporter.createRenderPipelinePrefabLight += CreatePrefabLight;
        }

        static void CreatePrefabLight(AssetImportContext ctx, string iesFileName, bool useIESMaximumIntensity,
            string iesMaximumIntensityUnit, float iesMaximumIntensity, Light light, Texture ies)
        {
            if (!(GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset) || light == null)
            {
                return;
            }

            InfinityAdditionalLightData data = light.gameObject.AddComponent<InfinityAdditionalLightData>();
            data.iesProfile = ies;
            if (useIESMaximumIntensity)
            {
                light.intensity = CandelaFromIES(light, iesMaximumIntensityUnit, iesMaximumIntensity);
            }
            ctx.AddObjectToAsset(iesFileName + "-Infinity", light.gameObject);
        }

        // The pipeline treats Light.intensity of punctual lights as on-axis intensity (candela). Lumen values spread
        // over the sphere (point) or the cone (spot).
        internal static float CandelaFromIES(Light light, string unit, float value)
        {
            if (unit != null && unit.ToLowerInvariant().StartsWith("lumen"))
            {
                float solidAngle = light.type == LightType.Spot
                    ? 2.0f * Mathf.PI * (1.0f - Mathf.Cos(0.5f * light.spotAngle * Mathf.Deg2Rad))
                    : 4.0f * Mathf.PI;
                return value / Mathf.Max(solidAngle, 1e-4f);
            }
            return value;
        }
    }
}
