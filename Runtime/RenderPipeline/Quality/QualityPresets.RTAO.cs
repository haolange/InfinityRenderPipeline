namespace InfinityTech.Rendering.Pipeline
{
    // Low-level RTAO settings consumed by RTAOPass.
    public struct RTAOSettings
    {
        // Quality
        public int numRays;
        // Artistic
        public float radius;
    }

    public static partial class QualityPresets
    {
        public static readonly RTAOSettings[] RTAO =
        {
            new RTAOSettings { numRays = 1 },  // Low
            new RTAOSettings { numRays = 2 },  // Medium (0.4.0 default)
            new RTAOSettings { numRays = 4 },  // High
            new RTAOSettings { numRays = 8 },  // Ultra
        };
    }
}
