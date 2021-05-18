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
        struct FOpaqueGBufferData
        {
            public RDGTextureRef GBufferA;
            public RDGTextureRef GBufferB;
            public RDGTextureRef DepthBuffer;
            public RendererList RendererList;
        }

        void RenderOpaqueGBuffer(Camera camera, FCullingData cullingData, in CullingResults cullingResult)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(cullingResult, camera, InfinityPassIDs.OpaqueGBuffer));

            RDGTextureRef DepthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescription GBufferADesc = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferATexture", colorFormat = GraphicsFormat.R8G8B8A8_UNorm };
            RDGTextureRef GBufferATexure = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferA, GBufferADesc);

            TextureDescription GBufferBDesc = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferBTexture", colorFormat = GraphicsFormat.A2B10G10R10_UIntPack32 };
            RDGTextureRef GBufferBTexure = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferB, GBufferBDesc);

            //Add OpaqueGBufferPass
            m_GraphBuilder.AddPass<FOpaqueGBufferData>("OpaqueGBuffer", ProfilingSampler.Get(CustomSamplerId.OpaqueGBuffer),
            (ref FOpaqueGBufferData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.GBufferA = PassBuilder.UseColorBuffer(GBufferATexure, 0);
                PassData.GBufferB = PassBuilder.UseColorBuffer(GBufferBTexure, 1);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.ReadWrite);
                m_GBufferPassMeshProcessor.DispatchSetup(ref cullingData, new FMeshPassDesctiption(0, 2999));
            },
            (ref FOpaqueGBufferData PassData, RDGContext GraphContext) =>
            {
                //UnityRenderer
                RendererList GBufferRenderList = PassData.RendererList;
                //GBufferRenderList.drawSettings.perObjectData = PerObjectData.Lightmaps;
                GBufferRenderList.drawSettings.enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch;
                GBufferRenderList.drawSettings.enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch;
                GBufferRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(0, 2999);
                GraphContext.RenderContext.DrawRenderers(GBufferRenderList.cullingResult, ref GBufferRenderList.drawSettings, ref GBufferRenderList.filteringSettings);

                //MeshDrawPipeline
                m_GBufferPassMeshProcessor.DispatchDraw(GraphContext, 1);
            });
        }
    }
}