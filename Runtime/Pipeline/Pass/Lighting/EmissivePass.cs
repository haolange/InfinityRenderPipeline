using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // Adds the emission of deferred opaque surfaces after deferred shading. A forward surface writes its emission in
    // the forward pass, so this pass draws only the deferred route.
    internal sealed class EmissivePass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly MeshPassId m_EmissiveMeshPass;

        internal EmissivePass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings, MeshPassId emissiveMeshPass)
        {
            m_RGBuilder = graph;
            m_Settings = settings;
            m_EmissiveMeshPass = emissiveMeshPass;
        }

        struct EmissivePassData
        {
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
        }

        internal void RasterEmissive(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            RGMeshDrawListHandle meshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_EmissiveMeshPass);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGRendererListHandle rendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterEmissivePass,
                new RenderQueueRange(0, InfinityMeshPasses.OpaqueQueueMax), SortingCriteria.OptimizeStateChanges, PerObjectData.None, m_Settings.enableInstanceBatch));

            RGRasterPassHandle<EmissivePassData> passHandle = m_RGBuilder.AddRasterPass<EmissivePassData>(ProfilingSampler.Get(CustomSamplerId.RasterEmissive));
            passHandle.SetColorAttachment(lightingTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);
            ref EmissivePassData passData = ref passHandle.GetPassData();
            passData.meshDrawListHandle = passHandle.UseMeshDrawList(meshDrawListHandle);
            passData.rendererListHandle = passHandle.UseRendererList(rendererListHandle);
            passHandle.SetExecuteFunc((in EmissivePassData passData, in RGRasterEncoder cmdEncoder) =>
            {
                cmdEncoder.SubmitMeshDrawList(passData.meshDrawListHandle);
                cmdEncoder.DrawRendererList(passData.rendererListHandle);
            });
        }
    }
}
