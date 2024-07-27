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
            public RGTextureRef depthBuffer;
            public RGTextureRef colorBuffer;
        }
        
        void RenderWireOverlay(RenderContext renderContext, Camera camera)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

            // Add WireOverlayPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<WireOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RenderWireOverlay)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref WireOverlayPassData passData = ref passRef.GetPassData<WireOverlayPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateWireOverlayRendererList(camera);
                passData.colorBuffer = passRef.UseColorBuffer(colorTexture, 0);
                passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passRef.SetExecuteFunc((in WireOverlayPassData passData, in RGContext graphContext) =>
                {
                    graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }
        
        // Gizmos Graph
        struct GizmosPassData
        {
            public RendererList rendererList;
            public RGTextureRef depthBuffer;
            public RGTextureRef colorBuffer;
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
                    passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                    ref GizmosPassData passData = ref passRef.GetPassData<GizmosPassData>();
                    passData.rendererList = renderContext.scriptableRenderContext.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects);
                    passData.colorBuffer = passRef.UseColorBuffer(colorTexture, 0);
                    passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                    //Execute Phase
                    passRef.SetExecuteFunc((in GizmosPassData passData, in RGContext graphContext) =>
                    {
                        graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
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
            // Add SkyBoxPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<SkyBoxPassData>(ProfilingSampler.Get(CustomSamplerId.RenderSkyBox)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                
                ref SkyBoxPassData passData = ref passRef.GetPassData<SkyBoxPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateSkyboxRendererList(camera);

                //Execute Phase
                passRef.SetExecuteFunc((in SkyBoxPassData passData, in RGContext graphContext) =>
                {
                    graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }

        // Present Graph
        struct PresentPassData
        {
            public Camera camera;
            public RenderTexture dscTexture;
            public RGTextureRef srcTexture;
            public RGTextureRef depthTexture;
        }

        void RenderPresent(RenderContext renderContext, Camera camera, RenderTexture dscTexture)
        {
            RGTextureRef srcTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            
            // Add PresentPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<PresentPassData>(ProfilingSampler.Get(CustomSamplerId.Present)))
            {
                //Setup Phase
                ref PresentPassData passData = ref passRef.GetPassData<PresentPassData>();
                passData.camera = camera;
                passData.dscTexture = dscTexture;
                passData.srcTexture = passRef.ReadTexture(srcTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in PresentPassData passData, in RGContext graphContext) =>
                {
                    RenderTexture srcBuffer = passData.srcTexture;
                    RenderTexture dscBuffer = passData.dscTexture;
                    
                    float4 scaleBias = new float4((float)passData.camera.pixelWidth / (float)srcBuffer.width, (float)passData.camera.pixelHeight / (float)srcBuffer.height, 0.0f, 0.0f);
                    if (!passData.dscTexture) { 
                        scaleBias.w = scaleBias.y; 
                        scaleBias.y *= -1; 
                    }

                    graphContext.cmdBuffer.SetGlobalVector(InfinityShaderIDs.ScaleBias, scaleBias);
                    //graphContext.cmdBuffer.Blit(srcBuffer, dscBuffer);
                    graphContext.cmdBuffer.DrawFullScreen(GraphicsUtility.GetViewport(passData.camera), passData.srcTexture, new RenderTargetIdentifier(passData.dscTexture), 1);
                    //graphContext.cmdBuffer.DrawFullScreen(GraphicsUtility.GetViewport(passData.camera), passData.srcTexture, new RenderTargetIdentifier(passData.dscTexture), passData.depthTexture, 1);
                });
            }
        }
    }
}
