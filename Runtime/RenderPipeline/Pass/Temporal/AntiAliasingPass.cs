using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AntiAliasingUtilityData
    {
        internal static readonly int HistoryColorTextureID = Shader.PropertyToID("HistoryColorTexture");
        internal static readonly int HistoryDepthTextureID = Shader.PropertyToID("HistoryDepthTexture");
    }
    public partial class InfinityRenderPipeline
    {
        struct AntiAliasingPassData
        {
            public Vector4 resolution, depthParameters, jitter;
            public bool historyValid, diagnostics;
            public int kernel;
            public ComputeShader shader;
            public RGTextureRef input, motion, metadata, reactive, historyColor, historyDepth;
            public RGTextureRef accumulation, temporalDepth, sharpened, confidence, reprojection;
            public CaptureStageSnapshot inputSnapshot, historyColorSnapshot, historyDepthSnapshot;
        }
        static TextureDescriptor TemporalColorDescriptor(int width, int height, string name, bool writable = false)
            => new TextureDescriptor(width, height, ETextureSizingDomain.Internal) { name = name, dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = writable, wrapMode = TextureWrapMode.Clamp };
        static TextureDescriptor TemporalDepthDescriptor(int width, int height, string name, bool writable = false)
            => new TextureDescriptor(width, height, ETextureSizingDomain.Internal) { name = name, dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R32_SFloat, enableRandomWrite = writable, wrapMode = TextureWrapMode.Clamp };

        internal static Vector2 ProjectionJitterUV(Matrix4x4 jittered, Matrix4x4 unjittered)
        {
            Vector4 point = new Vector4(0, 0, -1, 1);
            Vector4 a = jittered * point, b = unjittered * point;
            return new Vector2(a.x / a.w - b.x / b.w, a.y / a.w - b.y / b.w) * 0.5f;
        }
        void ComputeAntiAliasing(RenderContext context, Camera camera, HistoryCache history, CameraUniform uniform)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.TAA);
            ComputeShader shader = shaders.taaShader;
            if (shader == null || !shader.HasKernel("Main") || !shader.HasKernel("MainDebug"))
                throw new InvalidOperationException("TAA requires its fused accumulation/sharpen and diagnostics kernels.");
            var depthSource = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var depthDescription = m_RGBuilder.GetTextureDescriptor(depthSource);
            int width = depthDescription.width, height = depthDescription.height;
            var colorDescriptor = TemporalColorDescriptor(width, height, "HistoryColorTexture", true);
            var depthDescriptor = TemporalDepthDescriptor(width, height, "HistoryDepthTexture", true);
            var historyColor = m_RGBuilder.ImportTexture(history.GetTexture(AntiAliasingUtilityData.HistoryColorTextureID, colorDescriptor, out bool colorCreated));
            var historyDepth = m_RGBuilder.ImportTexture(history.GetTexture(AntiAliasingUtilityData.HistoryDepthTextureID, depthDescriptor, out bool depthCreated));
            var writeColor = TemporalColorDescriptor(width, height, "HistoryColorTexture", true);
            var writeDepth = TemporalDepthDescriptor(width, height, "HistoryDepthTexture", true);
            // Both sides of the ping-pong history have identical physical usage flags.
            var accumulation = m_RGBuilder.ImportTexture(history.GetWriteTexture(AntiAliasingUtilityData.HistoryColorTextureID, writeColor));
            var temporalDepth = m_RGBuilder.ImportTexture(history.GetWriteTexture(AntiAliasingUtilityData.HistoryDepthTextureID, writeDepth));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.TAAAccumulationBuffer, accumulation);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.TAADepthBuffer, temporalDepth);
            history.MarkProduced(AntiAliasingUtilityData.HistoryColorTextureID);
            history.MarkProduced(AntiAliasingUtilityData.HistoryDepthTextureID);
            var sharpened = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, TemporalColorDescriptor(width, height, "TAASharpened", true));
            var input = m_RGScoper.QueryTexture(TranslucentFeatureUtility.ResolveTemporalSceneColorId());
            bool diagnostics = m_ActiveFrameState.debugView != EDebugView.None ||
                (RenderCaptureService.current != null && RenderCaptureService.current.CaptureThisFrame && RenderCaptureService.current.request.includeConfidence);
            RGTextureRef confidence = default, reprojection = default;
            if (diagnostics)
            {
                var descriptor = TemporalColorDescriptor(width, height, "TAADiagnostics", true);
                descriptor.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
                confidence = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAConfidenceBuffer, descriptor);
                descriptor.name = "TAAReprojection";
                reprojection = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAReprojectionBuffer, descriptor);
            }
            using (var pass = m_RGBuilder.AddComputePass<AntiAliasingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAntiAliasing)))
            {
                ref var data = ref pass.GetPassData<AntiAliasingPassData>();
                data.shader = shader; data.kernel = shader.FindKernel(diagnostics ? "MainDebug" : "Main");
                data.resolution = new Vector4(width, height, 1f / width, 1f / height);
                data.depthParameters = new Vector4(camera.nearClipPlane, camera.farClipPlane, camera.orthographic ? 1 : 0, SystemInfo.usesReversedZBuffer ? 1 : 0);
                Vector2 currentJitter = ProjectionJitterUV(uniform.matrix_FlipYJitterProj, uniform.matrix_FlipYProj);
                data.jitter = new Vector4(currentJitter.x, currentJitter.y, uniform.previousJitterUV.x, uniform.previousJitterUV.y);
                data.historyValid = !uniform.historyReset && !colorCreated && !depthCreated;
                data.diagnostics = diagnostics;
                data.input = pass.ReadTexture(input);
                data.motion = pass.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                data.metadata = pass.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                data.reactive = pass.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.ReactiveMaskBuffer));
                data.historyColor = pass.ReadTexture(historyColor); data.historyDepth = pass.ReadTexture(historyDepth);
                data.accumulation = pass.WriteTexture(accumulation);
                data.sharpened = pass.WriteTexture(sharpened); data.temporalDepth = pass.WriteTexture(temporalDepth);
                if (diagnostics) { data.confidence = pass.WriteTexture(confidence); data.reprojection = pass.WriteTexture(reprojection); }
                data.inputSnapshot = PrepareStageSnapshot(pass, "TAAInput", input);
                data.historyColorSnapshot = PrepareStageSnapshot(pass, "TAAHistoryColor", historyColor);
                data.historyDepthSnapshot = PrepareStageSnapshot(pass, "TAAHistoryDepth", historyDepth);
                pass.SetExecuteFunc((in AntiAliasingPassData data, in RGComputeEncoder commands, RGObjectPool pool) =>
                {
                    data.inputSnapshot.Record(commands, data.input);
                    data.historyColorSnapshot.Record(commands, data.historyColor);
                    data.historyDepthSnapshot.Record(commands, data.historyDepth);
                    int kernel = data.kernel; var shader = data.shader;
                    commands.SetComputeVectorParam(shader, Shader.PropertyToID("TAA_Resolution"), data.resolution);
                    commands.SetComputeVectorParam(shader, Shader.PropertyToID("ScreenSpaceDepthParams"), data.depthParameters);
                    commands.SetComputeVectorParam(shader, Shader.PropertyToID("TAA_JitterUV"), data.jitter);
                    commands.SetComputeFloatParam(shader, Shader.PropertyToID("TAA_ResetBlend"), data.historyValid ? 1 : 0);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_AliasingColorTexture"), data.input);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_HistoryColorTexture"), data.historyColor);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_HistoryDepthTexture"), data.historyDepth);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_MotionTexture"), data.motion);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_MotionMetadata"), data.metadata);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_ReactiveMaskTexture"), data.reactive);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("UAV_AccumulateColorTexture"), data.accumulation);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("UAV_TemporalDepthTexture"), data.temporalDepth);
                    commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("UAV_SharpenedColorTexture"), data.sharpened);
                    if (data.diagnostics)
                    {
                        commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("UAV_TAADiagnostics"), data.confidence);
                        commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("UAV_TAAReprojection"), data.reprojection);
                    }
                    commands.DispatchCompute(shader, kernel, ((int)data.resolution.x + 7) / 8, ((int)data.resolution.y + 7) / 8, 1);
                });
            }
            MarkFeatureProduced(EFrameFeature.TAA);
        }
    }
}
