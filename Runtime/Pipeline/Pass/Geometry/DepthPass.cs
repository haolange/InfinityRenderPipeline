using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DepthPassUtilityData
    {
        internal static readonly string TextureName = "DepthTexture";
    }

    internal sealed class DepthPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        // The depth pass of the pipeline's mesh pass table.
        readonly MeshPassId m_DepthMeshPass;

        internal DepthPass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings, MeshPassId depthMeshPass)
        {
            m_RGBuilder = graph;
            m_Settings = settings;
            m_DepthMeshPass = depthMeshPass;
        }

        internal struct DepthPassData
        {
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
        }

        // viewHandle is the mesh view the depth list draws: the main view, or with two-stage occlusion the history view.
        internal void RasterDepth(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.Depth);
            RGMeshDrawListHandle depthMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_DepthMeshPass);
            TextureDescriptor depthTextureDsc = TextureDescriptor.CreateDepthStencil(
                DepthPassUtilityData.TextureName,
                context.dimensions.internalSize.x,
                context.dimensions.internalSize.y,
                m_Settings.config.depthStencilFormat,
                ETextureSizingDomain.Internal);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthTextureDsc);
            RGRendererListHandle depthRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterDepthPass,
                new RenderQueueRange(0, 2999), SortingCriteria.QuantizedFrontToBack, PerObjectData.None, m_Settings.enableInstanceBatch));

            {
                RGRasterPassHandle<DepthPassData> passHandle = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepth));
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref DepthPassData passData = ref passHandle.GetPassData();
                passData.meshDrawListHandle = passHandle.UseMeshDrawList(depthMeshDrawListHandle);
                passData.rendererListHandle = passHandle.UseRendererList(depthRendererListHandle);

                passHandle.SetExecuteFunc((in DepthPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawListHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }

            context.features.MarkProduced(EFrameFeature.Depth);
        }
    }
}
