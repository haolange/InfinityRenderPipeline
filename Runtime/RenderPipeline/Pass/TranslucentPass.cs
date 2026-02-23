using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class TranslucentPassUtilityData
    {
        internal static string DepthTextureName = "TranslucentDepthTexture";
        internal static string LightingTextureName = "TranslucentLightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct TranslucentDepthPassData
        {
            public RendererList rendererList;
        }

        struct ForwardTranslucentPassData
        {
            public RendererList rendererList;
        }

        void RenderTranslucentDepth(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            TextureDescriptor translucentDepthDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                translucentDepthDsc.name = TranslucentPassUtilityData.DepthTextureName;
                translucentDepthDsc.dimension = TextureDimension.Tex2D;
                translucentDepthDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureRef translucentDepthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.TranslucentDepthBuffer, translucentDepthDsc);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.TranslucentDepthPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = InfinityRenderQueue.k_RenderQueue_AllTransparent;
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList depthRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            //Add TranslucentDepthPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<TranslucentDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RenderTranslucentDepth)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(translucentDepthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref TranslucentDepthPassData passData = ref passRef.GetPassData<TranslucentDepthPassData>();
                {
                    passData.rendererList = depthRendererList;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in TranslucentDepthPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }

        void RenderForwardTranslucent(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.ForwardTranslucentPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = InfinityRenderQueue.k_RenderQueue_AllTransparent;
                rendererListDesc.sortingCriteria = SortingCriteria.CommonTransparent;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList translucentRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            //Add ForwardTranslucentPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ForwardTranslucentPassData>(ProfilingSampler.Get(CustomSamplerId.RenderForwardTranslucent)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(lightingTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref ForwardTranslucentPassData passData = ref passRef.GetPassData<ForwardTranslucentPassData>();
                {
                    passData.rendererList = translucentRendererList;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ForwardTranslucentPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
