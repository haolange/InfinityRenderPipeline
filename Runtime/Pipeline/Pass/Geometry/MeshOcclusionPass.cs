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
        bool RegisterMeshHistoryOccluder(in CameraRecordContext context, in MeshMotionBinding motion)
        {
            MeshWorld world = renderContext.meshWorld;
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
            m_RGBuilder.RegisterMeshOccluder(context.mainCulling, new MeshOccluder(m_RGBuilder.ImportTexture(previous.texture),
                context.cameraUniform.matrix_LastViewFlipYJitterProj, size, HiZMipCount(size), true));
            return true;
        }

        void RegisterMeshEarlyOccluder(in CameraRecordContext context)
        {
            int2 size = context.dimensions.internalSize;
            m_RGBuilder.RegisterMeshOccluder(context.mainCulling, new MeshOccluder(ComputeEarlyFurthestHiZ(),
                context.cameraUniform.matrix_ViewFlipYJitterProj, size, HiZMipCount(size), false));
        }

        // Mip 0 of a pyramid is half the full extent (HiZDescriptor).
        static int HiZMipCount(int2 size) => PyramidMipBatch.MipCount((size.x + 1) / 2, (size.y + 1) / 2);

        void RasterDepthRecovery(in RGMeshDrawListHandle draws)
        {
            using (var pass = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepthRecovery)))
            {
                pass.SetDepthStencilAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer),
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref var data = ref pass.GetPassData();
                data = default;
                data.meshDrawList = pass.UseMeshDrawList(draws);
                pass.SetExecuteFunc((in DepthPassData data, in RGRasterEncoder commands) => commands.SubmitMeshDrawList(data.meshDrawList));
            }
        }
    }
}
