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
        // Rays reused per pixel by the resolve (ratio estimator), including the pixel's own ray.
        public int resolveSamples;
        // Reuse radius in trace pixels.
        public float resolveRadius;
        public float temporalWeight;
        // Width of the variance clip box in standard deviations.
        public float temporalClampScale;
        // 0 or 1 a-trous pass after the temporal filter.
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
            new FSSGISettings { halfResolution = true, hiZSteps = 8, resolveSamples = 4, resolveRadius = 2.0f, temporalWeight = 0.95f, temporalClampScale = 1.0f, spatialPasses = 1 },   // Low
            new FSSGISettings { halfResolution = true, hiZSteps = 12, resolveSamples = 6, resolveRadius = 3.0f, temporalWeight = 0.93f, temporalClampScale = 1.25f, spatialPasses = 1 }, // Medium
            new FSSGISettings { halfResolution = true, hiZSteps = 16, resolveSamples = 8, resolveRadius = 3.0f, temporalWeight = 0.93f, temporalClampScale = 1.25f, spatialPasses = 1 }, // High
            new FSSGISettings { halfResolution = false, hiZSteps = 24, resolveSamples = 8, resolveRadius = 3.0f, temporalWeight = 0.9f, temporalClampScale = 1.5f, spatialPasses = 1 },  // Ultra
        };
    }
}
