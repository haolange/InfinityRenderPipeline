using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// The frustum visibility of one entity under a culling context; <c>Compute_MeshDrawPipeline.compute</c> evaluates the
    /// same expressions (MeshEligible, MeshPackLODVisibility, MeshFrustumCulled) in its frustum path.
    /// </summary>
    internal static class MeshCulling
    {
        public static bool Eligible(in MeshEntityDesc desc, in MeshCullingContext context)
        {
            return (desc.flags & EMeshEntityFlags.Visible) != 0
                && (unchecked((uint)desc.layerMask) & context.layerMask) != 0
                && (desc.sceneCullingMask & context.sceneCullingMask) != 0
                && (context.filterRenderingLayers == 0 || (desc.renderingLayerMask & context.renderingLayerMask) != 0);
        }

        public static bool OutsidePlanes(in MeshCullingContext context, in BoundingBox bound)
        {
            for (int i = 0; i < (int)context.planeCount; ++i)
            {
                float4 plane = context.GetPlane(i);
                float radius = math.dot(math.abs(plane.xyz), bound.extents);
                if (math.dot(plane.xyz, bound.center) + plane.w + radius < 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The packed visibility word of one entity: eligibility, then the LOD selection, then the frustum.</summary>
        public static uint Evaluate(in MeshEntity entity, in BoundingBox bound, in MeshCullingContext context, in MeshLODProjection projection)
        {
            if (!Eligible(entity.desc, context))
            {
                return 0u;
            }
            MeshLODSelection.Select(entity.lod, bound, projection, out byte level, out float fade);
            uint packed = MeshLODSelection.Pack(level, fade);
            return packed != 0u && OutsidePlanes(context, bound) ? 0u : packed;
        }
    }
}
