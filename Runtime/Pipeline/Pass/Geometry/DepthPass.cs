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

    public partial class InfinityRenderPipeline
    {
        struct DepthPassData
        {
            public RGMeshDrawListHandle meshDrawList;
            public RGRendererListHandle rendererList;
        }

        void RasterDepth(in CameraRecordContext context, in MeshView view, in RGMeshDrawListHandle depthMeshDrawList)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.Depth);
            TextureDescriptor depthTextureDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                depthTextureDsc.name = DepthPassUtilityData.TextureName;
                depthTextureDsc.dimension = TextureDimension.Tex2D;
                depthTextureDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureHandle depthTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthTextureDsc);
            RGRendererListHandle depthRendererList = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterDepthPass,
                new RenderQueueRange(0, 2999), SortingCriteria.QuantizedFrontToBack, PerObjectData.None, pipelineAsset));

            using (var passRef = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepth)))
            {
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref DepthPassData passData = ref passRef.GetPassData();
                passData.meshDrawList = passRef.UseMeshDrawList(depthMeshDrawList);
                passData.rendererList = passRef.UseRendererList(depthRendererList);

                passRef.SetExecuteFunc((in DepthPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawList);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            context.features.MarkProduced(EFrameFeature.Depth);
        }
    }
}
