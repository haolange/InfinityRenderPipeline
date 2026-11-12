using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DBufferPassUtilityData
    {
        internal static readonly string DBufferAName = "DBufferTextureA";
        internal static readonly string DBufferBName = "DBufferTextureB";
        internal static readonly string DBufferCName = "DBufferTextureC";
    }

    public partial class InfinityRenderPipeline
    {
        struct DBufferPassData
        {
            public RGRendererListHandle rendererList;
            public RGTextureHandle depthTexture;
        }

        void RasterDBuffer(in CameraRecordContext context)
        {
            if (renderContext.WorldDecalCount == 0 || !context.features.ShouldRecord(EFrameFeature.DBuffer))
            {
                return;
            }

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;

            TextureDescriptor dBufferADsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferADsc.name = DBufferPassUtilityData.DBufferAName;
                dBufferADsc.dimension = TextureDimension.Tex2D;
                dBufferADsc.colorFormat = GraphicsFormat.R8G8B8A8_SRGB;
                dBufferADsc.depthBufferBits = EDepthBits.None;
                dBufferADsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferA = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferA, dBufferADsc);

            TextureDescriptor dBufferBDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferBDsc.name = DBufferPassUtilityData.DBufferBName;
                dBufferBDsc.dimension = TextureDimension.Tex2D;
                dBufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferBDsc.depthBufferBits = EDepthBits.None;
                dBufferBDsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferB = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferB, dBufferBDsc);

            TextureDescriptor dBufferCDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferCDsc.name = DBufferPassUtilityData.DBufferCName;
                dBufferCDsc.dimension = TextureDimension.Tex2D;
                dBufferCDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferCDsc.depthBufferBits = EDepthBits.None;
                dBufferCDsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferC = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferC, dBufferCDsc);

            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            RGRendererListHandle dBufferRendererList = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterDBufferPass,
                new RenderQueueRange(2000, 2449), SortingCriteria.RendererPriority | SortingCriteria.CommonOpaque, PerObjectData.None, m_Settings.enableInstanceBatch));

            using (var passRef = m_RGBuilder.AddRasterPass<DBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDBuffer)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(dBufferA, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(dBufferB, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(dBufferC, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref DBufferPassData passData = ref passRef.GetPassData();
                {
                    passData.rendererList = passRef.UseRendererList(dBufferRendererList);
                    passData.depthTexture = depthTexture;
                }

                passRef.SetExecuteFunc((in DBufferPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DepthBuffer, passData.depthTexture);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            context.features.MarkProduced(EFrameFeature.DBuffer);
        }
    }
}
