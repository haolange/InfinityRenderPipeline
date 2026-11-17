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
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle depthTextureHandle;
        }

        void RasterDBuffer(in ViewContext context)
        {
            if (context.decalCount == 0 || !context.features.ShouldRecord(EFrameFeature.DBuffer))
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
                dBufferADsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferAHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferA, dBufferADsc);

            TextureDescriptor dBufferBDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferBDsc.name = DBufferPassUtilityData.DBufferBName;
                dBufferBDsc.dimension = TextureDimension.Tex2D;
                dBufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferBDsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferBHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferB, dBufferBDsc);

            TextureDescriptor dBufferCDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferCDsc.name = DBufferPassUtilityData.DBufferCName;
                dBufferCDsc.dimension = TextureDimension.Tex2D;
                dBufferCDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferCDsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferCHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferC, dBufferCDsc);

            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            RGRendererListHandle dBufferRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterDBufferPass,
                new RenderQueueRange(2000, 2449), SortingCriteria.RendererPriority | SortingCriteria.CommonOpaque, PerObjectData.None, m_Settings.enableInstanceBatch));

            {
                RGRasterPassHandle<DBufferPassData> passHandle = m_RGBuilder.AddRasterPass<DBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDBuffer));
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(dBufferAHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(dBufferBHandle, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(dBufferCHandle, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref DBufferPassData passData = ref passHandle.GetPassData();
                {
                    passData.rendererListHandle = passHandle.UseRendererList(dBufferRendererListHandle);
                    passData.depthTextureHandle = depthTextureHandle;
                }

                passHandle.SetExecuteFunc((in DBufferPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DepthBuffer, passData.depthTextureHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }

            context.features.MarkProduced(EFrameFeature.DBuffer);
        }
    }
}
