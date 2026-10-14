namespace InfinityTech.Rendering.Pipeline
{
    // Low-level RTAO settings consumed by RTAOPass.
    public struct FRTAOSettings
    {
        // Quality
        public int numRays;
        // Artistic
        public float radius;
    }

    public static partial class QualityPresets
    {
        public static readonly FRTAOSettings[] RTAO =
        {
            new FRTAOSettings { numRays = 1 },  // Low
            new FRTAOSettings { numRays = 2 },  // Medium (0.4.0 default)
            new FRTAOSettings { numRays = 4 },  // High
            new FRTAOSettings { numRays = 8 },  // Ultra
        };
    }
}
