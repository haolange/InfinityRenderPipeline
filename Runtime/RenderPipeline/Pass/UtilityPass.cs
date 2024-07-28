using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
#if UNITY_EDITOR
        // WireOverlay Graph
        struct WireOverlayPassData
        {
            public RendererList rendererList;
        }
        
        void RenderWireOverlay(RenderContext renderContext, Camera camera)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

            // Add WireOverlayPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<WireOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RenderWireOverlay)))
            {
                //Setup Phase
                passRef.UseDepthBuffer(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Read);
                passRef.UseColorBuffer(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref WireOverlayPassData passData = ref passRef.GetPassData<WireOverlayPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateWireOverlayRendererList(camera);

                //Execute Phase
                passRef.SetExecuteFunc((in WireOverlayPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }
        
        // Gizmos Graph
        struct GizmosPassData
        {
            public RendererList rendererList;
        }

        void RenderGizmos(RenderContext renderContext, Camera camera)
        {
            if (Handles.ShouldRenderGizmos())
            {
                RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
                RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

                // Add GizmosPass
                using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<GizmosPassData>(ProfilingSampler.Get(CustomSamplerId.RenderGizmos)))
                {
                    //Setup Phase
                    passRef.UseColorBuffer(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passRef.UseDepthBuffer(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Read);

                    ref GizmosPassData passData = ref passRef.GetPassData<GizmosPassData>();
                    passData.rendererList = renderContext.scriptableRenderContext.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects);

                    //Execute Phase
                    passRef.SetExecuteFunc((in GizmosPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                    {
                        cmdBuffer.DrawRendererList(passData.rendererList);
                    });
                }
            }
        }
#endif

        // SkyBox Graph
        struct SkyBoxPassData
        {
            public RendererList rendererList;
        }

        void RenderSkyBox(RenderContext renderContext, Camera camera)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);

            // Add SkyBoxPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<SkyBoxPassData>(ProfilingSampler.Get(CustomSamplerId.RenderSkyBox)))
            {
                //Setup Phase
                passRef.UseDepthBuffer(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Read);
                passRef.UseColorBuffer(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref SkyBoxPassData passData = ref passRef.GetPassData<SkyBoxPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateSkyboxRendererList(camera);

                //Execute Phase
                passRef.SetExecuteFunc((in SkyBoxPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }

        // Present Graph
        struct PresentPassData
        {
            public Camera camera;
            public RGTextureRef srcTexture;
            public RenderTexture dscTexture;
        }

        void RenderPresent(RenderContext renderContext, Camera camera, RenderTexture dscTexture)
        {
            RGTextureRef srcTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);
            
            // Add PresentPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<PresentPassData>(ProfilingSampler.Get(CustomSamplerId.Present)))
            {
                //Setup Phase
                ref PresentPassData passData = ref passRef.GetPassData<PresentPassData>();
                passData.camera = camera;
                passData.srcTexture = passRef.ReadTexture(srcTexture);
                passData.dscTexture = dscTexture;

                //Execute Phase
                passRef.SetExecuteFunc((in PresentPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    RenderTexture srcBuffer = passData.srcTexture;
                    RenderTexture dscBuffer = passData.dscTexture;
                    
                    float4 scaleBias = new float4((float)passData.camera.pixelWidth / (float)srcBuffer.width, (float)passData.camera.pixelHeight / (float)srcBuffer.height, 0.0f, 0.0f);
                    if (!passData.dscTexture) 
                    { 
                        scaleBias.w = scaleBias.y; 
                        scaleBias.y *= -1; 
                    }

                    cmdBuffer.SetGlobalVector(InfinityShaderIDs.ScaleBias, scaleBias);
                    cmdBuffer.DrawFullScreen(GraphicsUtility.GetViewport(passData.camera), passData.srcTexture, new RenderTargetIdentifier(passData.dscTexture), 1);
                });
            }
        }
    }
}
