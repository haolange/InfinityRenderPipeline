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
            public RendererList rendererList;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef motionBuffer;
        }

        void RenderOpaqueMotion(Camera camera, FCullingData cullingData, CullingResults cullingResult)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            RendererList rendererList = RendererList.Create(CreateRendererListDesc(camera, cullingResult, InfinityPassIDs.OpaqueMotion, RenderQueueRange.opaque, PerObjectData.MotionVectors));
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription motionDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, clearColor = Color.clear, enableMSAA = false, bindTextureMS = false, name = FOpaqueMotionString.TextureName, colorFormat = GraphicsFormat.R16G16_SFloat };
            RDGTextureRef motionTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.MotionBuffer, motionDescription);

            //Add OpaqueMotionPass
            m_GraphBuilder.AddPass<FOpaqueMotionData>(FOpaqueMotionString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueMotion),
            (ref FOpaqueMotionData passData, ref RDGPassBuilder passBuilder) =>
            {
                passData.rendererList = rendererList;
                passData.motionBuffer = passBuilder.UseColorBuffer(motionTexture, 0);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.Read);
            },
            (ref FOpaqueMotionData passData, ref RDGGraphContext graphContext) =>
            {
                passData.rendererList.drawSettings.sortingSettings = new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque };
                passData.rendererList.drawSettings.perObjectData = PerObjectData.MotionVectors;
                passData.rendererList.drawSettings.enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch;
                passData.rendererList.drawSettings.enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch;
                passData.rendererList.filteringSettings.renderQueueRange = RenderQueueRange.opaque;
                passData.rendererList.filteringSettings.excludeMotionVectorObjects = false;
                graphContext.renderContext.DrawRenderers(passData.rendererList.cullingResult, ref passData.rendererList.drawSettings, ref passData.rendererList.filteringSettings);
            });
        }

    }
}
