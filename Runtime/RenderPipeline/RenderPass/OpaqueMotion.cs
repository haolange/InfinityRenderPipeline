using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FMotionPassString
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
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription motionDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, clearColor = Color.clear, enableMSAA = false, bindTextureMS = false, name = FMotionPassString.TextureName, colorFormat = GraphicsFormat.R16G16_SFloat };
            RDGTextureRef motionTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.MotionBuffer, motionDescription);

            //Add MotionPass
            using (RDGPassBuilder passBuilder = m_GraphBuilder.AddPass<FMotionPassData>(FMotionPassString.PassName, ProfilingSampler.Get(CustomSamplerId.RenderMotion)))
            {
                //Setup Phase
                ref FMotionPassData passData = ref passBuilder.GetPassData<FMotionPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.motionBuffer = passBuilder.UseColorBuffer(motionTexture, 0);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passBuilder.SetRenderFunc((ref FMotionPassData passData, ref RDGGraphContext graphContext) =>
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
