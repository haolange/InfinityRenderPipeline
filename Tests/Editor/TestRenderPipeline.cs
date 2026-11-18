using System;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // A second pipeline on InfinityRenderPipelineBase: depth only, presented to the camera target. It is a compile-time
    // reader of the base API (constructor, capabilities, RecordCamera, the protected services); it is never instantiated.
    // Its mesh pass table is its own: depth, and the shadow caster pass every light context collects casters from.
    internal sealed class TestRenderPipeline : InfinityRenderPipelineBase
    {
        static readonly MeshPassDesc[] s_MeshPasses =
        {
            new MeshPassDesc("RasterDepthPass", "Depth", 0, 2999),
            new MeshPassDesc("RasterShadowPass", "Shadow", 0, 2999, EMeshEntityFlags.CastShadow)
        };
        static readonly MeshPassId s_DepthMeshPass = new MeshPassId(0);
        static readonly MeshPassId s_ShadowMeshPass = new MeshPassId(1);

        static readonly PipelineCapabilities s_Capabilities = new PipelineCapabilities(
            new[] { EFrameFeature.Depth, EFrameFeature.Display },
            new[] { EFrameFeature.Depth, EFrameFeature.Display },
            asyncCompute: false, meshPasses: s_MeshPasses, shadowCasterPass: s_ShadowMeshPass);

        readonly DepthPass m_DepthPass;
        readonly FinalOutputPass m_FinalOutputPass;

        internal TestRenderPipeline(InfinityRenderPipelineAsset asset) : base(asset, s_Capabilities)
        {
            try
            {
                m_DepthPass = new DepthPass(renderContext.graph, settings, s_DepthMeshPass);
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
            // One frustum view without motion: depth needs no previous transforms.
            RGMeshViewHandle viewHandle = renderContext.graph.CullMeshView(new MeshViewDesc(context.mainCulling, default, MeshOccluder.None));
            m_DepthPass.RasterDepth(context, viewHandle);
            RGTextureHandle targetHandle = m_FinalOutputPass.ImportCameraOutput(context);
            RGTextureHandle depthTextureHandle = renderContext.graph.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            context.features.ThrowIfCannotProduce(EFrameFeature.Display);
            m_FinalOutputPass.RasterFinalOutput(context, depthTextureHandle, targetHandle, true);
            context.features.MarkProduced(EFrameFeature.Display);
        }
    }
}
