using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        bool PrepareMeshOcclusion(ref MeshView view, out MeshOcclusionLists lists)
        {
            MeshWorld world = renderContext.meshWorld;
            var motion = m_ActiveFrameState.meshMotionHistory.Prepare(world.transformVersions.current);
            world.BindPreviousTransforms(view.viewKey, motion);
            lists = default;
            if (!m_ActiveFrameState.renderPolicy.temporal || !ShouldRecordFeature(EFrameFeature.HiZ)
                || world.entityCount == 0
                || world.SelectBackend(MeshPassTable.Depth) != EMeshDrawBackendPolicy.GPUIndirect) return false;
            var size = m_ActiveFrameState.dimensions.internalSize;
            var history = m_ActiveFrameState.historyCache;
            // A reset or a first frame has no Hi-Z to test. Those views join the one frustum dispatch.
            if (m_ActiveFrameState.cameraUniform.historyReset || motion.previous == null) return false;
            var previous = history.GetTexture(HiZPassUtilityData.HistoryFurthest,
                HiZDescriptor(size.x, size.y, "HiZFurthest"), out bool created);
            bool valid = !created && history.TextureGeneration(HiZPassUtilityData.HistoryFurthest) > 0
                && m_ActiveFrameState.acceptedHiZ;
            if (!valid) return false;
            lists = m_RGBuilder.RecordMeshOcclusionPrediction(view, motion,
                m_RGBuilder.ImportTexture(previous),
                m_ActiveFrameState.cameraUniform.matrix_LastViewFlipYJitterProj, size,
                (ulong)history.TextureGeneration(HiZPassUtilityData.HistoryFurthest));
            view = lists.final;
            return true;
        }
        void RecoverMeshOcclusion(in MeshOcclusionLists lists)
        {
            if (!lists.history) return;
            var early = ComputeEarlyFurthestHiZ();
            m_RGBuilder.RecordMeshOcclusionRecovery(lists, early,
                m_ActiveFrameState.cameraUniform.matrix_ViewFlipYJitterProj, m_ActiveFrameState.dimensions.internalSize);
            var draws = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(lists.recovered, InfinityPassIDs.RasterDepthPass));
            using (var pass = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepthRecovery)))
            {
                pass.SetDepthStencilAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer),
                    UnityEngine.Rendering.RenderBufferLoadAction.Load, UnityEngine.Rendering.RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref var data = ref pass.GetPassData<DepthPassData>(); data = default;
                data.meshDrawList = pass.UseMeshDrawList(draws);
                pass.SetExecuteFunc((in DepthPassData data, in RGRasterEncoder commands) => commands.SubmitMeshDrawList(data.meshDrawList));
            }
        }
    }
}
