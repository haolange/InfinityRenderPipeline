namespace InfinityTech.Rendering.Pipeline
{
    // Low-level volumetric cloud settings consumed by VolumetricCloudPass.
    public struct FVolumetricCloudSettings
    {
        // Quality
        public int numPrimarySteps;
        public int numLightSteps;
        public float temporalWeight;
        // Artistic
        public float cloudLayerBottom;
        public float cloudLayerThickness;
        public float densityMultiplier;
        public float shapeFactor;
        public float erosionFactor;
        public float anisotropy;
        public float silverIntensity;
        public float silverSpread;
        public float ambientIntensity;
    }

    public static partial class QualityPresets
    {
        public static readonly FVolumetricCloudSettings[] VolumetricCloud =
        {
            new FVolumetricCloudSettings { numPrimarySteps = 32, numLightSteps = 4, temporalWeight = 0.95f },   // Low
            new FVolumetricCloudSettings { numPrimarySteps = 64, numLightSteps = 6, temporalWeight = 0.9f },    // Medium (0.4.0 default)
            new FVolumetricCloudSettings { numPrimarySteps = 96, numLightSteps = 8, temporalWeight = 0.9f },    // High
            new FVolumetricCloudSettings { numPrimarySteps = 128, numLightSteps = 12, temporalWeight = 0.85f }, // Ultra
        };
    }
}
