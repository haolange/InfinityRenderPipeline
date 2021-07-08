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
            public RDGTextureRef depthBuffer;
            public RDGTextureRef motionBuffer;
        }

        void RenderOpaqueMotion(Camera camera, FCullingData cullingData, CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription motionDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, clearColor = Color.clear, enableMSAA = false, bindTextureMS = false, name = FOpaqueMotionString.TextureName, colorFormat = GraphicsFormat.R16G16_SFloat };
            RDGTextureRef motionTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.MotionBuffer, motionDescription);

            //Add OpaqueMotionPass
            m_GraphBuilder.AddPass<FOpaqueMotionData>(FOpaqueMotionString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueMotion),
            (ref FOpaqueMotionData passData, ref RDGPassBuilder passBuilder) =>
            {
                passData.motionBuffer = passBuilder.UseColorBuffer(motionTexture, 0);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.Read);
            },
            (ref FOpaqueMotionData passData, ref RDGGraphContext graphContext) =>
            {
                FilteringSettings filteringSettings = new FilteringSettings
                {
                    //renderingLayerMask = 1,
                    excludeMotionVectorObjects = false,
                    //layerMask = RenderCamera.cullingMask,
                    renderQueueRange = RenderQueueRange.opaque,
                };
                DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.OpaqueGBuffer, new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque })
                {
                    perObjectData = PerObjectData.MotionVectors,
                    enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch,
                    enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch
                };
                graphContext.renderContext.ExecuteCommandBuffer(graphContext.cmdBuffer);
                graphContext.cmdBuffer.Clear();
                graphContext.renderContext.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
            });
        }

    }
}
