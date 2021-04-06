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
        struct FOpaqueForwardData
        {
            public RendererList RendererList;
            public RDGTextureRef DepthBuffer;
            public RDGTextureRef DiffuseBuffer;
            public RDGTextureRef SpecularBuffer;
        }

        void RenderOpaqueForward(Camera camera, FCullingData cullingData, in CullingResults cullingResult)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(cullingResult, camera, InfinityPassIDs.ForwardPlus, new RenderQueueRange(0, 2999), PerObjectData.Lightmaps));

            RDGTextureRef DepthTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescription DiffuseDesc = new TextureDescription(Screen.width, Screen.height) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "DiffuseTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef DiffuseTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, DiffuseDesc);

            TextureDescription SpecularBDesc = new TextureDescription(Screen.width, Screen.height) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "SpecularTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef SpecularTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, SpecularBDesc);


            //Add OpaqueForwardPass
            GraphBuilder.AddPass<FOpaqueForwardData>("OpaqueForward", ProfilingSampler.Get(CustomSamplerId.OpaqueForward),
            (ref FOpaqueForwardData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.DiffuseBuffer = PassBuilder.UseColorBuffer(DiffuseTexture, 0);
                PassData.SpecularBuffer = PassBuilder.UseColorBuffer(SpecularTexture, 1);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.Read);
                ForwardPassMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(0, 2999));
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
                ForwardPassMeshProcessor.DispatchDraw(GraphContext, 2);
            });
        }
    }
}
