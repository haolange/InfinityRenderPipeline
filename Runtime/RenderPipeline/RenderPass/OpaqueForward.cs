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

        void RenderOpaqueForward(Camera RenderCamera, FCullingData CullingData, in CullingResults CullingResult)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(CullingResult, RenderCamera, InfinityPassIDs.ForwardPlus, new RenderQueueRange(0, 2999), PerObjectData.Lightmaps));

            RDGTextureRef DepthTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);

            RDGTextureDesc DiffuseDesc = new RDGTextureDesc(Screen.width, Screen.height) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "DiffuseTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef DiffuseTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, DiffuseDesc);

            RDGTextureDesc SpecularBDesc = new RDGTextureDesc(Screen.width, Screen.height) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "SpecularTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef SpecularTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, SpecularBDesc);


            //Add OpaqueForwardPass
            GraphBuilder.AddPass<FOpaqueForwardData>("OpaqueForward", ProfilingSampler.Get(CustomSamplerId.OpaqueForward),
            (ref FOpaqueForwardData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.DiffuseBuffer = PassBuilder.UseColorBuffer(DiffuseTexture, 0);
                PassData.SpecularBuffer = PassBuilder.UseColorBuffer(SpecularTexture, 1);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.Read);
                ForwardPassMeshProcessor.DispatchGather(CullingData, new FMeshPassDesctiption(0, 2999));
            },
            (ref FOpaqueForwardData PassData, RDGContext GraphContext) =>
            {
                //UnityRenderer
                RendererList ForwardRenderList = PassData.RendererList;
                ForwardRenderList.drawSettings.perObjectData = PerObjectData.Lightmaps;
                ForwardRenderList.drawSettings.enableInstancing = RenderPipelineAsset.EnableInstanceBatch;
                ForwardRenderList.drawSettings.enableDynamicBatching = RenderPipelineAsset.EnableDynamicBatch;
                ForwardRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(0, 2999);
                GraphContext.RenderContext.DrawRenderers(ForwardRenderList.cullingResult, ref ForwardRenderList.drawSettings, ref ForwardRenderList.filteringSettings);

                //MeshDrawPipeline
                ForwardPassMeshProcessor.DispatchDraw(GraphContext, 4);
            });
        }
    }
}
