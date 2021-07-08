using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FOpaqueForwardString
    {
        internal static string PassName = "OpaqueForward";
        internal static string TextureAName = "DiffuseTexture";
        internal static string TextureBName = "SpecularTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FOpaqueForwardData
        {
            public RDGTextureRef depthBuffer;
            public RDGTextureRef diffuseBuffer;
            public RDGTextureRef specularBuffer;
        }

        void RenderOpaqueForward(Camera camera, FCullingData cullingData, CullingResults cullingResults)
        {
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription diffuseDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueForwardString.TextureAName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef diffuseTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, diffuseDescription);
            TextureDescription specularDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueForwardString.TextureBName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef specularTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, specularDescription);

            //Add OpaqueForwardPass
            m_GraphBuilder.AddPass<FOpaqueForwardData>(FOpaqueForwardString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueForward),
            (ref FOpaqueForwardData passData, ref RDGPassBuilder passBuilder) =>
            {
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
                FilteringSettings filteringSettings = new FilteringSettings
                {
                    //renderingLayerMask = 1,
                    //layerMask = RenderCamera.cullingMask,
                    renderQueueRange = new RenderQueueRange(0, 2999),
                };
                DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.ForwardPlus, new SortingSettings(camera) { criteria = SortingCriteria.OptimizeStateChanges })
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
