using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        // Two-stage occlusion of the main camera context on the GPU backend: the depth list tests against the previous
        // frame's Hi-Z, an early pyramid of that depth replaces it, and a second depth list draws what the first left. A
        // reset, a first frame or a non-temporal camera has no history to test, so every list is frustum-only.
        bool RegisterMeshHistoryOccluder(in ViewContext context, in MeshMotionBinding motion)
        {
            MeshWorld world = context.meshes;
            if (!context.policy.temporal || !context.features.ShouldRecord(EFrameFeature.HiZ) || world.entityCount == 0
                || world.SelectBackend(MeshPassTable.Depth) != EMeshDrawBackendPolicy.GPUIndirect
                || context.resetsHistory || motion.previous == null)
            {
                return false;
            }
            int2 size = context.dimensions.internalSize;
            HistoryRead previous = context.historyCache.Read(HiZPassUtilityData.HistoryFurthest, HiZDescriptor(size.x, size.y, "HiZFurthest"), context.resetsHistory);
            if (!previous.valid || !context.state.acceptedHiZ)
            {
                return false;
            }
            m_RGBuilder.RegisterMeshOccluder(context.mainCulling, new MeshOccluder(m_RGBuilder.ImportTexture(previous.textureHandle),
                context.cameraUniform.matrix_LastViewFlipYJitterProj, size, HiZMipCount(size), true));
            return true;
        }

        void RegisterMeshEarlyOccluder(in ViewContext context)
        {
            int2 size = context.dimensions.internalSize;
            m_RGBuilder.RegisterMeshOccluder(context.mainCulling, new MeshOccluder(ComputeEarlyFurthestHiZ(),
                context.cameraUniform.matrix_ViewFlipYJitterProj, size, HiZMipCount(size), false));
        }

        // Mip 0 of a pyramid is half the full extent (HiZDescriptor).
        static int HiZMipCount(int2 size) => PyramidMipBatch.MipCount((size.x + 1) / 2, (size.y + 1) / 2);

        void RasterDepthRecovery(in ViewContext context)
        {
            RGMeshDrawListHandle drawsHandle = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(context.mainCulling, InfinityPassIDs.RasterDepthPass));
            {
                RGRasterPassHandle<DepthPassData> passHandle = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepthRecovery));
                passHandle.SetDepthStencilAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer),
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref DepthPassData passData = ref passHandle.GetPassData();
                passData = default;
                passData.meshDrawListHandle = passHandle.UseMeshDrawList(drawsHandle);
                passHandle.SetExecuteFunc((in DepthPassData passData, in RGRasterEncoder commands) => commands.SubmitMeshDrawList(passData.meshDrawListHandle));
            }
        }
    }
}
