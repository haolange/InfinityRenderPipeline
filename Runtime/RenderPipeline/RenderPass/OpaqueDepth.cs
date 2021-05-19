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
            public RendererList rendererList;
            public RDGTextureRef depthBuffer;
        }

        void RenderOpaqueDepth(Camera camera, FCullingData cullingData, CullingResults cullingResult)
        {
            RendererList rendererList = RendererList.Create(CreateRendererListDesc(cullingResult, camera, InfinityPassIDs.OpaqueDepth, new RenderQueueRange(2450, 2999)));
            TextureDescription depthDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "DepthTexture", depthBufferBits = EDepthBits.Depth32 };
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer, depthDescription);

            //Add OpaqueDepthPass
            m_GraphBuilder.AddPass<FOpaqueDepthData>("OpaqueDepth", ProfilingSampler.Get(CustomSamplerId.OpaqueDepth),
            (ref FOpaqueDepthData passData, ref RDGPassBuilder passBuilder) =>
            {
                passData.rendererList = rendererList;
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                m_DepthPassMeshProcessor.DispatchSetup(ref cullingData, new FMeshPassDesctiption(2450, 2999));
            },
            (ref FOpaqueDepthData passData, ref RDGContext graphContext) =>
            {
                //UnityDrawPipeline
                passData.rendererList.drawSettings.sortingSettings = new SortingSettings(camera) { criteria = SortingCriteria.QuantizedFrontToBack };
                passData.rendererList.drawSettings.enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch;
                passData.rendererList.drawSettings.enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch;
                passData.rendererList.filteringSettings.renderQueueRange = new RenderQueueRange(2450, 2999);
                graphContext.renderContext.DrawRenderers(passData.rendererList.cullingResult, ref passData.rendererList.drawSettings, ref passData.rendererList.filteringSettings);

                //MeshDrawPipeline
                m_DepthPassMeshProcessor.DispatchDraw(ref graphContext, 0);
            });
        }
    }
}