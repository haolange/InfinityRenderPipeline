using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    internal static class UtilityPassUtilityData
    {
        internal static string SkyBoxPassName = "SkyBoxPass";
        internal static string WireOverlayPassName = "WireOverlayPass";
        internal static string GizmosPassName = "GizmosPass";
        internal static string PresentPassName = "PresentPass";
    }

    public partial class InfinityRenderPipeline
    {
#if UNITY_EDITOR
        // WireOverlay Graph
        struct WireOverlayPassData
        {
            public RendererList rendererList;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef colorBuffer;
        }
        
        void RenderWireOverlay(RenderContext renderContext, Camera camera)
        {
            RDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef colorTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

            // Add WireOverlayPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<WireOverlayPassData>(UtilityPassUtilityData.WireOverlayPassName, ProfilingSampler.Get(CustomSamplerId.RenderWireOverlay)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref WireOverlayPassData passData = ref passRef.GetPassData<WireOverlayPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateWireOverlayRendererList(camera);
                passData.colorBuffer = passRef.UseColorBuffer(colorTexture, 0);
                passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passRef.SetExecuteFunc((in WireOverlayPassData passData, in RDGContext graphContext) =>
                {
                    graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }
        
        // Gizmos Graph
        struct GizmosPassData
        {
            public RendererList rendererList;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef colorBuffer;
        }

        void RenderGizmos(RenderContext renderContext, Camera camera)
        {
            if (Handles.ShouldRenderGizmos())
            {
                RDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
                RDGTextureRef colorTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

                // Add GizmosPass
                using (RDGPassRef passRef = m_GraphBuilder.AddPass<GizmosPassData>(UtilityPassUtilityData.GizmosPassName, ProfilingSampler.Get(CustomSamplerId.RenderGizmos)))
                {
                    //Setup Phase
                    passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                    ref GizmosPassData passData = ref passRef.GetPassData<GizmosPassData>();
                    passData.rendererList = renderContext.scriptableRenderContext.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects);
                    passData.colorBuffer = passRef.UseColorBuffer(colorTexture, 0);
                    passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                    //Execute Phase
                    passRef.SetExecuteFunc((in GizmosPassData passData, in RDGContext graphContext) =>
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
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<SkyBoxPassData>(UtilityPassUtilityData.SkyBoxPassName, ProfilingSampler.Get(CustomSamplerId.RenderSkyBox)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                
                ref SkyBoxPassData passData = ref passRef.GetPassData<SkyBoxPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateSkyboxRendererList(camera);

                //Execute Phase
                passRef.SetExecuteFunc((in SkyBoxPassData passData, in RDGContext graphContext) =>
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
            public RDGTextureRef srcTexture;
            public RDGTextureRef depthTexture;
        }

        void RenderPresent(RenderContext renderContext, Camera camera, RenderTexture dscTexture)
        {
            RDGTextureRef srcTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);
            RDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            
            // Add PresentPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<PresentPassData>(UtilityPassUtilityData.PresentPassName, ProfilingSampler.Get(CustomSamplerId.RenderPresent)))
            {
                //Setup Phase
                ref PresentPassData passData = ref passRef.GetPassData<PresentPassData>();
                passData.camera = camera;
                passData.dscTexture = dscTexture;
                passData.srcTexture = passRef.ReadTexture(srcTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in PresentPassData passData, in RDGContext graphContext) =>
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
