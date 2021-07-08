using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FOpaqueGBufferString
    {
        internal static string PassName = "OpaqueGBuffer";
        internal static string TextureAName = "GBufferATexture";
        internal static string TextureBName = "GBufferBTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FOpaqueGBufferData
        {
            public RDGTextureRef GBufferA;
            public RDGTextureRef GBufferB;
            public RDGTextureRef depthBuffer;
        }

        void RenderOpaqueGBuffer(Camera camera, FCullingData cullingData, CullingResults cullingResults)
        {
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription GBufferADescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueGBufferString.TextureAName, colorFormat = GraphicsFormat.R8G8B8A8_UNorm };
            RDGTextureRef GBufferATexure = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferA, GBufferADescription);
            TextureDescription GBufferBDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueGBufferString.TextureBName, colorFormat = GraphicsFormat.A2B10G10R10_UIntPack32 };
            RDGTextureRef GBufferBTexure = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferB, GBufferBDescription);

            //Add OpaqueGBufferPass
            m_GraphBuilder.AddPass<FOpaqueGBufferData>(FOpaqueGBufferString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueGBuffer),
            (ref FOpaqueGBufferData passData, ref RDGPassBuilder passBuilder) =>
            {
                passData.GBufferA = passBuilder.UseColorBuffer(GBufferATexure, 0);
                passData.GBufferB = passBuilder.UseColorBuffer(GBufferBTexure, 1);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                m_GBufferMeshProcessor.DispatchSetup(ref cullingData, new FMeshPassDesctiption(0, 2999));
            },
            (ref FOpaqueGBufferData passData, ref RDGGraphContext graphContext) =>
            {
                //MeshDrawPipeline
                m_GBufferMeshProcessor.DispatchDraw(ref graphContext, 1);

                //UnityDrawPipeline
                FilteringSettings filteringSettings = new FilteringSettings
                {
                    //renderingLayerMask = 1,
                    //layerMask = RenderCamera.cullingMask,
                    renderQueueRange = new RenderQueueRange(0, 2999),
                };
                DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.OpaqueGBuffer, new SortingSettings(camera) { criteria = SortingCriteria.QuantizedFrontToBack })
                {
                    perObjectData = PerObjectData.Lightmaps,
                    enableInstancing = m_RenderPipelineAsset.EnableInstanceBatch,
                    enableDynamicBatching = m_RenderPipelineAsset.EnableDynamicBatch
                };
                graphContext.renderContext.ExecuteCommandBuffer(graphContext.cmdBuffer);
                graphContext.cmdBuffer.Clear();
                graphContext.renderContext.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
            });
        }
    }
}