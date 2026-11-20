using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// The camera's main mesh views. With two-stage occlusion, <see cref="depthViewHandle"/> tests the previous frame's Hi-Z,
    /// <see cref="mainViewHandle"/> tests the early Hi-Z of that depth for every later pass, and
    /// <see cref="recoveryViewHandle"/> draws what the depth view left and the main view sees. Otherwise the three are one
    /// frustum view.
    /// </summary>
    internal readonly struct MeshMainViews
    {
        internal readonly RGMeshViewHandle depthViewHandle;
        internal readonly RGMeshViewHandle recoveryViewHandle;
        internal readonly RGMeshViewHandle mainViewHandle;
        internal readonly bool twoStage;

        internal MeshMainViews(in RGMeshViewHandle depthViewHandle, in RGMeshViewHandle recoveryViewHandle, in RGMeshViewHandle mainViewHandle, bool twoStage)
        {
            this.depthViewHandle = depthViewHandle;
            this.recoveryViewHandle = recoveryViewHandle;
            this.mainViewHandle = mainViewHandle;
            this.twoStage = twoStage;
        }
    }

    internal sealed class MeshOcclusionPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly HiZPass m_HiZPass;
        readonly MeshPassId m_DepthMeshPass;

        internal MeshOcclusionPass(RGBuilder graph, HiZPass hiZPass, MeshPassId depthMeshPass)
        {
            m_RGBuilder = graph;
            m_HiZPass = hiZPass;
            m_DepthMeshPass = depthMeshPass;
        }

        // Two-stage occlusion of the main camera view on the GPU backend: the depth list tests the previous frame's Hi-Z, an
        // early pyramid of that depth follows, and a remainder view draws what the first list left. A reset, a first frame
        // or a non-temporal camera has no history to test, so the main views are one frustum view and nothing follows.
        // With two stages, only depthViewHandle is valid until CullEarlyViews.
        internal MeshMainViews CullMainViews(in ViewContext context, in MeshMotionBinding motion)
        {
            MeshWorld world = context.meshes;
            bool history = context.policy.temporal && context.features.ShouldRecord(EFrameFeature.HiZ) && world.entityCount > 0
                && world.SelectBackend(m_DepthMeshPass) == EMeshDrawBackendPolicy.GPUIndirect
                && !context.resetsHistory && motion.previous != null;
            HistoryRead previous = default;
            int2 size = context.dimensions.internalSize;
            if (history)
            {
                previous = context.historyCache.Read(HiZPassUtilityData.HistoryFurthest, HiZPass.HiZDescriptor(size.x, size.y, "HiZFurthest"), context.resetsHistory);
                history = previous.valid && context.state.acceptedHiZ;
            }
            if (!history)
            {
                RGMeshViewHandle frustumViewHandle = m_RGBuilder.CullMeshView(new MeshViewDesc(context.mainCulling, motion, MeshOccluder.None));
                return new MeshMainViews(frustumViewHandle, frustumViewHandle, frustumViewHandle, false);
            }
            var historyOccluder = new MeshOccluder(m_RGBuilder.ImportTexture(previous.textureHandle),
                context.cameraUniform.matrix_LastViewFlipYJitterProj, size, HiZMipCount(size), true);
            RGMeshViewHandle historyViewHandle = m_RGBuilder.CullMeshView(new MeshViewDesc(context.mainCulling, motion, historyOccluder));
            return new MeshMainViews(historyViewHandle, default, default, true);
        }

        // The furthest Hi-Z of the depth the history view's list drew, recorded after that depth.
        internal MeshOccluder CreateEarlyOccluder(in ViewContext context)
        {
            int2 size = context.dimensions.internalSize;
            return new MeshOccluder(m_HiZPass.ComputeEarlyFurthestHiZ(), context.cameraUniform.matrix_ViewFlipYJitterProj, size, HiZMipCount(size), false);
        }

        // The second stage: the main view every later pass draws, against the early occluder, and the remainder of the
        // history view, derived from the history and main views without a culling pass of its own.
        internal MeshMainViews CullEarlyViews(in ViewContext context, in MeshMotionBinding motion, in MeshMainViews views, in MeshOccluder earlyOccluder)
        {
            RGMeshViewHandle mainViewHandle = m_RGBuilder.CullMeshView(new MeshViewDesc(context.mainCulling, motion, earlyOccluder));
            RGMeshViewHandle recoveryViewHandle = m_RGBuilder.CullMeshViewRemainder(views.depthViewHandle, mainViewHandle);
            return new MeshMainViews(views.depthViewHandle, recoveryViewHandle, mainViewHandle, true);
        }

        // Mip 0 of a pyramid is half the full extent (HiZDescriptor).
        static int HiZMipCount(int2 size) => PyramidMipBatch.MipCount((size.x + 1) / 2, (size.y + 1) / 2);

        // The remainder depth list: what the history view's depth list left visible under the early occluder.
        internal void RasterDepthRecovery(in RGMeshViewHandle recoveryViewHandle)
        {
            RGMeshDrawListHandle drawsHandle = m_RGBuilder.CreateMeshDrawList(recoveryViewHandle, m_DepthMeshPass);
            {
                RGRasterPassHandle<DepthPass.DepthPassData> passHandle = m_RGBuilder.AddRasterPass<DepthPass.DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepthRecovery));
                passHandle.SetDepthStencilAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer),
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref DepthPass.DepthPassData passData = ref passHandle.GetPassData();
                passData = default;
                passData.meshDrawListHandle = passHandle.UseMeshDrawList(drawsHandle);
                passHandle.SetExecuteFunc((in DepthPass.DepthPassData passData, in RGRasterEncoder commands) => commands.SubmitMeshDrawList(passData.meshDrawListHandle));
            }
        }
    }
}
