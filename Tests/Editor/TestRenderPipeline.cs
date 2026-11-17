using System;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // A second pipeline on InfinityRenderPipelineBase: depth only, presented to the camera target. It is a compile-time
    // reader of the base API (constructor, capabilities, RecordCamera, the protected services); it is never instantiated.
    internal sealed class TestRenderPipeline : InfinityRenderPipelineBase
    {
        static readonly PipelineCapabilities s_Capabilities = new PipelineCapabilities(
            new[] { EFrameFeature.Depth, EFrameFeature.Display },
            new[] { EFrameFeature.Depth, EFrameFeature.Display },
            asyncCompute: false);

        readonly DepthPass m_DepthPass;
        readonly FinalOutputPass m_FinalOutputPass;

        internal TestRenderPipeline(InfinityRenderPipelineAsset asset) : base(asset, s_Capabilities)
        {
            try
            {
                m_DepthPass = new DepthPass(renderContext.graph, settings);
                m_FinalOutputPass = new FinalOutputPass(renderContext.graph, resources);
            }
            catch (Exception constructionFailure)
            {
                try { ReleaseOwnedResources(); }
                catch (Exception cleanupFailure) { throw new AggregateException(constructionFailure, cleanupFailure); }
                throw;
            }
        }

        private protected override void RecordCamera(in ViewContext context, CommandBuffer cmdBuffer)
        {
            m_DepthPass.RasterDepth(context);
            RGTextureHandle targetHandle = m_FinalOutputPass.ImportCameraOutput(context);
            RGTextureHandle depthTextureHandle = renderContext.graph.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            context.features.ThrowIfCannotProduce(EFrameFeature.Display);
            m_FinalOutputPass.RasterFinalOutput(context, depthTextureHandle, targetHandle, true);
            context.features.MarkProduced(EFrameFeature.Display);
        }
    }
}
