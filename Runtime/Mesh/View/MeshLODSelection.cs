using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// LOD selection and the packed visibility word, mirrored by <c>MeshLOD.hlsl</c>. The selected level is the first whose
    /// lower bound the screen size reaches; the fade is 1 outside the band above that bound and in [0, 1) inside it. A level
    /// whose bound is 0 never fades.
    /// </summary>
    internal static class MeshLODSelection
    {
        public const byte None = 255;

        // Packed visibility word (MeshPackLODVisibility): 0 when no level draws, else 1 | level << 8 | fade << 16, where fade
        // is StableFade outside the band and the band position quantized to [0, FadeScale) inside it.

        public const int LevelShift = 8;
        public const int FadeShift = 16;
        public const uint StableFade = 65535u;
        public const float FadeScale = 65534f;

        /// <summary>
        /// The largest half-extent of the world bounds times scale and bias over distance: for a camera, the share of the
        /// view height the bounds' largest side covers, as <c>LODGroup.size</c> drives <c>screenRelativeTransitionHeight</c>.
        /// </summary>
        public static float ScreenSize(in BoundingBox bound, in MeshLODProjection projection)
        {
            float distance = projection.orthographic ? 1.0f : math.max(math.length(bound.center - projection.origin), 1e-6f);
            return math.cmax(bound.extents) * projection.scale * projection.bias / distance;
        }

        public static void Select(in MeshLODChain chain, in BoundingBox bound, in MeshLODProjection projection, out byte level, out float fade)
        {
            float screen = ScreenSize(bound, projection);
            level = None;
            fade = 1;
            for (int i = 0; i < chain.count; ++i)
            {
                float threshold = chain.ScreenSize(i);
                if (screen < threshold)
                {
                    continue;
                }
                level = (byte)i;
                if (threshold > 0)
                {
                    float previous = i == 0 ? 1.0f : chain.ScreenSize(i - 1);
                    float width = chain.fadeWidth * (previous - threshold);
                    float position = (screen - threshold) / width;
                    if (position < 1)
                    {
                        fade = position;
                    }
                }
                return;
            }
        }

        /// <summary>The packed visibility word of a selection: 0 for <see cref="None"/>, else visible, the level and its fade bits.</summary>
        public static uint Pack(byte level, float fade)
        {
            if (level == None)
            {
                return 0u;
            }
            uint fadeBits = fade < 1 ? (uint)(math.saturate(fade) * FadeScale) : StableFade;
            return 1u | ((uint)level << LevelShift) | (fadeBits << FadeShift);
        }
    }
}
