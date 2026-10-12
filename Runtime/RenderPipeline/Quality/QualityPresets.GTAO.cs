namespace InfinityTech.Rendering.Pipeline
{
    // Low-level GTAO settings consumed by GTAOPass.
    public struct FGTAOSettings
    {
        // Quality (preset-driven unless Custom)
        public int numRays;
        public int numSteps;
        public float sharpness;
        public float temporalScale;
        public float temporalWeight;
        // Artistic (always from the Volume)
        public float power;
        public float radius;
        public float intensity;
    }

    public static partial class QualityPresets
    {
        // Medium matches the 0.4.0 defaults.
        public static readonly FGTAOSettings[] GTAO =
        {
            new FGTAOSettings { numRays = 1, numSteps = 2, sharpness = 0.5f, temporalScale = 1.0f, temporalWeight = 0.95f },  // Low
            new FGTAOSettings { numRays = 2, numSteps = 2, sharpness = 0.5f, temporalScale = 1.25f, temporalWeight = 0.93f }, // Medium
            new FGTAOSettings { numRays = 2, numSteps = 4, sharpness = 0.5f, temporalScale = 1.25f, temporalWeight = 0.93f }, // High
            new FGTAOSettings { numRays = 4, numSteps = 8, sharpness = 0.6f, temporalScale = 1.5f, temporalWeight = 0.9f },   // Ultra
        };
    }
}
