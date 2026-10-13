namespace InfinityTech.Rendering.Pipeline
{
    // Low-level screen-space traced (contact) shadow settings consumed by ContactShadowPass.
    public struct FContactShadowSettings
    {
        // Quality (preset-driven unless Custom)
        public int numSteps;
        public int sunNumSteps;
        // Artistic (always from the Volume)
        public float maxDistance;
        public float thickness;
        public float intensity;
        public float fadeDistance;
    }

    public static partial class QualityPresets
    {
        // Medium keeps the 0.4.0 step count for local lights.
        public static readonly FContactShadowSettings[] ContactShadow =
        {
            new FContactShadowSettings { numSteps = 6, sunNumSteps = 8 },   // Low
            new FContactShadowSettings { numSteps = 8, sunNumSteps = 16 },  // Medium
            new FContactShadowSettings { numSteps = 12, sunNumSteps = 24 }, // High
            new FContactShadowSettings { numSteps = 16, sunNumSteps = 32 }, // Ultra
        };
    }
}
