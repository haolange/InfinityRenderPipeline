using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FMotionPassUtilityData
    {
        internal static string PassName = "MotionPass";
        internal static string TextureName = "MotionTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FMotionPassData
        {
            public Camera camera;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef motionBuffer;
            public CullingResults cullingResults;
        }

        void RenderMotion(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

            TextureDescription motionDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, clearColor = Color.clear, enableMSAA = false, bindTextureMS = false, name = FMotionPassUtilityData.TextureName, colorFormat = GraphicsFormat.R16G16_SFloat, depthBufferBits = EDepthBits.None };
            
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef motionTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.MotionBuffer, motionDescription);

            //Add MotionPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<FMotionPassData>(FMotionPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderMotion)))
            {
                //Setup Phase
                ref FMotionPassData passData = ref passRef.GetPassData<FMotionPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.motionBuffer = passRef.UseColorBuffer(motionTexture, 0);
                passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passRef.SetExecuteFunc((ref FMotionPassData passData, ref RDGContext graphContext) =>
                {
                    FilteringSettings filteringSettings = new FilteringSettings
                    {
                        renderingLayerMask = 1,
                        excludeMotionVectorObjects = false,
                        layerMask = passData.camera.cullingMask,
                        renderQueueRange = RenderQueueRange.opaque,
                    };
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.MotionPass, new SortingSettings(passData.camera) { criteria = SortingCriteria.CommonOpaque })
                    {
                        perObjectData = PerObjectData.MotionVectors,
                        enableInstancing = true,
                        enableDynamicBatching = false
                    };
                    graphContext.renderContext.DrawRenderers(passData.cullingResults, ref drawingSettings, ref filteringSettings);
                });
            }
        }

    }
}
