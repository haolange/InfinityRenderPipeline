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
            public RendererList rendererList;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef diffuseBuffer;
            public RDGTextureRef specularBuffer;
        }

        void RenderOpaqueForward(Camera camera, FCullingData cullingData, in CullingResults cullingResult)
        {
            RendererList rendererList = RendererList.Create(CreateRendererListDesc(cullingResult, camera, InfinityPassIDs.ForwardPlus, new RenderQueueRange(0, 2999), PerObjectData.Lightmaps));
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription diffuseDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "DiffuseTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef diffuseTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, diffuseDescription);
            TextureDescription specularDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "SpecularTexture", colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef specularTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, specularDescription);

            //Add OpaqueForwardPass
            m_GraphBuilder.AddPass<FOpaqueForwardData>("OpaqueForward", ProfilingSampler.Get(CustomSamplerId.OpaqueForward),
            (ref FOpaqueForwardData passData, ref RDGPassBuilder passBuilder) =>
            {
                passData.rendererList = rendererList;
                passData.diffuseBuffer = passBuilder.UseColorBuffer(diffuseTexture, 0);
                passData.specularBuffer = passBuilder.UseColorBuffer(specularTexture, 1);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.Read);
                m_ForwardMeshProcessor.DispatchSetup(ref cullingData, new FMeshPassDesctiption(0, 2999));
            },
            (ref FOpaqueForwardData passData, ref RDGGraphContext graphContext) =>
            {
                //MeshDrawPipeline
                m_ForwardMeshProcessor.DispatchDraw(ref graphContext, 2);

                //UnityDrawPipeline
                passData.rendererList.drawSettings.perObjectData = PerObjectData.Lightmaps;
                passData.rendererList.drawSettings.enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch;
                passData.rendererList.drawSettings.enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch;
                passData.rendererList.filteringSettings.renderQueueRange = new RenderQueueRange(0, 2999);
                graphContext.renderContext.DrawRenderers(passData.rendererList.cullingResult, ref passData.rendererList.drawSettings, ref passData.rendererList.filteringSettings);
            });
        }
    }
}
