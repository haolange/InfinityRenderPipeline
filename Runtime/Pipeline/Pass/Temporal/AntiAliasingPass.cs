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
        internal static readonly int SRV_ReactiveMaskTextureID = Shader.PropertyToID("SRV_ReactiveMaskTexture");
        internal static readonly int UAV_AccumulateColorTextureID = Shader.PropertyToID("UAV_AccumulateColorTexture");
        internal static readonly int UAV_SharpenedColorTextureID = Shader.PropertyToID("UAV_SharpenedColorTexture");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal static readonly int UAV_TAADiagnosticsID = Shader.PropertyToID("UAV_TAADiagnostics");
        internal static readonly int UAV_TAAReprojectionID = Shader.PropertyToID("UAV_TAAReprojection");
#endif
        internal static readonly int HistoryColorTextureID = Shader.PropertyToID("HistoryColorTexture");
    }
    internal sealed class AntiAliasingPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly RenderCapturePass m_RenderCapturePass;
#endif

        internal AntiAliasingPass(RGBuilder graph, InfinityRenderPipelineResources resources
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            , RenderCapturePass renderCapturePass
#endif
            )
        {
            m_RGBuilder = graph;
            m_Resources = resources;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_RenderCapturePass = renderCapturePass;
#endif
        }

        struct AntiAliasingPassData
        {
            public RGConstantsHandle parametersHandle;
            public int kernel;
            public int2 groups;
            public ComputeShader shader;
            public RGTextureHandle inputHandle, motionHandle, reactiveHandle, historyColorHandle;
            public RGTextureHandle accumulationHandle, sharpenedHandle;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public bool diagnostics;
            public RGTextureHandle confidenceHandle, reprojectionHandle;
            public RenderCapturePass.CaptureStageSnapshot inputSnapshot, historyColorSnapshot;
#endif
        }
        internal static TextureDescriptor TemporalColorDescriptor(int width, int height, string name, bool writable = false, ETextureSizingDomain domain = ETextureSizingDomain.Internal)
            => TemporalDescriptor(name, width, height, GraphicsFormat.R16G16B16A16_SFloat, writable, domain);
        // Temporal inputs are read at texel centers with point sampling.
        static TextureDescriptor TemporalDescriptor(string name, int width, int height, GraphicsFormat format, bool writable, ETextureSizingDomain domain)
        {
            var descriptor = TextureDescriptor.Create2D(name, width, height, format, domain);
            descriptor.enableRandomWrite = writable;
            descriptor.filterMode = FilterMode.Point;
            return descriptor;
        }

        internal void ComputeAntiAliasing(in ViewContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.TAA);
            var depthSource = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var depthDescription = m_RGBuilder.GetTextureDescriptor(depthSource);
            int width = depthDescription.width, height = depthDescription.height;
            var colorDescriptor = TemporalColorDescriptor(width, height, "HistoryColorTexture", true);
            HistoryRead colorRead = context.historyCache.Read(AntiAliasingUtilityData.HistoryColorTextureID, colorDescriptor, context.resetsHistory);
            var historyColor = m_RGBuilder.ImportTexture(colorRead.textureHandle);
            // One descriptor per history: both sides of the ping-pong pair share it.
            var accumulation = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(AntiAliasingUtilityData.HistoryColorTextureID, colorDescriptor));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TAAAccumulationBuffer, accumulation);
            context.historyCache.MarkProduced(AntiAliasingUtilityData.HistoryColorTextureID);
            var sharpened = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, TemporalColorDescriptor(width, height, "TAASharpened", true));
            var input = m_RGBuilder.scoper.QueryTexture(context.sceneColor.fogged);
            ComputeKernel temporalKernel = m_Resources.kernels.temporalAntiAliasing;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // A debug view or a confidence capture reads the diagnostics kernel's extra outputs.
            bool diagnostics = context.debugView != EDebugView.None
                || (RenderCaptureService.current != null && RenderCaptureService.current.CaptureThisFrame && RenderCaptureService.current.request.includeConfidence);
            RGTextureHandle confidenceHandle = default, reprojection = default;
            if (diagnostics)
            {
                var descriptor = TemporalColorDescriptor(width, height, "TAADiagnostics", true);
                descriptor.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
                confidenceHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAConfidenceBuffer, descriptor);
                descriptor.name = "TAAReprojection";
                reprojection = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAReprojectionBuffer, descriptor);
                temporalKernel = m_Resources.kernels.temporalAntiAliasingDebug;
            }
#endif
            {
                RGComputePassHandle<AntiAliasingPassData> passHandle = m_RGBuilder.AddComputePass<AntiAliasingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAntiAliasing));
                ref AntiAliasingPassData passData = ref passHandle.GetPassData();
                passData.shader = temporalKernel.shader;
                passData.kernel = temporalKernel.index;
                passData.groups = new int2(temporalKernel.GroupsX(width), temporalKernel.GroupsY(height));
                passData.parametersHandle = passHandle.WriteConstants(new TemporalAntiAliasingParameters
                {
                    resolution = new Vector4(width, height, 1f / width, 1f / height),
                    resetBlend = colorRead.valid ? 1 : 0,
                });
                passData.inputHandle = passHandle.ReadTexture(input);
                passData.motionHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                passData.reactiveHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ReactiveMaskBuffer));
                passData.historyColorHandle = passHandle.ReadTexture(historyColor);
                passData.accumulationHandle = passHandle.WriteTexture(accumulation);
                passData.sharpenedHandle = passHandle.WriteTexture(sharpened);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.diagnostics = diagnostics;
                if (diagnostics)
                {
                    passData.confidenceHandle = passHandle.WriteTexture(confidenceHandle);
                    passData.reprojectionHandle = passHandle.WriteTexture(reprojection);
                }
                passData.inputSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.TAAInput, input);
                passData.historyColorSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.TAAHistoryColor, historyColor);
#endif
                passHandle.SetExecuteFunc((in AntiAliasingPassData passData, in RGComputeEncoder commands) =>
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.inputSnapshot.Record(commands, passData.inputHandle);
                    passData.historyColorSnapshot.Record(commands, passData.historyColorHandle);
#endif
                    int kernel = passData.kernel;
                    var shader = passData.shader;
                    commands.SetComputeConstantBufferParam(shader, AntiAliasingUtilityData.CBV_TemporalAntiAliasingParametersID, passData.parametersHandle);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.SRV_AliasingColorTextureID, passData.inputHandle);
                    commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HistoryColorTexture, passData.historyColorHandle);
                    commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionHandle);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.SRV_ReactiveMaskTextureID, passData.reactiveHandle);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_AccumulateColorTextureID, passData.accumulationHandle);
                    commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_SharpenedColorTextureID, passData.sharpenedHandle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (passData.diagnostics)
                    {
                        commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_TAADiagnosticsID, passData.confidenceHandle);
                        commands.SetComputeTextureParam(shader, kernel, AntiAliasingUtilityData.UAV_TAAReprojectionID, passData.reprojectionHandle);
                    }
#endif
                    commands.DispatchCompute(shader, kernel, passData.groups.x, passData.groups.y, 1);
                });
            }
            context.features.MarkProduced(EFrameFeature.TAA);
        }
    }
}
