using Unity.Mathematics;
using InfinityTech.Rendering.Volume;

namespace InfinityTech.Rendering.Pipeline
{
    public static class ExposureUtility
    {
        public const int HistogramBinCount = 256;
        public const float HistogramMinLog = -10.0f;
        public const float HistogramMaxLog = 4.0f;

        public static float EvToMultiplier(float ev)
        {
            return math.exp2(ev);
        }

        static void ValidateSnapshot(Exposure exposure)
        {
            if (exposure == null || !math.isfinite(exposure.evCompensation.value))
                throw new System.InvalidOperationException("InfinityRP: a finite resolved Exposure snapshot is required.");
        }

        public static float ResolveCpuEvCompensation(Exposure exposure)
        {
            ValidateSnapshot(exposure);
            return exposure.evCompensation.value;
        }

        public static bool ShouldRecordAuto(Exposure exposure)
        {
            ValidateSnapshot(exposure);
            return exposure.mode.value == EExposureMode.Auto;
        }

        /// <summary>The linear exposure multiplier applied before the LUT; it applies in every mode.</summary>
        public static float ResolveMultiplier(Exposure exposure)
        {
            return EvToMultiplier(ResolveCpuEvCompensation(exposure));
        }
    }
}
