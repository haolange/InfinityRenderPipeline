namespace InfinityTech.Rendering.Pipeline
{
    // Low-level SSGI settings consumed by SSGIPass. The work grid is always half resolution.
    public struct FSSGISettings
    {
        // Quality (preset-driven unless Custom)
        // Hi-Z walk budget; the walk runs at most 4x this many cell steps.
        public int hiZSteps;
        // Cosine rays traced per work pixel.
        public int samples;
        public float temporalWeight;
        // Width of the variance clip box in standard deviations.
        public float temporalClampScale;
        // Artistic (always from the Volume). Rays end at the near/far planes and the screen edge.
        public bool traceBehind;
        public bool traceToward;
        public float intensity;
    }

    public static partial class QualityPresets
    {
        // Medium matches the Volume defaults.
        public static readonly FSSGISettings[] SSGI =
        {
            new FSSGISettings { hiZSteps = 8, samples = 1, temporalWeight = 0.95f, temporalClampScale = 1.0f },   // Low
            new FSSGISettings { hiZSteps = 12, samples = 1, temporalWeight = 0.93f, temporalClampScale = 1.25f }, // Medium
            new FSSGISettings { hiZSteps = 16, samples = 2, temporalWeight = 0.93f, temporalClampScale = 1.25f }, // High
            new FSSGISettings { hiZSteps = 24, samples = 2, temporalWeight = 0.9f, temporalClampScale = 1.5f },  // Ultra
        };
    }
}
