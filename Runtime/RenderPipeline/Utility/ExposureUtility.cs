using Unity.Mathematics;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    public static class ExposureUtility
    {
        public const int HistogramBinCount = 256;
        public const float HistogramMinLog = -10.0f;
        public const float HistogramMaxLog = 4.0f;
        public const float MidGray = 0.18f;

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

        public static float ResolveManualMultiplier(Exposure exposure)
        {
            ValidateSnapshot(exposure);
            return exposure.mode.value == EExposureMode.Manual ? EvToMultiplier(exposure.evCompensation.value) : 1.0f;
        }
    }
}
