using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct FOpaqueDepthData
        {
            public RendererList RendererList;
            public RDGTextureRef DepthBuffer;
        }

        void RenderOpaqueDepth(Camera camera, FCullingData cullingData, CullingResults cullingResult)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(cullingResult, camera, InfinityPassIDs.OpaqueDepth, new RenderQueueRange(2450, 2999)));

            TextureDescription DepthDesc = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "DepthTexture", depthBufferBits = EDepthBits.Depth32 };
            RDGTextureRef DepthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer, DepthDesc);

            //Add OpaqueDepthPass
            m_GraphBuilder.AddPass<FOpaqueDepthData>("OpaqueDepth", ProfilingSampler.Get(CustomSamplerId.OpaqueDepth),
            (ref FOpaqueDepthData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.ReadWrite);
                m_DepthPassMeshProcessor.DispatchSetup(ref cullingData, new FMeshPassDesctiption(2450, 2999));
            },
            (ref FOpaqueDepthData PassData, RDGContext GraphContext) =>
            {
                RendererList DepthRenderList = PassData.RendererList;
                DepthRenderList.drawSettings.sortingSettings = new SortingSettings(camera) { criteria = SortingCriteria.QuantizedFrontToBack };
                DepthRenderList.drawSettings.enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch;
                DepthRenderList.drawSettings.enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch;
                DepthRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(2450, 2999);
                GraphContext.RenderContext.DrawRenderers(DepthRenderList.cullingResult, ref DepthRenderList.drawSettings, ref DepthRenderList.filteringSettings);

                //MeshDrawPipeline
                m_DepthPassMeshProcessor.DispatchDraw(GraphContext, 0);
            });
        }
    }
}