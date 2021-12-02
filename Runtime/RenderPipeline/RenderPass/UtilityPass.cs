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
    internal static class FUtilityPassUtilityData
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
            public FRDGTextureRef depthBuffer;
            public FRDGTextureRef colorBuffer;
            #endif
        }

        #if UNITY_EDITOR
        void RenderGizmos(Camera camera)
        {
            if (Handles.ShouldRenderGizmos())
            {
                FRDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
                FRDGTextureRef colorTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

                // Add GizmosPass
                using (FRDGPassRef passRef = m_GraphBuilder.AddPass<GizmosPassData>(FUtilityPassUtilityData.GizmosPassName, ProfilingSampler.Get(CustomSamplerId.RenderGizmos)))
                {
                    //Setup Phase
                    ref GizmosPassData passData = ref passRef.GetPassData<GizmosPassData>();
                    passData.camera = camera;
                    passData.colorBuffer = passRef.UseColorBuffer(colorTexture, 0);
                    passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                    //Execute Phase
                    passRef.SetExecuteFunc((in GizmosPassData passData, in FRDGContext graphContext) =>
                    {
                        graphContext.renderContext.DrawGizmos(passData.camera, GizmoSubset.PostImageEffects);
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
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<SkyBoxPassData>(FUtilityPassUtilityData.SkyBoxPassName, ProfilingSampler.Get(CustomSamplerId.RenderSkyBox)))
            {
                //Setup Phase
                ref SkyBoxPassData passData = ref passRef.GetPassData<SkyBoxPassData>();
                passData.camera = camera;

                //Execute Phase
                passRef.SetExecuteFunc((in SkyBoxPassData passData, in FRDGContext graphContext) =>
                {
                    graphContext.renderContext.DrawSkybox(passData.camera);
                });
            }
        }

        // Present Graph
        struct PresentPassData
        {
            public Camera camera;
            public RenderTexture dscTexture;
            public FRDGTextureRef srcTexture;
        }

        void RenderPresent(Camera camera, RenderTexture dscTexture)
        {
            FRDGTextureRef srcTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);
            
            // Add PresentPass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<PresentPassData>(FUtilityPassUtilityData.PresentPassName, ProfilingSampler.Get(CustomSamplerId.RenderPresent)))
            {
                //Setup Phase
                ref PresentPassData passData = ref passRef.GetPassData<PresentPassData>();
                passData.camera = camera;
                passData.dscTexture = dscTexture;
                passData.srcTexture = passRef.ReadTexture(srcTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in PresentPassData passData, in FRDGContext graphContext) =>
                {
                    RenderTexture srcBuffer = passData.srcTexture;
                    RenderTexture dscBuffer = passData.dscTexture;

                    float4 ScaleBias = new float4((float)passData.camera.pixelWidth / (float)srcBuffer.width, (float)passData.camera.pixelHeight / (float)srcBuffer.height, 0.0f, 0.0f);
                    if (!passData.dscTexture) { ScaleBias.w = ScaleBias.y; ScaleBias.y *= -1; }

                    graphContext.cmdBuffer.SetGlobalVector(InfinityShaderIDs.ScaleBias, ScaleBias);
                    graphContext.cmdBuffer.DrawFullScreen(GraphicsUtility.GetViewport(passData.camera), srcBuffer, new RenderTargetIdentifier(dscBuffer), 1);
                });
            }
        }
    }
}
