using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct FOpaqueForwardData
        {
            public RendererList RendererList;
            public RDGTextureRef DepthBuffer;
            public RDGTextureRef DiffuseBuffer;
            public RDGTextureRef SpecularBuffer;
        }

        void RenderOpaqueForward(Camera RenderCamera, FGPUScene GPUScene, FCullingData CullingData, in CullingResults CullingResult)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(CullingResult, RenderCamera, InfinityPassIDs.ForwardPlus, new RenderQueueRange(0, 2999), PerObjectData.Lightmaps));

            RDGTextureRef DepthTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);

            RDGTextureDesc DiffuseDesc = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "DiffuseTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef DiffuseTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, DiffuseDesc);

            RDGTextureDesc SpecularBDesc = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "SpecularTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef SpecularTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, SpecularBDesc);

            //Add OpaqueGBufferPass
            GraphBuilder.AddPass<FOpaqueForwardData>("OpaqueForward", ProfilingSampler.Get(CustomSamplerId.OpaqueForward),
            (ref FOpaqueForwardData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.DiffuseBuffer = PassBuilder.UseColorBuffer(DiffuseTexture, 0);
                PassData.SpecularBuffer = PassBuilder.UseColorBuffer(SpecularTexture, 1);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.Read);
            },
            (ref FOpaqueForwardData PassData, RDGContext GraphContext) =>
            {
                //UnityRenderer
                RendererList GBufferRenderList = PassData.RendererList;
                GBufferRenderList.drawSettings.perObjectData = PerObjectData.Lightmaps;
                GBufferRenderList.drawSettings.enableInstancing = RenderPipelineAsset.EnableInstanceBatch;
                GBufferRenderList.drawSettings.enableDynamicBatching = RenderPipelineAsset.EnableDynamicBatch;
                GBufferRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(0, 2999);
                GraphContext.RenderContext.DrawRenderers(GBufferRenderList.cullingResult, ref GBufferRenderList.drawSettings, ref GBufferRenderList.filteringSettings);

                //MeshDrawPipeline
                FMeshPassProcessor OpaqueMeshProcessor = GraphContext.ObjectPool.Get<FMeshPassProcessor>();
                FMeshPassDesctiption OpaqueMeshPassDescription = new FMeshPassDesctiption(GBufferRenderList);
                OpaqueMeshProcessor.DispatchDraw(GraphContext, GPUScene, CullingData, OpaqueMeshPassDescription);
            });
        }
    }
}
