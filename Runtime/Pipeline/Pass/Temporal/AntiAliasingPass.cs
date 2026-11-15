using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_TemporalAntiAliasingParameters Shaders/Pass/Temporal/Compute_TemporalAntiAliasing.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalAntiAliasingParameters
    {
        public Vector4 resolution;
        public float resetBlend;
        public int padding0;
        public int padding1;
        public int padding2;
    }

    internal static class AntiAliasingUtilityData
    {
        internal static readonly int CBV_TemporalAntiAliasingParametersID = Shader.PropertyToID("CBV_TemporalAntiAliasingParameters");
        internal static readonly int SRV_AliasingColorTextureID = Shader.PropertyToID("SRV_AliasingColorTexture");
        internal static readonly int SRV_MotionMetadataID = Shader.PropertyToID("SRV_MotionMetadata");
        internal static readonly int SRV_ReactiveMaskTextureID = Shader.PropertyToID("SRV_ReactiveMaskTexture");
        internal static readonly int UAV_AccumulateColorTextureID = Shader.PropertyToID("UAV_AccumulateColorTexture");
        internal static readonly int UAV_SharpenedColorTextureID = Shader.PropertyToID("UAV_SharpenedColorTexture");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal static readonly int UAV_TAADiagnosticsID = Shader.PropertyToID("UAV_TAADiagnostics");
        internal static readonly int UAV_TAAReprojectionID = Shader.PropertyToID("UAV_TAAReprojection");
#endif
        internal static readonly int UAV_TemporalDepthTextureID = Shader.PropertyToID("UAV_TemporalDepthTexture");
        internal static readonly int HistoryColorTextureID = Shader.PropertyToID("HistoryColorTexture");
        internal static readonly int HistoryDepthTextureID = Shader.PropertyToID("HistoryDepthTexture");
    }
    public partial class InfinityRenderPipeline
    {
        struct AntiAliasingPassData
        {
            public RGConstantsHandle parameters;
            public int kernel;
            public int2 groups;
            public ComputeShader shader;
            public RGTextureHandle input, motion, metadata, reactive, historyColor, historyDepth;
            public RGTextureHandle accumulation, temporalDepth, sharpened;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public bool diagnostics;
            public RGTextureHandle confidence, reprojection;
            public CaptureStageSnapshot inputSnapshot, historyColorSnapshot, historyDepthSnapshot;
#endif
        }
        static TextureDescriptor TemporalColorDescriptor(int width, int height, string name, bool writable = false, ETextureSizingDomain domain = ETextureSizingDomain.Internal)
            => TemporalDescriptor(name, width, height, GraphicsFormat.R16G16B16A16_SFloat, writable, domain);
        static TextureDescriptor TemporalDepthDescriptor(int width, int height, string name, bool writable = false, ETextureSizingDomain domain = ETextureSizingDomain.Internal)
            => TemporalDescriptor(name, width, height, GraphicsFormat.R32_SFloat, writable, domain);
        // Temporal inputs are read at texel centers with point sampling.
        static TextureDescriptor TemporalDescriptor(string name, int width, int height, GraphicsFormat format, bool writable, ETextureSizingDomain domain)
        {
            var descriptor = TextureDescriptor.Create2D(name, width, height, format, domain);
            descriptor.enableRandomWrite = writable;
            descriptor.filterMode = FilterMode.Point;
            return descriptor;
        }

        void ComputeAntiAliasing(in CameraRecordContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.TAA);
            var depthSource = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var depthDescription = m_RGBuilder.GetTextureDescriptor(depthSource);
            int width = depthDescription.width, height = depthDescription.height;
            var colorDescriptor = TemporalColorDescriptor(width, height, "HistoryColorTexture", true);
            var depthDescriptor = TemporalDepthDescriptor(width, height, "HistoryDepthTexture", true);
            HistoryRead colorRead = context.historyCache.Read(AntiAliasingUtilityData.HistoryColorTextureID, colorDescriptor, context.resetsHistory);
            HistoryRead depthRead = context.historyCache.Read(AntiAliasingUtilityData.HistoryDepthTextureID, depthDescriptor, context.resetsHistory);
            var historyColor = m_RGBuilder.ImportTexture(colorRead.texture);
            var historyDepth = m_RGBuilder.ImportTexture(depthRead.texture);
            // One descriptor per history: both sides of the ping-pong pair share it.
            var accumulation = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(AntiAliasingUtilityData.HistoryColorTextureID, colorDescriptor));
            var temporalDepth = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(AntiAliasingUtilityData.HistoryDepthTextureID, depthDescriptor));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TAAAccumulationBuffer, accumulation);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TAADepthBuffer, temporalDepth);
            context.historyCache.MarkProduced(AntiAliasingUtilityData.HistoryColorTextureID);
            context.historyCache.MarkProduced(AntiAliasingUtilityData.HistoryDepthTextureID);
            var sharpened = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, TemporalColorDescriptor(width, height, "TAASharpened", true));
            m_RGBuilder.scoper.PublishTextureRole(InfinityShaderIDs.ResolvedSceneColor, sharpened);
            var input = m_RGBuilder.scoper.QueryTexture(TranslucentFeatureUtility.ResolveTemporalSceneColorId());
            ComputeKernel temporalKernel = kernels.temporalAntiAliasing;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // A debug view or a confidence capture reads the diagnostics kernel's extra outputs.
            bool diagnostics = context.debugView != EDebugView.None
                || (RenderCaptureService.current != null && RenderCaptureService.current.CaptureThisFrame && RenderCaptureService.current.request.includeConfidence);
            RGTextureHandle confidence = default, reprojection = default;
            if (diagnostics)
            {
                var descriptor = TemporalColorDescriptor(width, height, "TAADiagnostics", true);
                descriptor.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
                confidence = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAConfidenceBuffer, descriptor);
                descriptor.name = "TAAReprojection";
                reprojection = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAReprojectionBuffer, descriptor);
                temporalKernel = kernels.temporalAntiAliasingDebug;
            }
