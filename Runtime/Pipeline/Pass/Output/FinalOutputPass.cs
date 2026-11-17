using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FinalOutputPassUtilityData
    {
        internal static readonly int InfinityOutputTransferID = Shader.PropertyToID("_InfinityOutputTransfer");
    }

    internal sealed class FinalOutputPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;

        internal FinalOutputPass(RGBuilder graph, InfinityRenderPipelineResources resources)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
        }

        internal struct FinalOutputPassData
        {
            public Rect viewport;
            public Vector4 scaleBias, outputTransfer;
            public int shaderPass, sourceWidth, sourceHeight;
            public RGTextureHandle srcTextureHandle;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public RenderCaptureSession captureSession;
            public NativeDisplayProbe displayProbe;
#endif
        }

        internal FinalOutputPassData PrepareFinalOutput(in ViewContext context, RGTextureHandle sourceHandle, bool finalTarget, int shaderPass)
        {
            var dimensions = context.dimensions;
            var descriptor = m_RGBuilder.GetTextureDescriptor(sourceHandle);
            var data = new FinalOutputPassData
            {
                srcTextureHandle = sourceHandle, sourceWidth = descriptor.width, sourceHeight = descriptor.height,
                viewport = finalTarget ? dimensions.outputViewport : new Rect(0, 0, dimensions.displaySize.x, dimensions.displaySize.y),
                scaleBias = finalTarget && context.camera.targetTexture == null ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0),
                outputTransfer = finalTarget ? new Vector4((int)context.outputDecision.policy, context.outputDecision.outputGamut == OutputTransformUtility.OutputGamutRec2020 ? 1 : 0, context.outputDecision.paperWhiteNits, 0)
                    : new Vector4((int)EOutputEncodePolicy.LinearTarget, 0, 100, 0),
                shaderPass = shaderPass
            };
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (finalTarget)
            {
                var capture = RenderCaptureService.current;
                data.captureSession = capture != null && !capture.Finished && capture.request.Matches(context.camera) ? capture : null;
                data.captureSession?.RecordOutputDecision(context.outputDecision);
                data.captureSession?.RecordPresent(context.camera, data.viewport, data.scaleBias, data.sourceWidth, data.sourceHeight, false);
                data.displayProbe = data.captureSession?.ReserveNativeProbe();
            }
#endif
            return data;
        }

        internal static void BindFinalOutput(in FinalOutputPassData data, in RGRasterEncoder commands)
        {
            commands.SetViewport(data.viewport);
            commands.SetGlobalVector(FinalOutputPassUtilityData.InfinityOutputTransferID, data.outputTransfer);
            commands.SetGlobalVector(InfinityShaderIDs.ScaleBias, data.scaleBias);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        internal static void CompleteFinalOutput(in FinalOutputPassData data, in RGRasterEncoder commands)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            data.captureSession?.RecordPresent(null, data.viewport, data.scaleBias, data.sourceWidth, data.sourceHeight, true);
            if (data.displayProbe != null)
                commands.IssuePluginEventAndData(data.displayProbe.callback, 0, data.displayProbe.payload);
#endif
        }

        internal void RasterFinalOutput(in ViewContext context, RGTextureHandle sourceHandle, RGTextureHandle targetHandle, bool finalTarget)
        {
            var sampler = finalTarget ? CustomSamplerId.RasterFinalOutput : CustomSamplerId.RasterOutputCopy;
            {
                RGRasterPassHandle<FinalOutputPassData> passHandle = m_RGBuilder.AddRasterPass<FinalOutputPassData>(ProfilingSampler.Get(sampler));
                passHandle.EnablePassCulling(false);
                if (finalTarget)
                {
                    passHandle.EnableNativeRenderPass(false);
                }
                passHandle.SetColorAttachment(targetHandle, 0, finalTarget && !OutputPlan.CoversTarget(context.camera.rect) ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                ref FinalOutputPassData passData = ref passHandle.GetPassData();
                passData = PrepareFinalOutput(context, sourceHandle, finalTarget, m_Resources.blitPasses.finalOutput);
                passData.srcTextureHandle = passHandle.ReadTexture(sourceHandle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (passData.displayProbe != null)
                {
                    passHandle.SetQueueObserver(passData.displayProbe);
                }
#endif
                passHandle.SetExecuteFunc((in FinalOutputPassData passData, in RGRasterEncoder commands) =>
                {
                    BindFinalOutput(passData, commands);
                    commands.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.srcTextureHandle);
                    commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, passData.shaderPass);
                    CompleteFinalOutput(passData, commands);
                });
            }
        }
    }
}
