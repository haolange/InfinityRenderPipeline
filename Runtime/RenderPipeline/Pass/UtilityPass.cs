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
        internal static string SkyBoxPassName = "Gizmos";
        internal static string GizmosPassName = "SkyBox";
        internal static string PresentPassName = "Present";
    }

    public partial class InfinityRenderPipeline
    {
        // Gizmos Graph
        struct GizmosPassData
        {
            #if UNITY_EDITOR
            public Camera camera;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef colorBuffer;
            #endif
        }

        #if UNITY_EDITOR
        void RenderGizmos(Camera camera)
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
                    passData.camera = camera;
                    passData.colorBuffer = passRef.UseColorBuffer(colorTexture, 0);
                    passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                    //Execute Phase
                    passRef.SetExecuteFunc((in GizmosPassData passData, in RDGContext graphContext) =>
                    {
                        graphContext.renderContext.scriptableRenderContext.DrawGizmos(passData.camera, GizmoSubset.PostImageEffects);
                    });
                }
            }
        }
        #endif

        // SkyBox Graph
        struct SkyBoxPassData
        {
            public Camera camera;
        }

        void RenderSkyBox(Camera camera)
        {
            // Add SkyBoxPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<SkyBoxPassData>(UtilityPassUtilityData.SkyBoxPassName, ProfilingSampler.Get(CustomSamplerId.RenderSkyBox)))
            {
                //Setup Phase
                ref SkyBoxPassData passData = ref passRef.GetPassData<SkyBoxPassData>();
                passData.camera = camera;

                //Execute Phase
                passRef.SetExecuteFunc((in SkyBoxPassData passData, in RDGContext graphContext) =>
                {
                    graphContext.renderContext.scriptableRenderContext.DrawSkybox(passData.camera);
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

        void RenderPresent(Camera camera, RenderTexture dscTexture)
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
                    graphContext.cmdBuffer.DrawFullScreen(GraphicsUtility.GetViewport(passData.camera), passData.srcTexture, new RenderTargetIdentifier(passData.dscTexture), 1);
                    //graphContext.cmdBuffer.DrawFullScreen(GraphicsUtility.GetViewport(passData.camera), passData.srcTexture, new RenderTargetIdentifier(passData.dscTexture), passData.depthTexture, 1);
                });
            }
        }
    }
}
