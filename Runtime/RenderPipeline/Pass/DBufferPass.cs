using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DBufferPassUtilityData
    {
        internal static string DBufferAName = "DBufferTextureA";
        internal static string DBufferBName = "DBufferTextureB";
        internal static string DBufferCName = "DBufferTextureC";
    }

    public partial class InfinityRenderPipeline
    {
        struct DBufferPassData
        {
            public RendererList rendererList;
            public RGTextureRef depthTexture;
        }

        void RenderDBuffer(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            if (renderContext.WorldDecalCount == 0 || !ShouldRecordFeature(EFrameFeature.DBuffer))
            {
                Shader.SetKeyword(GBufferPassUtilityData.DBufferKeyword, false);
                return;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor dBufferADsc = new TextureDescriptor(width, height);
            {
                dBufferADsc.name = DBufferPassUtilityData.DBufferAName;
                dBufferADsc.dimension = TextureDimension.Tex2D;
                dBufferADsc.colorFormat = GraphicsFormat.R8G8B8A8_SRGB;
                dBufferADsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef dBufferA = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferA, dBufferADsc);

            TextureDescriptor dBufferBDsc = new TextureDescriptor(width, height);
            {
                dBufferBDsc.name = DBufferPassUtilityData.DBufferBName;
                dBufferBDsc.dimension = TextureDimension.Tex2D;
                dBufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferBDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef dBufferB = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferB, dBufferBDsc);

            TextureDescriptor dBufferCDsc = new TextureDescriptor(width, height);
            {
                dBufferCDsc.name = DBufferPassUtilityData.DBufferCName;
                dBufferCDsc.dimension = TextureDimension.Tex2D;
                dBufferCDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferCDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef dBufferC = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferC, dBufferCDsc);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.DBufferPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(2000, 2449);
                rendererListDesc.sortingCriteria = SortingCriteria.RendererPriority | SortingCriteria.CommonOpaque;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList dBufferRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<DBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RenderDBuffer)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(dBufferA, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(dBufferB, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(dBufferC, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref DBufferPassData passData = ref passRef.GetPassData<DBufferPassData>();
                {
                    passData.rendererList = dBufferRendererList;
                    passData.depthTexture = depthTexture;
                }

                passRef.SetExecuteFunc((in DBufferPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DepthBuffer, passData.depthTexture);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            MarkFeatureProduced(EFrameFeature.DBuffer);
        }
    }
}
