using System.Runtime.InteropServices;
using InfinityTech.Core;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.Temporal;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_TemporalAntiAliasingParameters Shaders/Pass/Temporal/Compute_TemporalAntiAliasing.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalAntiAliasingParameters
    {
        public Vector4 resolution;
        public Vector4 inputResolution;
        public Vector2 jitterPixels;
        public float resetBlend;
        public float sharpness;
    }

    /// <summary>
    /// Native InfinityRP temporal reconstruction backend (PRD §10.2). Unifies NativeAA (TAA) and
    /// Upscaling (TAAU) under Compute_TemporalAntiAliasing.compute using TAA_UPSCALING.
    /// </summary>
    internal sealed class NativeTemporalBackend : ITemporalExecutionBackend
    {
        internal ETemporalBackend Backend => ETemporalBackend.Native;
        ETemporalBackend ITemporalExecutionBackend.Backend => ETemporalBackend.Native;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly RenderCapturePass m_RenderCapturePass;

        internal NativeTemporalBackend(RenderCapturePass renderCapturePass = null)
        {
            m_RenderCapturePass = renderCapturePass;
        }
#else
        internal NativeTemporalBackend() { }
#endif

        static class NativeShaderIDs
        {
            internal static readonly int CBV_TAA = Shader.PropertyToID("CBV_TemporalAntiAliasingParameters");
            internal static readonly int SRV_AliasingColorTexture = Shader.PropertyToID("SRV_AliasingColorTexture");
            internal static readonly int UAV_AccumulateColorTexture = Shader.PropertyToID("UAV_AccumulateColorTexture");
            internal static readonly int UAV_SharpenedColorTexture = Shader.PropertyToID("UAV_SharpenedColorTexture");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            internal static readonly int UAV_TAADiagnostics = Shader.PropertyToID("UAV_TAADiagnostics");
            internal static readonly int UAV_TAAReprojection = Shader.PropertyToID("UAV_TAAReprojection");
#endif
        }

        static readonly ProfilingSampler s_NativeAASampler = new ProfilingSampler("ComputeTemporalNativeAA");
        static readonly ProfilingSampler s_NativeUpscaleSampler = new ProfilingSampler("ComputeTemporalNativeUpscale");

        struct NativeTemporalPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public LocalKeyword reactiveKeyword;
            public LocalKeyword upscalingKeyword;
            public bool hasReactiveMask;
            public bool isUpscaling;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle colorHandle;
            public RGTextureHandle depthHandle;
            public RGTextureHandle historyReadHandle;
            public RGTextureHandle motionHandle;
            public RGTextureHandle reactiveMaskHandle;
            public RGTextureHandle historyWriteHandle;
            public RGTextureHandle outputHandle;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public bool writeDiagnostics;
            public RGTextureHandle diagnosticsHandle;
            public RGTextureHandle reprojectionHandle;
            public RenderCapturePass.CaptureStageSnapshot inputSnapshot;
            public RenderCapturePass.CaptureStageSnapshot historySnapshot;
#endif
        }

        public void Record(RGBuilder graph, InfinityRenderPipelineResources resources, in TemporalExecutionContext executionContext)
        {
            TemporalFrameRequirements requirements = executionContext.view.temporalRequirements;
            bool isUpscaling = requirements.mode == ETemporalReconstructionMode.Upscaling;
            int2 displaySize = requirements.displaySize;
            int2 renderSize = requirements.renderSize;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RenderCaptureSession session = RenderCaptureService.current;
            bool captureActive = session != null && session.CaptureThisFrame;
            bool writeDiagnostics = captureActive
                && (session.Captures(ECaptureBuffer.TAAReprojection) || session.request.includeConfidence);
            ComputeKernel kernel = writeDiagnostics
                ? resources.kernels.temporalAntiAliasingDebug
                : resources.kernels.temporalAntiAliasing;
#else
            ComputeKernel kernel = resources.kernels.temporalAntiAliasing;
#endif
            TemporalReconstructionUtility.RequireKernel(kernel);

            CameraUniform cameraUniform = executionContext.view.cameraUniform;
            Vector2 jitterPixels = isUpscaling
                ? new Vector2(cameraUniform.currentJitterUV.x * renderSize.x, cameraUniform.currentJitterUV.y * renderSize.y)
                : Vector2.zero;

            {
                RGComputePassHandle<NativeTemporalPassData> passHandle = graph.AddComputePass<NativeTemporalPassData>(
                    isUpscaling ? s_NativeUpscaleSampler : s_NativeAASampler);
                ref NativeTemporalPassData passData = ref passHandle.GetPassData();
                passData.shader = kernel.shader;
                passData.kernelIndex = kernel.index;
                passData.reactiveKeyword = resources.kernels.taaReactiveMask;
                passData.upscalingKeyword = resources.kernels.taaUpscaling;
                passData.hasReactiveMask = executionContext.hasReactiveMask;
                passData.isUpscaling = isUpscaling;
                passData.groups = new int2(kernel.GroupsX(displaySize.x), kernel.GroupsY(displaySize.y));
                passData.parametersHandle = passHandle.WriteConstants(new TemporalAntiAliasingParameters
                {
                    resolution = new Vector4(displaySize.x, displaySize.y, 1.0f / displaySize.x, 1.0f / displaySize.y),
                    inputResolution = new Vector4(renderSize.x, renderSize.y, 1.0f / renderSize.x, 1.0f / renderSize.y),
                    jitterPixels = jitterPixels,
                    resetBlend = executionContext.resetHistory ? 0.0f : 1.0f,
                    sharpness = requirements.sharpness,
                });
                passData.colorHandle = passHandle.ReadTexture(executionContext.sceneColorHandle);
                passData.depthHandle = passHandle.ReadTexture(executionContext.depthHandle);
                passData.historyReadHandle = passHandle.ReadTexture(executionContext.historyColorReadHandle);
                passData.motionHandle = passHandle.ReadTexture(executionContext.motionHandle);
                passData.reactiveMaskHandle = executionContext.hasReactiveMask ? passHandle.ReadTexture(executionContext.reactiveMaskHandle) : default;
                passData.historyWriteHandle = passHandle.WriteTexture(executionContext.historyColorWriteHandle);
                passData.outputHandle = passHandle.WriteTexture(executionContext.outputHandle);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.writeDiagnostics = writeDiagnostics;
                if (captureActive && session.Captures(ECaptureBuffer.TemporalAccumulation))
                {
                    graph.scoper.RegisterTexture(InfinityShaderIDs.TemporalAccumulationBuffer, executionContext.historyColorWriteHandle);
                }
                if (writeDiagnostics)
                {
                    TextureDescriptor confidenceDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                        displaySize.x, displaySize.y, "TAAConfidence", GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point, ETextureSizingDomain.Display);
                    RGTextureHandle diagnosticsHandle = graph.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAConfidenceBuffer, confidenceDesc);
                    TextureDescriptor reprojectionDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                        displaySize.x, displaySize.y, "TAAReprojection", GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point, ETextureSizingDomain.Display);
                    RGTextureHandle reprojectionHandle = graph.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TAAReprojectionBuffer, reprojectionDesc);
                    passData.diagnosticsHandle = passHandle.WriteTexture(diagnosticsHandle);
                    passData.reprojectionHandle = passHandle.WriteTexture(reprojectionHandle);
                }
                if (m_RenderCapturePass != null && captureActive)
                {
                    passData.inputSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.TAAInput, executionContext.sceneColorHandle);
                    passData.historySnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.TAAHistoryColor, executionContext.historyColorReadHandle);
                }
