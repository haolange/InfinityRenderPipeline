using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Low-level volumetric fog settings consumed by VolumetricFogPass.
    public struct VolumetricFogSettings
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
        public static readonly VolumetricFogSettings[] VolumetricFog =
        {
            new VolumetricFogSettings { depthSlices = 32, temporalWeight = 0.95f },  // Low
            new VolumetricFogSettings { depthSlices = 64, temporalWeight = 0.9f },   // Medium (0.4.0 default)
            new VolumetricFogSettings { depthSlices = 96, temporalWeight = 0.9f },   // High
            new VolumetricFogSettings { depthSlices = 128, temporalWeight = 0.85f }, // Ultra
        };
    }
}
