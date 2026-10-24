namespace InfinityTech.Rendering.Pipeline
{
    // Low-level stochastic SSR settings consumed by SSRPass.
    public struct FSSRSettings
    {
        // Quality (preset-driven unless Custom)
        // Trace one ray per 2x2 quad (rotating every frame) instead of one per pixel.
        public bool halfResolution;
        // Below this roughness a pixel resolves only its own mirror ray (no reuse, no spatial filter).
        public float mirrorRoughness;
        // A tile whose minimum roughness is at or above this value traces with the linear march instead of Hi-Z.
        public float linearRoughness;
        // Hi-Z walk budget; the walk runs at most 4x this many cell steps.
        public int hiZSteps;
        public int linearSteps;
        // Rays reused per pixel by the resolve (ratio estimator), including the pixel's own ray.
        public int resolveSamples;
        // Reuse radius in trace pixels.
        public float resolveRadius;
        // Truncates the GGX lobe tail: 0 samples the full lobe, 1 only the mirror direction.
        public float brdfBias;
        public float temporalWeight;
        // Width of the variance clip box in standard deviations.
        public float temporalClampScale;
        // 0 or 1 a-trous pass after the temporal filter.
        public int spatialPasses;
        // Artistic (always from the Volume)
        public float maxDistance;
        public float thickness;
        public float maxRoughness;
        public float edgeFade;
    }

    public static partial class QualityPresets
    {
        // Medium matches the Volume defaults.
        public static readonly FSSRSettings[] SSR =
        {
            new FSSRSettings { halfResolution = true, mirrorRoughness = 0.05f, linearRoughness = 0.2f, hiZSteps = 8, linearSteps = 8, resolveSamples = 2, resolveRadius = 1.5f, brdfBias = 0.7f, temporalWeight = 0.95f, temporalClampScale = 1.0f, spatialPasses = 1 },   // Low
            new FSSRSettings { halfResolution = true, mirrorRoughness = 0.05f, linearRoughness = 0.3f, hiZSteps = 8, linearSteps = 16, resolveSamples = 4, resolveRadius = 2.0f, brdfBias = 0.7f, temporalWeight = 0.93f, temporalClampScale = 1.25f, spatialPasses = 1 }, // Medium
            new FSSRSettings { halfResolution = true, mirrorRoughness = 0.05f, linearRoughness = 0.4f, hiZSteps = 12, linearSteps = 16, resolveSamples = 4, resolveRadius = 2.0f, brdfBias = 0.7f, temporalWeight = 0.93f, temporalClampScale = 1.25f, spatialPasses = 1 }, // High
            new FSSRSettings { halfResolution = false, mirrorRoughness = 0.05f, linearRoughness = 0.5f, hiZSteps = 16, linearSteps = 24, resolveSamples = 8, resolveRadius = 3.0f, brdfBias = 0.8f, temporalWeight = 0.9f, temporalClampScale = 1.5f, spatialPasses = 0 },   // Ultra
        };
    }
}
