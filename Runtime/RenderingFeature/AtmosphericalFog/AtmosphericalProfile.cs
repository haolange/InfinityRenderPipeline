using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Feature
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/AtmosphericalProfile", order = 359)]
    public sealed class AtmosphericalProfile : ScriptableObject
    {
        [Header("Planet")]
        [Min(10000)]
        public float radius = 6360000.0f;

        [Min(100)]
        public float thickness = 60000.0f;

        [Header("Scatter (Hillaire km^-1; bind converts to m^-1)")]
        [Range(0.01f, 100f)]
        public float brightness = 1.0f;

        public bool drawGround = false;

        public Color groundAlbedo = new Color(0.3f, 0.3f, 0.3f);

        public Color rayleighScatter = new Color(0.00580f, 0.01356f, 0.03310f);

        [Min(0)]
        public float rayleighStrength = 1.0f;

        [Min(1)]
        public float rayleighHeight = 8000.0f;

        [Min(0)]
        public float mieStrength = 0.003996f;

        [Min(0)]
        public float mieAbsorption = 0.000444f;

        [Min(1)]
        public float mieHeight = 1200.0f;

        [Range(-1f, 1f)]
        public float mieAnisotropy = 0.8f;

        public Color ozoneAbsorption = new Color(0.000650f, 0.001881f, 0.000085f);

        [Min(0)]
        public float ozoneStrength = 1.0f;

        [Min(1)]
        public float ozoneLayerCenter = 25000.0f;

        [Min(1)]
        public float ozoneLayerWidth = 15000.0f;

        [Min(0)]
        public float multiScatterStrength = 1.0f;

        [Range(0.0001f, 0.03f)]
        public float sunAngle = (0.5f / 180.0f * Mathf.PI);

        [Header("Quality")]
        [Range(64, 512)]
        public int transmittanceLUTWidth = 256;

        [Range(16, 128)]
        public int transmittanceLUTHeight = 64;

        [Range(16, 64)]
        public int multiScatteringLUTSize = 32;

        [Range(64, 512)]
        public int skyViewLUTWidth = 192;

        [Range(32, 256)]
        public int skyViewLUTHeight = 108;

        [Range(8, 64)]
        public int aerialPerspectiveSize = 32;

        [Min(100)]
        public float aerialPerspectiveDistance = 32000.0f;

        [Range(16, 256)]
        public int cubemapSize = 128;

        public void ResetToEarth()
        {
            radius = AtmosphereParameter.EarthPlanetRadius;
            thickness = AtmosphereParameter.EarthAtmosphereHeight;
            brightness = AtmosphereParameter.EarthBrightness;
            drawGround = false;
            groundAlbedo = AtmosphereParameter.EarthGroundAlbedo;
            rayleighScatter = AtmosphereParameter.EarthRayleighScattering;
            rayleighStrength = 1.0f;
            rayleighHeight = AtmosphereParameter.EarthRayleighHeight;
            mieStrength = AtmosphereParameter.EarthMieScattering;
            mieAbsorption = AtmosphereParameter.EarthMieAbsorption;
            mieHeight = AtmosphereParameter.EarthMieHeight;
            mieAnisotropy = AtmosphereParameter.EarthMieAnisotropy;
            ozoneAbsorption = AtmosphereParameter.EarthOzoneAbsorption;
            ozoneStrength = 1.0f;
            ozoneLayerCenter = AtmosphereParameter.EarthOzoneLayerCenter;
            ozoneLayerWidth = AtmosphereParameter.EarthOzoneLayerWidth;
            multiScatterStrength = AtmosphereParameter.EarthMultiScatterStrength;
            sunAngle = AtmosphereParameter.EarthSunAngle;
            transmittanceLUTWidth = 256;
            transmittanceLUTHeight = 64;
            multiScatteringLUTSize = 32;
            skyViewLUTWidth = 192;
            skyViewLUTHeight = 108;
            aerialPerspectiveSize = 32;
            aerialPerspectiveDistance = 32000.0f;
            cubemapSize = 128;
        }

        public bool UpgradeOutOfRangeToEarth()
        {
            return UpgradeOutOfRangeToEarth(null);
        }

        public bool UpgradeOutOfRangeToEarth(List<string> changedFields)
        {
            bool changed = false;
            changed |= UpgradeIf(ref radius, AtmosphereParameter.EarthPlanetRadius, radius <= 0.0f, "radius", changedFields);
            changed |= UpgradeIf(ref thickness, AtmosphereParameter.EarthAtmosphereHeight, !AtmosphereParameter.IsAtmosphereHeightInRange(thickness), "thickness", changedFields);
            changed |= UpgradeIf(ref brightness, AtmosphereParameter.EarthBrightness, brightness <= 0.0f, "brightness", changedFields);
            changed |= UpgradeColorIf(ref groundAlbedo, AtmosphereParameter.EarthGroundAlbedo, groundAlbedo.r == 0.0f && groundAlbedo.g == 0.0f && groundAlbedo.b == 0.0f, "groundAlbedo", changedFields);

            Color rayleigh = rayleighScatter * rayleighStrength;
            if (!AtmosphereParameter.IsRayleighScatterInRange(rayleigh) || rayleighStrength <= 0.0f)
            {
                changed |= UpgradeIf(ref rayleighScatter, AtmosphereParameter.EarthRayleighScattering, true, "rayleighScatter", changedFields);
                changed |= UpgradeIf(ref rayleighStrength, 1.0f, true, "rayleighStrength", changedFields);
            }

            changed |= UpgradeIf(ref rayleighHeight, AtmosphereParameter.EarthRayleighHeight, !AtmosphereParameter.IsDensityHeightInRange(rayleighHeight), "rayleighHeight", changedFields);
            changed |= UpgradeIf(ref mieStrength, AtmosphereParameter.EarthMieScattering, !AtmosphereParameter.IsMieScatteringInRange(mieStrength), "mieStrength", changedFields);
            changed |= UpgradeIf(ref mieAbsorption, AtmosphereParameter.EarthMieAbsorption, !AtmosphereParameter.IsMieAbsorptionInRange(mieAbsorption), "mieAbsorption", changedFields);
            changed |= UpgradeIf(ref mieHeight, AtmosphereParameter.EarthMieHeight, !AtmosphereParameter.IsDensityHeightInRange(mieHeight), "mieHeight", changedFields);

            Color ozone = ozoneAbsorption * ozoneStrength;
            if (!AtmosphereParameter.IsOzoneAbsorptionInRange(ozone) || ozoneStrength <= 0.0f)
            {
                changed |= UpgradeIf(ref ozoneAbsorption, AtmosphereParameter.EarthOzoneAbsorption, true, "ozoneAbsorption", changedFields);
                changed |= UpgradeIf(ref ozoneStrength, 1.0f, true, "ozoneStrength", changedFields);
            }

            changed |= UpgradeIf(ref ozoneLayerCenter, AtmosphereParameter.EarthOzoneLayerCenter, ozoneLayerCenter <= 0.0f, "ozoneLayerCenter", changedFields);
            changed |= UpgradeIf(ref ozoneLayerWidth, AtmosphereParameter.EarthOzoneLayerWidth, ozoneLayerWidth <= 0.0f, "ozoneLayerWidth", changedFields);
            changed |= UpgradeIf(ref multiScatterStrength, AtmosphereParameter.EarthMultiScatterStrength, multiScatterStrength <= 0.0f, "multiScatterStrength", changedFields);
            changed |= UpgradeIf(ref sunAngle, AtmosphereParameter.EarthSunAngle, !AtmosphereParameter.IsSunAngleInRange(sunAngle), "sunAngle", changedFields);
            changed |= UpgradeIf(ref transmittanceLUTWidth, 256, transmittanceLUTWidth <= 0, "transmittanceLUTWidth", changedFields);
            changed |= UpgradeIf(ref transmittanceLUTHeight, 64, transmittanceLUTHeight <= 0, "transmittanceLUTHeight", changedFields);
            changed |= UpgradeIf(ref multiScatteringLUTSize, 32, multiScatteringLUTSize <= 0, "multiScatteringLUTSize", changedFields);
            changed |= UpgradeIf(ref skyViewLUTWidth, 192, skyViewLUTWidth <= 0, "skyViewLUTWidth", changedFields);
            changed |= UpgradeIf(ref skyViewLUTHeight, 108, skyViewLUTHeight <= 0, "skyViewLUTHeight", changedFields);
            changed |= UpgradeIf(ref aerialPerspectiveSize, 32, aerialPerspectiveSize <= 0, "aerialPerspectiveSize", changedFields);
            changed |= UpgradeIf(ref aerialPerspectiveDistance, 32000.0f, aerialPerspectiveDistance <= 0.0f, "aerialPerspectiveDistance", changedFields);
            changed |= UpgradeIf(ref cubemapSize, 128, cubemapSize <= 0, "cubemapSize", changedFields);
            return changed;
        }

        static bool UpgradeIf(ref float field, float earth, bool invalid, string name, List<string> changedFields)
        {
            if (!invalid || field == earth)
            {
                return false;
            }

            field = earth;
            changedFields?.Add(name);
            return true;
        }

        static bool UpgradeIf(ref int field, int earth, bool invalid, string name, List<string> changedFields)
        {
            if (!invalid || field == earth)
            {
                return false;
            }

            field = earth;
            changedFields?.Add(name);
            return true;
        }

        static bool UpgradeIf(ref Color field, Color earth, bool invalid, string name, List<string> changedFields)
        {
            if (!invalid || ColorRgbEquals(field, earth))
            {
                return false;
            }

            field = earth;
            changedFields?.Add(name);
            return true;
        }

        static bool UpgradeColorIf(ref Color field, Color earth, bool invalid, string name, List<string> changedFields)
        {
            return UpgradeIf(ref field, earth, invalid, name, changedFields);
        }

        static bool ColorRgbEquals(Color a, Color b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }
    }
}
