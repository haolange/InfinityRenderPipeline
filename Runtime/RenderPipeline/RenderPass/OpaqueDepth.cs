using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Runtime.Rendering.RDG;

namespace InfinityTech.Runtime.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct FOpaqueDepthData
        {
            public RendererList RendererList;
            public RDGTextureRef DepthBuffer;
        }

        void RenderOpaqueDepth(Camera RenderCamera, CullingResults CullingData)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(CullingData, RenderCamera, InfinityPassIDs.OpaqueDepth));

            RDGTextureDesc DepthDesc = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "DepthTexture", depthBufferBits = EDepthBits.Depth32 };
            RDGTextureRef DepthTexture = GraphBuilder.CreateTexture(DepthDesc, InfinityShaderIDs.RT_DepthBuffer);

            GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_DepthBuffer, DepthTexture);

            //Add RenderPass
            GraphBuilder.AddRenderPass<FOpaqueDepthData>("OpaqueDepth", ProfilingSampler.Get(CustomSamplerId.OpaqueDepth),
            (ref FOpaqueDepthData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.ReadWrite);
            },
            (ref FOpaqueDepthData PassData, RDGContext GraphContext) =>
            {
                RendererList DepthRenderList = PassData.RendererList;
                DepthRenderList.drawSettings.sortingSettings = new SortingSettings(RenderCamera) { criteria = SortingCriteria.QuantizedFrontToBack };
                DepthRenderList.drawSettings.enableInstancing = RenderPipelineAsset.EnableInstanceBatch;
                DepthRenderList.drawSettings.enableDynamicBatching = RenderPipelineAsset.EnableDynamicBatch;
                DepthRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(2450, 3000);

                GraphContext.RenderContext.DrawRenderers(DepthRenderList.cullingResult, ref DepthRenderList.drawSettings, ref DepthRenderList.filteringSettings);
            });
        }
    }
}