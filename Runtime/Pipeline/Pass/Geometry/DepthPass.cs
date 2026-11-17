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
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
        }

        void RasterDepth(in ViewContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.Depth);
            RGMeshDrawListHandle depthMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(context.mainCulling, InfinityPassIDs.RasterDepthPass));
            TextureDescriptor depthTextureDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                depthTextureDsc.name = DepthPassUtilityData.TextureName;
                depthTextureDsc.dimension = TextureDimension.Tex2D;
                depthTextureDsc.depthBufferBits = EDepthBits.Depth32;
            }
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