#endif
            using (var pass = m_RGBuilder.AddComputePass<AntiAliasingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAntiAliasing)))
            {
                ref var data = ref pass.GetPassData();
                data.shader = temporalKernel.shader;
                data.kernel = temporalKernel.index;
                data.groups = new int2(temporalKernel.GroupsX(width), temporalKernel.GroupsY(height));
                data.parameters = pass.WriteConstants(new TemporalAntiAliasingParameters
                {
                    resolution = new Vector4(width, height, 1f / width, 1f / height),
                    resetBlend = colorRead.valid && depthRead.valid ? 1 : 0,
                });
                data.input = pass.ReadTexture(input);
                data.motion = pass.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                data.metadata = pass.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                data.reactive = pass.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ReactiveMaskBuffer));
                data.historyColor = pass.ReadTexture(historyColor);
                data.historyDepth = pass.ReadTexture(historyDepth);
                data.accumulation = pass.WriteTexture(accumulation);
                data.sharpened = pass.WriteTexture(sharpened);
                data.temporalDepth = pass.WriteTexture(temporalDepth);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                data.diagnostics = diagnostics;
                if (diagnostics)
                {
                    data.confidence = pass.WriteTexture(confidence);
                    data.reprojection = pass.WriteTexture(reprojection);
                }
                data.inputSnapshot = PrepareStageSnapshot(pass, ECaptureBuffer.TAAInput, input);
                data.historyColorSnapshot = PrepareStageSnapshot(pass, ECaptureBuffer.TAAHistoryColor, historyColor);
                data.historyDepthSnapshot = PrepareStageSnapshot(pass, ECaptureBuffer.TAAHistoryDepth, historyDepth);
#endif
                pass.SetExecuteFunc((in AntiAliasingPassData data, in RGComputeEncoder commands) =>
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    data.inputSnapshot.Record(commands, data.input);
                    data.historyColorSnapshot.Record(commands, data.historyColor);
                    data.historyDepthSnapshot.Record(commands, data.historyDepth);
#endif
                    int kernel = data.kernel;
                    var shader = data.shader;
                    commands.SetComputeConstantBufferParam(shader, AntiAliasingUtilityData.CBV_TemporalAntiAliasingParametersID, data.parameters);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.SRV_AliasingColorTextureID, data.input);
                    commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HistoryColorTexture, data.historyColor);
                    commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HistoryDepthTexture, data.historyDepth);
                    commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, data.motion);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.SRV_MotionMetadataID, data.metadata);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.SRV_ReactiveMaskTextureID, data.reactive);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_AccumulateColorTextureID, data.accumulation);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_TemporalDepthTextureID, data.temporalDepth);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_SharpenedColorTextureID, data.sharpened);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (data.diagnostics)
                    {
                        commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_TAADiagnosticsID, data.confidence);
                        commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_TAAReprojectionID, data.reprojection);
                    }
#endif
                    commands.DispatchCompute(shader, kernel, data.groups.x, data.groups.y, 1);
                });
            }
            context.features.MarkProduced(EFrameFeature.TAA);
        }
    }
}
