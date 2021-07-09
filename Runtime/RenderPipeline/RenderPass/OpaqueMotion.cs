using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FOpaqueMotionString
    {
        internal static string PassName = "OpaqueMotion";
        internal static string TextureName = "MotionBufferTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FOpaqueMotionData
        {
            public Camera camera;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef motionBuffer;
            public CullingResults cullingResults;
        }

        void RenderOpaqueMotion(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription motionDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, clearColor = Color.clear, enableMSAA = false, bindTextureMS = false, name = FOpaqueMotionString.TextureName, colorFormat = GraphicsFormat.R16G16_SFloat };
            RDGTextureRef motionTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.MotionBuffer, motionDescription);

            //Add OpaqueMotionPass
            using (RDGPassBuilder passBuilder = m_GraphBuilder.AddPass<FOpaqueMotionData>(FOpaqueMotionString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueMotion)))
            {
                //Setup Phase
                ref FOpaqueMotionData passData = ref passBuilder.GetPassData<FOpaqueMotionData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.motionBuffer = passBuilder.UseColorBuffer(motionTexture, 0);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passBuilder.SetRenderFunc((ref FOpaqueMotionData passData, ref RDGGraphContext graphContext) =>
                {
                    FilteringSettings filteringSettings = new FilteringSettings
                    {
                        renderingLayerMask = 1,
                        excludeMotionVectorObjects = false,
                        layerMask = passData.camera.cullingMask,
                        renderQueueRange = RenderQueueRange.opaque,
                    };
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.OpaqueGBuffer, new SortingSettings(passData.camera) { criteria = SortingCriteria.CommonOpaque })
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
