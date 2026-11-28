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
    // GPU-RECORD: CBV_TemporalFsrParameters Shaders/Pass/Temporal/Compute_TemporalFSR.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalFsrParameters
    {
        public Vector4 displayResolution;
        public Vector4 renderResolution;
        public Vector4 depthParams;
        public Vector4 jitterAndFlags;
        public Matrix4x4 previousFromCurrentClip;
        public int resetHistory;
        public int jitterPhaseCount;
        public float sharpness;
        public float deltaTime;
    }

    /// <summary>
    /// Cross-platform pure HLSL compute backend for AMD FidelityFX Super Resolution 3.1 (upscaler without frame generation) (PRD §10.2).
    /// Supports both NativeAA and Upscaling modes via RGBuilder and CameraHistoryCache.
    /// </summary>
    internal sealed class FsrTemporalBackend : ITemporalExecutionBackend
    {
        ETemporalBackend ITemporalExecutionBackend.Backend => ETemporalBackend.FSR;

        static class FsrShaderIDs
        {
            internal static readonly int CBV_Fsr = Shader.PropertyToID("CBV_TemporalFsrParameters");
            internal static readonly int SRV_FsrPreparedInput = Shader.PropertyToID("SRV_FsrPreparedInput");
            internal static readonly int SRV_FsrLumaHistory = Shader.PropertyToID("SRV_FsrLumaHistory");
            internal static readonly int SRV_FsrLumaCurrent = Shader.PropertyToID("SRV_FsrLumaCurrent");
            internal static readonly int SRV_FsrLockHistory = Shader.PropertyToID("SRV_FsrLockHistory");
            internal static readonly int SRV_FsrHistoryRead = Shader.PropertyToID("SRV_FsrHistoryRead");
            internal static readonly int UAV_FsrPreparedInput = Shader.PropertyToID("UAV_FsrPreparedInput");
            internal static readonly int UAV_FsrLumaHistory = Shader.PropertyToID("UAV_FsrLumaHistory");
            internal static readonly int UAV_FsrLockHistory = Shader.PropertyToID("UAV_FsrLockHistory");

            internal static readonly int HistoryLumaID = Shader.PropertyToID("FsrLumaHistory");
            internal static readonly int HistoryLockID = Shader.PropertyToID("FsrLockHistory");
        }

        static readonly ProfilingSampler s_PrepareSampler = new ProfilingSampler("ComputeTemporalFSR.PrepareInputs");
        static readonly ProfilingSampler s_LumaSampler = new ProfilingSampler("ComputeTemporalFSR.LumaInstability");
        static readonly ProfilingSampler s_AccumulateSampler = new ProfilingSampler("ComputeTemporalFSR.Accumulate");
        static readonly ProfilingSampler s_RcasSampler = new ProfilingSampler("ComputeTemporalFSR.RCAS");

        struct FsrPreparePassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle sceneColorHandle;
            public RGTextureHandle depthHandle;
            public RGTextureHandle motionHandle;
            public RGTextureHandle preparedWriteHandle;
        }

        struct FsrLumaPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle preparedReadHandle;
            public RGTextureHandle lumaHistoryReadHandle;
            public RGTextureHandle lumaHistoryWriteHandle;
        }

        struct FsrAccumulatePassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public LocalKeyword reactiveKeyword;
            public bool hasReactiveMask;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle sceneColorHandle;
            public RGTextureHandle historyColorReadHandle;
            public RGTextureHandle preparedReadHandle;
            public RGTextureHandle lumaHistoryReadHandle;
            public RGTextureHandle lumaCurrentReadHandle;
            public RGTextureHandle lockHistoryReadHandle;
            public RGTextureHandle reactiveMaskHandle;
            public RGTextureHandle lockHistoryWriteHandle;
            public RGTextureHandle historyColorWriteHandle;
        }

        struct FsrRcasPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle historyColorReadHandle;
            public RGTextureHandle displayOutputHandle;
        }

        public void Record(RGBuilder graph, InfinityRenderPipelineResources resources, in TemporalExecutionContext executionContext)
        {
            ComputeKernel prepareKernel = resources.kernels.fsrPrepareInputs;
            ComputeKernel lumaKernel = resources.kernels.fsrLumaInstability;
            ComputeKernel accumulateKernel = resources.kernels.fsrAccumulate;
            ComputeKernel rcasKernel = resources.kernels.fsrRcas;
            TemporalReconstructionUtility.RequireKernel(prepareKernel);
            TemporalReconstructionUtility.RequireKernel(lumaKernel);
            TemporalReconstructionUtility.RequireKernel(accumulateKernel);
            TemporalReconstructionUtility.RequireKernel(rcasKernel);

            ViewContext view = executionContext.view;
            TemporalFrameRequirements requirements = view.temporalRequirements;
            int2 renderSize = requirements.renderSize;
            int2 displaySize = requirements.displaySize;

            HistoryCache historyCache = view.historyCache;
            bool resetRequested = executionContext.resetHistory;

            TextureDescriptor lumaDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "FsrLumaHistory", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Internal);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, FsrShaderIDs.HistoryLumaID, in lumaDesc, resetRequested,
                out RGTextureHandle lumaReadHandle, out RGTextureHandle lumaWriteHandle, out bool lumaValid);

            TextureDescriptor lockDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                displaySize.x, displaySize.y, "FsrLockHistory", GraphicsFormat.R16G16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Display);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, FsrShaderIDs.HistoryLockID, in lockDesc, resetRequested,
                out RGTextureHandle lockReadHandle, out RGTextureHandle lockWriteHandle, out bool lockValid);

            bool resetHistory = resetRequested || !lumaValid || !lockValid;

            Camera camera = view.camera;
            float nearPlane = math.max(camera.nearClipPlane, 1e-4f);
            float farPlane = math.max(camera.farClipPlane, nearPlane + 1e-3f);
            CameraUniform cameraUniform = view.cameraUniform;

            var parameters = new TemporalFsrParameters
            {
                displayResolution = new Vector4(displaySize.x, displaySize.y, 1.0f / displaySize.x, 1.0f / displaySize.y),
                renderResolution = new Vector4(renderSize.x, renderSize.y, 1.0f / renderSize.x, 1.0f / renderSize.y),
                depthParams = new Vector4(nearPlane, farPlane, SystemInfo.usesReversedZBuffer ? 1.0f : 0.0f, camera.orthographic ? 1.0f : 0.0f),
                jitterAndFlags = new Vector4(
                    -cameraUniform.currentJitterUV.x * renderSize.x,
                    -cameraUniform.currentJitterUV.y * renderSize.y,
                    -cameraUniform.previousJitterUV.x * cameraUniform.priorRenderSize.x,
                    -cameraUniform.previousJitterUV.y * cameraUniform.priorRenderSize.y),
                // Compare both depths in the previous camera's projection.
                previousFromCurrentClip = cameraUniform.matrix_LastViewFlipYJitterProj * cameraUniform.matrix_InvViewFlipYJitterProj,
                resetHistory = resetHistory ? 1 : 0,
                jitterPhaseCount = math.max(1, requirements.jitterPhaseCount),
                sharpness = requirements.sharpness,
                deltaTime = math.max(cameraUniform.deltaTime, 1e-4f),
            };

            RGTextureHandle preparedHandle = graph.CreateTexture(
                TemporalReconstructionUtility.CreateScratchDescriptor(
                    renderSize.x, renderSize.y, "FsrPreparedInput", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Internal));

            // Stage 1: Depth Dilation + Motion Dilation + LogLuma.
            {
                RGComputePassHandle<FsrPreparePassData> passHandle = graph.AddComputePass<FsrPreparePassData>(s_PrepareSampler);
                ref FsrPreparePassData passData = ref passHandle.GetPassData();
                passData.shader = prepareKernel.shader;
                passData.kernelIndex = prepareKernel.index;
                passData.groups = new int2(prepareKernel.GroupsX(renderSize.x), prepareKernel.GroupsY(renderSize.y));
                passData.parametersHandle = passHandle.WriteConstants(in parameters);
                passData.sceneColorHandle = passHandle.ReadTexture(executionContext.sceneColorHandle);
                passData.depthHandle = passHandle.ReadTexture(executionContext.depthHandle);
                passData.motionHandle = passHandle.ReadTexture(executionContext.motionHandle);
                passData.preparedWriteHandle = passHandle.WriteTexture(preparedHandle);

                passHandle.SetExecuteFunc((in FsrPreparePassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, FsrShaderIDs.CBV_Fsr, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_SceneColorTexture, data.sceneColorHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_DepthTexture, data.depthHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_MotionTexture, data.motionHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.UAV_FsrPreparedInput, data.preparedWriteHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Stage 2: Temporal Luma Instability Estimation.
            {
                RGComputePassHandle<FsrLumaPassData> passHandle = graph.AddComputePass<FsrLumaPassData>(s_LumaSampler);
                ref FsrLumaPassData passData = ref passHandle.GetPassData();
                passData.shader = lumaKernel.shader;
                passData.kernelIndex = lumaKernel.index;
                passData.groups = new int2(lumaKernel.GroupsX(renderSize.x), lumaKernel.GroupsY(renderSize.y));
                passData.parametersHandle = passHandle.WriteConstants(in parameters);
                passData.preparedReadHandle = passHandle.ReadTexture(preparedHandle);
                passData.lumaHistoryReadHandle = passHandle.ReadTexture(lumaReadHandle);
                passData.lumaHistoryWriteHandle = passHandle.WriteTexture(lumaWriteHandle);

                passHandle.SetExecuteFunc((in FsrLumaPassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, FsrShaderIDs.CBV_Fsr, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.SRV_FsrPreparedInput, data.preparedReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.SRV_FsrLumaHistory, data.lumaHistoryReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.UAV_FsrLumaHistory, data.lumaHistoryWriteHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Stage 3: FSR 3.1 Reprojection, Reactive Weighting & Lanczos2 Accumulation -> unified unsharpened history.
            {
                RGComputePassHandle<FsrAccumulatePassData> passHandle = graph.AddComputePass<FsrAccumulatePassData>(s_AccumulateSampler);
                ref FsrAccumulatePassData passData = ref passHandle.GetPassData();
                passData.shader = accumulateKernel.shader;
                passData.kernelIndex = accumulateKernel.index;
                passData.reactiveKeyword = resources.kernels.fsrReactiveMask;
                passData.hasReactiveMask = executionContext.hasReactiveMask;
                passData.groups = new int2(accumulateKernel.GroupsX(displaySize.x), accumulateKernel.GroupsY(displaySize.y));
                passData.parametersHandle = passHandle.WriteConstants(in parameters);
                passData.sceneColorHandle = passHandle.ReadTexture(executionContext.sceneColorHandle);
                passData.historyColorReadHandle = passHandle.ReadTexture(executionContext.historyColorReadHandle);
                passData.preparedReadHandle = passHandle.ReadTexture(preparedHandle);
                passData.lumaHistoryReadHandle = passHandle.ReadTexture(lumaReadHandle);
                passData.lumaCurrentReadHandle = passHandle.ReadTexture(lumaWriteHandle);
                passData.lockHistoryReadHandle = passHandle.ReadTexture(lockReadHandle);
                passData.reactiveMaskHandle = executionContext.hasReactiveMask ? passHandle.ReadTexture(executionContext.reactiveMaskHandle) : default;
                passData.lockHistoryWriteHandle = passHandle.WriteTexture(lockWriteHandle);
                passData.historyColorWriteHandle = passHandle.WriteTexture(executionContext.historyColorWriteHandle);

                passHandle.SetExecuteFunc((in FsrAccumulatePassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetKeyword(data.shader, data.reactiveKeyword, data.hasReactiveMask);
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, FsrShaderIDs.CBV_Fsr, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_SceneColorTexture, data.sceneColorHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_HistoryColorTexture, data.historyColorReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.SRV_FsrPreparedInput, data.preparedReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.SRV_FsrLumaHistory, data.lumaHistoryReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.SRV_FsrLumaCurrent, data.lumaCurrentReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.SRV_FsrLockHistory, data.lockHistoryReadHandle);
                    if (data.hasReactiveMask)
                    {
                        cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, TemporalShaderIDs.SRV_ReactiveMaskTexture, data.reactiveMaskHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.UAV_FsrLockHistory, data.lockHistoryWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, TemporalShaderIDs.UAV_TemporalHistoryTexture, data.historyColorWriteHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Stage 4: Display-Only RCAS Sharpening -> TemporalOutputBuffer.
            {
                RGComputePassHandle<FsrRcasPassData> passHandle = graph.AddComputePass<FsrRcasPassData>(s_RcasSampler);
                ref FsrRcasPassData passData = ref passHandle.GetPassData();
                passData.shader = rcasKernel.shader;
                passData.kernelIndex = rcasKernel.index;
                passData.groups = new int2(rcasKernel.GroupsX(displaySize.x), rcasKernel.GroupsY(displaySize.y));
                passData.parametersHandle = passHandle.WriteConstants(in parameters);
                passData.historyColorReadHandle = passHandle.ReadTexture(executionContext.historyColorWriteHandle);
                passData.displayOutputHandle = passHandle.WriteTexture(executionContext.outputHandle);

                passHandle.SetExecuteFunc((in FsrRcasPassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, FsrShaderIDs.CBV_Fsr, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, FsrShaderIDs.SRV_FsrHistoryRead, data.historyColorReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, TemporalShaderIDs.UAV_TemporalOutputTexture, data.displayOutputHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RenderCaptureSession session = RenderCaptureService.current;
            if (session != null && session.CaptureThisFrame && session.Captures(ECaptureBuffer.TemporalAccumulation))
            {
                graph.scoper.RegisterTexture(InfinityShaderIDs.TemporalAccumulationBuffer, executionContext.historyColorWriteHandle);
            }
#endif
        }
    }
}
