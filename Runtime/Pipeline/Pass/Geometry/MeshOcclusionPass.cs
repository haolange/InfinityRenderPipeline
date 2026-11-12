using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        bool PrepareMeshOcclusion(in CameraRecordContext context, ref MeshView view, out MeshOcclusionLists lists)
        {
            MeshWorld world = renderContext.meshWorld;
            var motion = context.state.meshMotionHistory.Prepare(world.transformVersions.current);
            world.BindPreviousTransforms(view.viewKey, motion);
            lists = default;
            if (!context.policy.temporal || !context.features.ShouldRecord(EFrameFeature.HiZ)
                || world.entityCount == 0
                || world.SelectBackend(MeshPassTable.Depth) != EMeshDrawBackendPolicy.GPUIndirect) return false;
            var size = context.dimensions.internalSize;
            var history = context.historyCache;
            // A reset or a first frame has no Hi-Z to test. Those views join the one frustum dispatch.
            if (context.resetsHistory || motion.previous == null)
            {
                return false;
            }
            var previous = history.GetTexture(HiZPassUtilityData.HistoryFurthest,
                HiZDescriptor(size.x, size.y, "HiZFurthest"), out bool created);
            bool valid = !created && history.TextureGeneration(HiZPassUtilityData.HistoryFurthest) > 0
                && context.state.acceptedHiZ;
            if (!valid)
            {
                return false;
            }
            lists = m_RGBuilder.RecordMeshOcclusionPrediction(view, motion,
                m_RGBuilder.ImportTexture(previous),
                context.cameraUniform.matrix_LastViewFlipYJitterProj, size,
                (ulong)history.TextureGeneration(HiZPassUtilityData.HistoryFurthest));
            view = lists.final;
            return true;
        }
        void RecoverMeshOcclusion(in CameraRecordContext context, in MeshOcclusionLists lists)
        {
            if (!lists.history)
            {
                return;
            }
            var early = ComputeEarlyFurthestHiZ();
            m_RGBuilder.RecordMeshOcclusionRecovery(lists, early,
                context.cameraUniform.matrix_ViewFlipYJitterProj, context.dimensions.internalSize);
        }

        void RasterDepthRecovery(in RGMeshDrawListHandle draws)
        {
            using (var pass = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepthRecovery)))
            {
                pass.SetDepthStencilAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer),
                    UnityEngine.Rendering.RenderBufferLoadAction.Load, UnityEngine.Rendering.RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref var data = ref pass.GetPassData();
                data = default;
                data.meshDrawList = pass.UseMeshDrawList(draws);
                pass.SetExecuteFunc((in DepthPassData data, in RGRasterEncoder commands) => commands.SubmitMeshDrawList(data.meshDrawList));
            }
        }
    }
}
