using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Low-level volumetric fog settings consumed by VolumetricFogPass.
    public struct FVolumetricFogSettings
    {
        // Quality
        public int depthSlices;
        public float temporalWeight;
        // Artistic
        public float density;
        public float height;
        public float heightFalloff;
        public Color albedo;
        public float anisotropy;
        public float ambientIntensity;
        public float maxDistance;
    }

    public static partial class QualityPresets
    {
        public static readonly FVolumetricFogSettings[] VolumetricFog =
        {
            new FVolumetricFogSettings { depthSlices = 32, temporalWeight = 0.95f },  // Low
            new FVolumetricFogSettings { depthSlices = 64, temporalWeight = 0.9f },   // Medium (0.4.0 default)
            new FVolumetricFogSettings { depthSlices = 96, temporalWeight = 0.9f },   // High
            new FVolumetricFogSettings { depthSlices = 128, temporalWeight = 0.85f }, // Ultra
        };
    }
}
