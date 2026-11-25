using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Editor
{
    // Completes the light prefab of CoreRP's IES importer (UnityEditor.Rendering.IESImporter, *.ies) for Infinity: the
    // prefab gets InfinityAdditionalLightData with the profile texture as iesProfile (Cube for point prefabs, 2D for
    // spot prefabs) and preserves either the file's photometric intensity (converted via LightIntensityUtility into
    // canonical units while keeping the imported display unit) or normalized unity intensity.
    // Runs only while an Infinity asset is the active pipeline, so another SRP's handler owns the prefab otherwise.
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

            ApplyImportedIES(light, ies, useIESMaximumIntensity, iesMaximumIntensityUnit, iesMaximumIntensity);
            ctx.AddObjectToAsset(iesFileName + "-Infinity", light.gameObject);
        }

        internal static void ApplyImportedIES(Light light, Texture ies, bool useIESMaximumIntensity,
            string iesMaximumIntensityUnit, float iesMaximumIntensity)
        {
            InfinityAdditionalLightData data = light.gameObject.GetComponent<InfinityAdditionalLightData>()
                ?? light.gameObject.AddComponent<InfinityAdditionalLightData>();
            data.iesProfile = ies;
            // The IES profile encodes the fixture's angular distribution; keep SpotWindow unattenuated inside outer cone.
            if (light.type == LightType.Spot)
            {
                light.innerSpotAngle = light.spotAngle;
            }
            LightSourceShape shape = LightShapeUtility.FromUnityLight(light);
            LightUnit canonicalUnit = LightIntensityUtility.GetCanonicalUnit(shape);
            if (useIESMaximumIntensity)
            {
                LightUnit sourceUnit = ParseIESUnit(iesMaximumIntensityUnit);
                light.intensity = CandelaFromIES(light, iesMaximumIntensityUnit, iesMaximumIntensity);
                light.lightUnit = LightIntensityUtility.IsUnitSupported(shape, sourceUnit) ? sourceUnit : canonicalUnit;
            }
            else
            {
                // Normalized IES profile: leave peak at 1 in canonical units.
                light.intensity = 1.0f;
                light.lightUnit = canonicalUnit;
            }
        }

        internal static LightUnit ParseIESUnit(string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return LightUnit.Candela;
            }
            string lower = unit.Trim().ToLowerInvariant();
            if (lower.StartsWith("lumen"))
            {
                return LightUnit.Lumen;
            }
            if (lower.StartsWith("lux"))
            {
                return LightUnit.Lux;
            }
            if (lower.StartsWith("nit"))
            {
                return LightUnit.Nits;
            }
            return LightUnit.Candela;
        }

        // Converts an IES maximum intensity (lumens or candelas) to canonical Light.intensity (candela) using the
        // IES cone opening angle without Unity's default inner-angle cone weighting.
        internal static float CandelaFromIES(Light light, string unit, float value)
        {
            LightUnit sourceUnit = ParseIESUnit(unit);
            if (light == null)
            {
                return value;
            }
            LightSourceShape shape = LightShapeUtility.FromUnityLight(light);
            float outerAngle = LightIntensityUtility.GetEffectiveSpotAngle(light);
            return LightIntensityUtility.ToCanonicalIntensity(
                value, shape, sourceUnit, outerAngle, outerAngle, LightIntensityUtility.GetEffectiveLuxDistance(light));
        }
    }
}
