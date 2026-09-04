using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    public enum EScreenSpaceMode
    {
        None = 0,
        SSR = 1,
        SSGI = 2,
        Both = 3
    }

    public static class ScreenSpaceModeUtility
    {
        public static EScreenSpaceMode Resolve(ScreenSpaceReflection reflection, ScreenSpaceIndirectDiffuse indirectDiffuse)
        {
            bool ssr = GraphicsUtility.VolumeHasOverrides(reflection);
            bool ssgi = GraphicsUtility.VolumeHasOverrides(indirectDiffuse);
            if (ssr && ssgi)
            {
                return EScreenSpaceMode.Both;
            }

            if (ssr)
            {
                return EScreenSpaceMode.SSR;
            }

            if (ssgi)
            {
                return EScreenSpaceMode.SSGI;
            }

            return EScreenSpaceMode.None;
        }

        public static bool IncludesSSR(EScreenSpaceMode mode)
        {
            return mode == EScreenSpaceMode.SSR || mode == EScreenSpaceMode.Both;
        }

        public static bool IncludesSSGI(EScreenSpaceMode mode)
        {
            return mode == EScreenSpaceMode.SSGI || mode == EScreenSpaceMode.Both;
        }
    }
}