#endif

                passHandle.SetExecuteFunc((in NativeTemporalPassData data, in RGComputeEncoder cmdEncoder) =>
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    data.inputSnapshot.Record(cmdEncoder, data.colorHandle);
                    data.historySnapshot.Record(cmdEncoder, data.historyReadHandle);
#endif
                    cmdEncoder.SetKeyword(data.shader, data.reactiveKeyword, data.hasReactiveMask);
                    cmdEncoder.SetKeyword(data.shader, data.upscalingKeyword, data.isUpscaling);
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, NativeShaderIDs.CBV_TAA, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, NativeShaderIDs.SRV_AliasingColorTexture, data.colorHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_DepthTexture, data.depthHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_HistoryColorTexture, data.historyReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_MotionTexture, data.motionHandle);
                    if (data.hasReactiveMask)
                    {
                        cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, TemporalShaderIDs.SRV_ReactiveMaskTexture, data.reactiveMaskHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, NativeShaderIDs.UAV_AccumulateColorTexture, data.historyWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, NativeShaderIDs.UAV_SharpenedColorTexture, data.outputHandle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (data.writeDiagnostics)
                    {
                        cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, NativeShaderIDs.UAV_TAADiagnostics, data.diagnosticsHandle);
                        cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, NativeShaderIDs.UAV_TAAReprojection, data.reprojectionHandle);
                    }
#endif
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }
        }
    }
}
