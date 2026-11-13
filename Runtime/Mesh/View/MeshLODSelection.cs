using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// LOD selection shared by the CPU gather job and mirrored by <c>MeshLOD.hlsl</c>. The selected level is the first
    /// whose lower bound the screen size reaches; the fade is 1 outside the band above that bound and in [0, 1) inside it.
    /// A level whose bound is 0 never fades.
    /// </summary>
    internal static class MeshLODSelection
    {
        public const byte None = 255;

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
    }
}
