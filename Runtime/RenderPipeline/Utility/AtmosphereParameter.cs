using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    public struct AtmosphereParameter
    {
        public float planetRadius;
        public float atmosphereHeight;
        public Color rayleighScattering;
        public float rayleighHeight;
        public float mieScattering;
        public float mieAbsorption;
        public float mieHeight;
        public float mieAnisotropy;
        public Color ozoneAbsorption;
        public float ozoneLayerCenter;
        public float ozoneLayerWidth;
        public Color groundAlbedo;
        public float brightness;
        public float multiScatterStrength;
        public float sunAngle;
        public bool drawGround;
        public int transmittanceLUTWidth;
        public int transmittanceLUTHeight;
        public int multiScatteringLUTSize;
        public int skyViewLUTWidth;
        public int skyViewLUTHeight;
        public int aerialPerspectiveSize;
        public float aerialPerspectiveDistance;
        public int cubemapSize;

        // Hillaire coefficients are stored per kilometer. Integration and Unity world units are meters.
        public const float ScatterPerKmToPerMeter = 0.001f;

        public Vector4 RayleighScatteringPerMeter => (Vector4)rayleighScattering * ScatterPerKmToPerMeter;
        public float MieScatteringPerMeter => mieScattering * ScatterPerKmToPerMeter;
        public float MieAbsorptionPerMeter => mieAbsorption * ScatterPerKmToPerMeter;
        public Vector4 OzoneAbsorptionPerMeter => (Vector4)ozoneAbsorption * ScatterPerKmToPerMeter;

        public static AtmosphereParameter FromProfile(AtmosphericalProfile profile)
        {
            AtmosphereParameter parameter = Default();
            if (profile == null)
            {
                return parameter;
            }

            parameter.planetRadius = profile.radius;
            parameter.atmosphereHeight = profile.thickness;
            parameter.rayleighScattering = profile.rayleighScatter * profile.rayleighStrength;
            parameter.rayleighHeight = profile.rayleighHeight;
            parameter.mieScattering = profile.mieStrength;
            parameter.mieAbsorption = profile.mieAbsorption;
            parameter.mieHeight = profile.mieHeight;
            parameter.mieAnisotropy = profile.mieAnisotropy;
            parameter.ozoneAbsorption = profile.ozoneAbsorption * profile.ozoneStrength;
            parameter.ozoneLayerCenter = profile.ozoneLayerCenter;
            parameter.ozoneLayerWidth = profile.ozoneLayerWidth;
            parameter.groundAlbedo = profile.groundAlbedo;
            parameter.brightness = profile.brightness;
            parameter.multiScatterStrength = profile.multiScatterStrength;
            parameter.sunAngle = profile.sunAngle;
            parameter.drawGround = profile.drawGround;
            parameter.transmittanceLUTWidth = profile.transmittanceLUTWidth;
            parameter.transmittanceLUTHeight = profile.transmittanceLUTHeight;
            parameter.multiScatteringLUTSize = profile.multiScatteringLUTSize;
            parameter.skyViewLUTWidth = profile.skyViewLUTWidth;
            parameter.skyViewLUTHeight = profile.skyViewLUTHeight;
            parameter.aerialPerspectiveSize = profile.aerialPerspectiveSize;
            parameter.aerialPerspectiveDistance = profile.aerialPerspectiveDistance;
            parameter.cubemapSize = profile.cubemapSize;
            return parameter;
        }

        public static AtmosphereParameter Default()
        {
            return new AtmosphereParameter
            {
                planetRadius = 6360000.0f,
                atmosphereHeight = 60000.0f,
                rayleighScattering = new Color(0.00580f, 0.01356f, 0.03310f, 1.0f),
                rayleighHeight = 8000.0f,
                mieScattering = 0.003996f,
                mieAbsorption = 0.000444f,
                mieHeight = 1200.0f,
                mieAnisotropy = 0.8f,
                ozoneAbsorption = new Color(0.000650f, 0.001881f, 0.000085f, 1.0f),
                ozoneLayerCenter = 25000.0f,
                ozoneLayerWidth = 15000.0f,
                groundAlbedo = new Color(0.3f, 0.3f, 0.3f, 1.0f),
                brightness = 1.0f,
                multiScatterStrength = 1.0f,
                sunAngle = 0.5f / 180.0f * Mathf.PI,
                drawGround = false,
                transmittanceLUTWidth = 256,
                transmittanceLUTHeight = 64,
                multiScatteringLUTSize = 32,
                skyViewLUTWidth = 192,
                skyViewLUTHeight = 108,
                aerialPerspectiveSize = 32,
                aerialPerspectiveDistance = 32000.0f,
                cubemapSize = 128
            };
        }

        public static AtmosphereParameter Resolve(InfinityRenderPipelineAsset pipelineAsset, VolumeStack stack)
        {
            AtmosphereParameter parameter = FromProfile(pipelineAsset != null ? pipelineAsset.atmosphericalProfile : null);
            if (stack == null)
            {
                return parameter;
            }

            AtmosphericScattering volume = stack.GetComponent<AtmosphericScattering>();
            if (!volume.active)
            {
                return parameter;
            }

            if (volume.PlanetRadius.overrideState) parameter.planetRadius = volume.PlanetRadius.value;
            if (volume.AtmosphereHeight.overrideState) parameter.atmosphereHeight = volume.AtmosphereHeight.value;
            if (volume.RayleighScattering.overrideState) parameter.rayleighScattering = volume.RayleighScattering.value;
            if (volume.RayleighHeight.overrideState) parameter.rayleighHeight = volume.RayleighHeight.value;
            if (volume.MieScattering.overrideState) parameter.mieScattering = volume.MieScattering.value;
            if (volume.MieAbsorption.overrideState) parameter.mieAbsorption = volume.MieAbsorption.value;
            if (volume.MieHeight.overrideState) parameter.mieHeight = volume.MieHeight.value;
            if (volume.MieAnisotropy.overrideState) parameter.mieAnisotropy = volume.MieAnisotropy.value;
            if (volume.OzoneAbsorption.overrideState) parameter.ozoneAbsorption = volume.OzoneAbsorption.value;
            if (volume.OzoneLayerCenter.overrideState) parameter.ozoneLayerCenter = volume.OzoneLayerCenter.value;
            if (volume.OzoneLayerWidth.overrideState) parameter.ozoneLayerWidth = volume.OzoneLayerWidth.value;
            if (volume.GroundAlbedo.overrideState) parameter.groundAlbedo = volume.GroundAlbedo.value;
            if (volume.TransmittanceLUTWidth.overrideState) parameter.transmittanceLUTWidth = volume.TransmittanceLUTWidth.value;
            if (volume.TransmittanceLUTHeight.overrideState) parameter.transmittanceLUTHeight = volume.TransmittanceLUTHeight.value;
            if (volume.MultiScatteringLUTSize.overrideState) parameter.multiScatteringLUTSize = volume.MultiScatteringLUTSize.value;
            return parameter;
        }
    }
}
