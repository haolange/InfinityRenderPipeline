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
            public FRDGTextureRef depthBuffer;
            public FRDGTextureRef motionBuffer;
            public CullingResults cullingResults;
        }

        void RenderMotion(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

            FTextureDescriptor motionDescriptor = new FTextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = FMotionPassUtilityData.TextureName, colorFormat = GraphicsFormat.R16G16_SFloat, depthBufferBits = EDepthBits.None };

            FRDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            FRDGTextureRef motionTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionDescriptor);

            //Add MotionPass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<FMotionPassData>(FMotionPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderMotion)))
            {
                //Setup Phase
                passRef.SetOption(false, true, Color.black);

                ref FMotionPassData passData = ref passRef.GetPassData<FMotionPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.motionBuffer = passRef.UseColorBuffer(motionTexture, 0);
                passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passRef.SetExecuteFunc((in FMotionPassData passData, in FRDGContext graphContext) =>
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
