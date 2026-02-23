using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;
using InfinityTech.Rendering.MeshPipeline;

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
        }

        void RenderDBuffer(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            // DBufferA: Albedo (RGB) + Mask (A)
            TextureDescriptor dBufferADsc = new TextureDescriptor(width, height);
            {
                dBufferADsc.name = DBufferPassUtilityData.DBufferAName;
                dBufferADsc.dimension = TextureDimension.Tex2D;
                dBufferADsc.colorFormat = GraphicsFormat.R8G8B8A8_SRGB;
                dBufferADsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef dBufferA = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferA, dBufferADsc);

            // DBufferB: Normal (RGB) + Mask (A)
            TextureDescriptor dBufferBDsc = new TextureDescriptor(width, height);
            {
                dBufferBDsc.name = DBufferPassUtilityData.DBufferBName;
                dBufferBDsc.dimension = TextureDimension.Tex2D;
                dBufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferBDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef dBufferB = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferB, dBufferBDsc);

            // DBufferC: Roughness (R) + Metallic (G) + AO (B) + Mask (A)
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
                rendererListDesc.sortingCriteria = SortingCriteria.CommonOpaque;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList dBufferRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            //Add DBufferPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<DBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RenderDBuffer)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(dBufferA, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(dBufferB, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(dBufferC, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref DBufferPassData passData = ref passRef.GetPassData<DBufferPassData>();
                {
                    passData.rendererList = dBufferRendererList;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in DBufferPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
