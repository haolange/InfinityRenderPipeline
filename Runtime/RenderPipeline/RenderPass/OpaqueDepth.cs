using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FOpaqueDepthString
    {
        internal static string PassName = "OpaqueDepth";
        internal static string TextureName = "DepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FOpaqueDepthData
        {
            public RendererList rendererList;
            public RDGTextureRef depthBuffer;
        }

        void RenderOpaqueDepth(Camera camera, FCullingData cullingData, CullingResults cullingResult)
        {
            RendererList rendererList = RendererList.Create(CreateRendererListDesc(camera, cullingResult, InfinityPassIDs.OpaqueDepth, new RenderQueueRange(2450, 2999)));
            TextureDescription depthDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueDepthString.TextureName, depthBufferBits = EDepthBits.Depth32 };
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer, depthDescription);

            //Add OpaqueDepthPass
            m_GraphBuilder.AddPass<FOpaqueDepthData>(FOpaqueDepthString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueDepth),
            (ref FOpaqueDepthData passData, ref RDGPassBuilder passBuilder) =>
            {
                passData.rendererList = rendererList;
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                m_DepthMeshProcessor.DispatchSetup(ref cullingData, new FMeshPassDesctiption(2450, 2999));
            },
            (ref FOpaqueDepthData passData, ref RDGGraphContext graphContext) =>
            {
                //MeshDrawPipeline
                m_DepthMeshProcessor.DispatchDraw(ref graphContext, 0);

                //UnityDrawPipeline
                passData.rendererList.drawSettings.sortingSettings = new SortingSettings(camera) { criteria = SortingCriteria.QuantizedFrontToBack };
                passData.rendererList.drawSettings.enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch;
                passData.rendererList.drawSettings.enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch;
                passData.rendererList.filteringSettings.renderQueueRange = new RenderQueueRange(2450, 2999);
                graphContext.renderContext.DrawRenderers(passData.rendererList.cullingResult, ref passData.rendererList.drawSettings, ref passData.rendererList.filteringSettings);
            });
        }
    }
}