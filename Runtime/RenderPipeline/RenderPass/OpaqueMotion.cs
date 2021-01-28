using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct FOpaqueMotionData
        {
            public RendererList RendererList;
            public RDGTextureRef DepthBuffer;
            public RDGTextureRef MotionBuffer;
        }

        void RenderOpaqueMotion(Camera RenderCamera, FCullingData CullingData, CullingResults CullingResult)
        {
            RenderCamera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(CullingResult, RenderCamera, InfinityPassIDs.OpaqueMotion, RenderQueueRange.opaque, PerObjectData.MotionVectors));

            RDGTextureRef DepthTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);

            RDGTextureDesc MotionDesc = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, clearColor = Color.clear, enableMSAA = false, bindTextureMS = false, name = "MotionBufferTexture", colorFormat = GraphicsFormat.R16G16_SFloat };
            RDGTextureRef MotionTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.MotionBuffer, MotionDesc);

            //Add OpaqueMotionPass
            GraphBuilder.AddPass<FOpaqueMotionData>("OpaqueMotion", ProfilingSampler.Get(CustomSamplerId.OpaqueMotion),
            (ref FOpaqueMotionData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.MotionBuffer = PassBuilder.UseColorBuffer(MotionTexture, 0);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.Read);
            },
            (ref FOpaqueMotionData PassData, RDGContext GraphContext) =>
            {
                RendererList MotionRenderList = PassData.RendererList;
                MotionRenderList.drawSettings.sortingSettings = new SortingSettings(RenderCamera) { criteria = SortingCriteria.CommonOpaque };
                MotionRenderList.drawSettings.perObjectData = PerObjectData.MotionVectors;
                MotionRenderList.drawSettings.enableInstancing = RenderPipelineAsset.EnableInstanceBatch;
                MotionRenderList.drawSettings.enableDynamicBatching = RenderPipelineAsset.EnableDynamicBatch;
                MotionRenderList.filteringSettings.renderQueueRange = RenderQueueRange.opaque;
                MotionRenderList.filteringSettings.excludeMotionVectorObjects = false;

                GraphContext.RenderContext.DrawRenderers(MotionRenderList.cullingResult, ref MotionRenderList.drawSettings, ref MotionRenderList.filteringSettings);
            });
        }

    }
}
