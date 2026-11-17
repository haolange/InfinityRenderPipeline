using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// A Hi-Z occluder: the furthest-depth pyramid (mip 0 is half the full extent), the view-projection it was built with and
    /// the full-resolution extent. A history occluder is the previous frame's pyramid and proves occlusion only for entities
    /// whose transform did not change since. <see cref="None"/> tests the frustum only.
    /// </summary>
    internal readonly struct MeshOccluder
    {
        internal readonly RGTextureHandle furthestHandle;
        internal readonly Matrix4x4 viewProjection;
        internal readonly int2 extent;
        internal readonly int mipCount;
        internal readonly bool history;

        internal MeshOccluder(RGTextureHandle furthestHandle, Matrix4x4 viewProjection, int2 extent, int mipCount, bool history)
        {
            this.furthestHandle = furthestHandle;
            this.viewProjection = viewProjection;
            this.extent = extent;
            this.mipCount = mipCount;
            this.history = history;
        }

        internal static MeshOccluder None => default;

        internal bool isValid => furthestHandle.IsValid();
    }

    /// <summary>
    /// What a mesh view culls against, the mesh counterpart of <c>ScriptableCullingParameters</c>: the culling context, the
    /// transform versions its lists bind (default for a view whose passes need no previous transforms) and its occluder.
    /// <see cref="MeshGraphRecording.CullMeshView"/> turns it into an <see cref="RGMeshViewHandle"/>; a list of the view is
    /// created from that handle and a pass.
    /// </summary>
    internal readonly struct MeshViewDesc
    {
        internal readonly MeshCullingContext cullingContext;
        internal readonly MeshMotionBinding motion;
        internal readonly MeshOccluder occluder;

        internal MeshViewDesc(in MeshCullingContext cullingContext, in MeshMotionBinding motion, in MeshOccluder occluder)
        {
            this.cullingContext = cullingContext;
            this.motion = motion;
            this.occluder = occluder;
        }
    }
}
