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

        void RenderOpaqueGBuffer(Camera RenderCamera, FCullingData CullingData, in CullingResults CullingResult)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(CullingResult, RenderCamera, InfinityPassIDs.OpaqueGBuffer));

            RDGTextureRef DepthTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescription GBufferADesc = new TextureDescription(Screen.width, Screen.height) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferATexture", colorFormat = GraphicsFormat.R8G8B8A8_UNorm };
            RDGTextureRef GBufferATexure = GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferA, GBufferADesc);

            TextureDescription GBufferBDesc = new TextureDescription(Screen.width, Screen.height) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferBTexture", colorFormat = GraphicsFormat.A2B10G10R10_UIntPack32 };
            RDGTextureRef GBufferBTexure = GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferB, GBufferBDesc);

            //Add OpaqueGBufferPass
            GraphBuilder.AddPass<FOpaqueGBufferData>("OpaqueGBuffer", ProfilingSampler.Get(CustomSamplerId.OpaqueGBuffer),
            (ref FOpaqueGBufferData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.GBufferA = PassBuilder.UseColorBuffer(GBufferATexure, 0);
                PassData.GBufferB = PassBuilder.UseColorBuffer(GBufferBTexure, 1);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.ReadWrite);
                GBufferPassMeshProcessor.DispatchSetup(CullingData, new FMeshPassDesctiption(0, 2999));
            },
            (ref FOpaqueGBufferData PassData, RDGContext GraphContext) =>
            {
                //UnityRenderer
                RendererList GBufferRenderList = PassData.RendererList;
                //GBufferRenderList.drawSettings.perObjectData = PerObjectData.Lightmaps;
                GBufferRenderList.drawSettings.enableInstancing = RenderPipelineAsset.EnableInstanceBatch;
                GBufferRenderList.drawSettings.enableDynamicBatching = RenderPipelineAsset.EnableDynamicBatch;
                GBufferRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(0, 2999);
                GraphContext.RenderContext.DrawRenderers(GBufferRenderList.cullingResult, ref GBufferRenderList.drawSettings, ref GBufferRenderList.filteringSettings);

                //MeshDrawPipeline
                GBufferPassMeshProcessor.DispatchDraw(GraphContext, 1);
            });
        }
    }
}


/*var RTV_ThinGBuffer_ID = GraphContext.ObjectPool.GetTempArray<RenderTargetIdentifier>(2);
RTV_ThinGBuffer_ID[0] = PassData.GBufferA;
RTV_ThinGBuffer_ID[1] = PassData.GBufferB;
CoreUtils.SetRenderTarget(GraphContext.CmdBuffer, RTV_ThinGBuffer_ID, PassData.DepthBuffer);

GraphContext.RenderContext.ExecuteCommandBuffer(GraphContext.CmdBuffer);
GraphContext.CmdBuffer.Clear();*/