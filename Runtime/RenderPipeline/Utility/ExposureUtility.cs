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

        public static bool VolumeIsActive(Exposure exposure)
        {
            return GraphicsUtility.VolumeHasOverrides(exposure);
        }

        public static float ResolveCpuEvCompensation(Exposure exposure)
        {
            if (!VolumeIsActive(exposure))
            {
                return 0.0f;
            }

            return exposure.evCompensation.value;
        }

        public static bool ShouldRecordAuto(Exposure exposure)
        {
            return VolumeIsActive(exposure) && exposure.mode.value == EExposureMode.Auto;
        }

        public static float ResolveManualMultiplier(Exposure exposure)
        {
            if (!VolumeIsActive(exposure) || exposure.mode.value != EExposureMode.Manual)
            {
                return 1.0f;
            }

            return EvToMultiplier(exposure.evCompensation.value);
        }
    }
}
