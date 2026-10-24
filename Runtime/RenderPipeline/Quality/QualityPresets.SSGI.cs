namespace InfinityTech.Rendering.Pipeline
{
    // Low-level SSGI settings consumed by SSGIPass.
    public struct FSSGISettings
    {
        // Quality (preset-driven unless Custom)
        // Trace one ray per 2x2 quad (rotating every frame) instead of one per pixel.
        public bool halfResolution;
        // Hi-Z walk budget; the walk runs at most 4x this many cell steps.
        public int hiZSteps;
        public float temporalWeight;
        // Width of the variance clip box in standard deviations.
        public float temporalClampScale;
        // 0 or 1 world-space spatial pass after temporal accumulation.
        public int spatialPasses;
        // Artistic (always from the Volume)
        public float maxDistance;
        public float thickness;
        public float intensity;
    }

    public static partial class QualityPresets
    {
        // Medium matches the Volume defaults.
        public static readonly FSSGISettings[] SSGI =
        {
            new FSSGISettings { halfResolution = true, hiZSteps = 8, temporalWeight = 0.95f, temporalClampScale = 1.0f, spatialPasses = 1 },   // Low
            new FSSGISettings { halfResolution = true, hiZSteps = 12, temporalWeight = 0.93f, temporalClampScale = 1.25f, spatialPasses = 1 }, // Medium
            new FSSGISettings { halfResolution = true, hiZSteps = 16, temporalWeight = 0.93f, temporalClampScale = 1.25f, spatialPasses = 1 }, // High
            new FSSGISettings { halfResolution = false, hiZSteps = 24, temporalWeight = 0.9f, temporalClampScale = 1.5f, spatialPasses = 1 },  // Ultra
        };
    }
}
