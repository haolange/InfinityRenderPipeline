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
    // GPU-RECORD: CBV_TemporalSgsrParameters Shaders/Pass/Temporal/Compute_TemporalSGSR.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalSgsrParameters
    {
        public Vector4 renderSizeAndRcp;
        public Vector4 displaySizeAndRcp;
        public Vector4 jitterAndFov;
        public Vector4 previousJitter;
        public Matrix4x4 previousFromCurrentClip;
        public int resetHistory;
        public int reversedZ;
        public float preExposure;
        public int padding0;
    }

    /// <summary>
    /// Mobile-first pure HLSL compute backend for Qualcomm Snapdragon Game Super Resolution v2 (3-pass CS) (PRD §10.2).
    /// Supports Upscaling mode only, writing both unified unsharpened history and optionally sharpened display output.
    /// </summary>
    internal sealed class SgsrTemporalBackend : ITemporalExecutionBackend
    {
        ETemporalBackend ITemporalExecutionBackend.Backend => ETemporalBackend.SGSR;

        static class SgsrShaderIDs
        {
            internal static readonly int CBV_Sgsr = Shader.PropertyToID("CBV_TemporalSgsrParameters");
            internal static readonly int SRV_SgsrYCoCg = Shader.PropertyToID("SRV_SgsrYCoCg");
            internal static readonly int SRV_SgsrMotionDepthAlpha = Shader.PropertyToID("SRV_SgsrMotionDepthAlpha");
            internal static readonly int SRV_SgsrCurrentDepth = Shader.PropertyToID("SRV_SgsrCurrentDepth");
            internal static readonly int SRV_SgsrPreviousDepth = Shader.PropertyToID("SRV_SgsrPreviousDepth");
            internal static readonly int SRV_SgsrPrevLumaHistory = Shader.PropertyToID("SRV_SgsrPrevLumaHistory");
            internal static readonly int SRV_SgsrMotionDepthClipAlpha = Shader.PropertyToID("SRV_SgsrMotionDepthClipAlpha");
            internal static readonly int SRV_SgsrPrevHistory = Shader.PropertyToID("SRV_SgsrPrevHistory");
            internal static readonly int UAV_SgsrYCoCg = Shader.PropertyToID("UAV_SgsrYCoCg");
            internal static readonly int UAV_SgsrMotionDepthAlpha = Shader.PropertyToID("UAV_SgsrMotionDepthAlpha");
            internal static readonly int UAV_SgsrDepth = Shader.PropertyToID("UAV_SgsrDepth");
            internal static readonly int UAV_SgsrLumaHistory = Shader.PropertyToID("UAV_SgsrLumaHistory");
            internal static readonly int UAV_SgsrMotionDepthClipAlpha = Shader.PropertyToID("UAV_SgsrMotionDepthClipAlpha");
            internal static readonly int UAV_SgsrHistory = Shader.PropertyToID("UAV_SgsrHistory");

            internal static readonly int HistoryLumaID = Shader.PropertyToID("SgsrLumaHistory");
            internal static readonly int HistoryDepthID = Shader.PropertyToID("SgsrDepthHistory");
            internal static readonly int HistoryYCoCgID = Shader.PropertyToID("SgsrHistory");
        }

        static readonly ProfilingSampler s_ConvertSampler = new ProfilingSampler("ComputeTemporalSGSR.Convert");
        static readonly ProfilingSampler s_ActivateSampler = new ProfilingSampler("ComputeTemporalSGSR.Activate");
        static readonly ProfilingSampler s_UpscaleSampler = new ProfilingSampler("ComputeTemporalSGSR.Upscale");

        struct SgsrConvertPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public LocalKeyword reactiveKeyword;
            public bool hasReactiveMask;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle sceneColorHandle;
            public RGTextureHandle depthHandle;
            public RGTextureHandle motionHandle;
            public RGTextureHandle reactiveMaskHandle;
            public RGTextureHandle ycocgWriteHandle;
            public RGTextureHandle mdaWriteHandle;
            public RGTextureHandle depthHistoryWriteHandle;
        }

        struct SgsrActivatePassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle ycocgReadHandle;
            public RGTextureHandle mdaReadHandle;
            public RGTextureHandle currentDepthReadHandle;
            public RGTextureHandle previousDepthReadHandle;
            public RGTextureHandle prevLumaHistoryHandle;
            public RGTextureHandle lumaHistoryWriteHandle;
            public RGTextureHandle clipAlphaWriteHandle;
        }

        struct SgsrUpscalePassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle ycocgReadHandle;
            public RGTextureHandle clipAlphaReadHandle;
            public RGTextureHandle prevHistoryReadHandle;
            public RGTextureHandle historyWriteHandle;
            public RGTextureHandle temporalHistoryWriteHandle;
            public RGTextureHandle displayOutputHandle;
        }

        public void Record(RGBuilder graph, InfinityRenderPipelineResources resources, in TemporalExecutionContext executionContext)
        {
            ComputeKernel convertKernel = resources.kernels.sgsrConvert;
            ComputeKernel activateKernel = resources.kernels.sgsrActivate;
            ComputeKernel upscaleKernel = resources.kernels.sgsrUpscale;
            TemporalReconstructionUtility.RequireKernel(convertKernel);
            TemporalReconstructionUtility.RequireKernel(activateKernel);
            TemporalReconstructionUtility.RequireKernel(upscaleKernel);

            ViewContext view = executionContext.view;
            TemporalFrameRequirements requirements = view.temporalRequirements;
            int2 renderSize = requirements.renderSize;
            int2 displaySize = requirements.displaySize;

            HistoryCache historyCache = view.historyCache;
            bool resetRequested = executionContext.resetHistory;

            TextureDescriptor lumaDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "SgsrLumaHistory", GraphicsFormat.R32_UInt, FilterMode.Point, ETextureSizingDomain.Internal);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, SgsrShaderIDs.HistoryLumaID, in lumaDesc, resetRequested,
                out RGTextureHandle prevLumaHandle, out RGTextureHandle lumaWriteHandle, out bool lumaValid);

            TextureDescriptor sgsrHistoryDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                displaySize.x, displaySize.y, "SgsrHistory", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Display);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, SgsrShaderIDs.HistoryYCoCgID, in sgsrHistoryDesc, resetRequested,
                out RGTextureHandle prevHistoryHandle, out RGTextureHandle historyWriteHandle, out bool historyValid);

            TextureDescriptor depthDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "SgsrDepthHistory", GraphicsFormat.R32_SFloat, FilterMode.Point, ETextureSizingDomain.Internal);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, SgsrShaderIDs.HistoryDepthID, in depthDesc, resetRequested,
                out RGTextureHandle previousDepthHandle, out RGTextureHandle depthWriteHandle, out bool depthValid);

            bool resetHistory = resetRequested || !lumaValid || !historyValid || !depthValid;

            Camera camera = view.camera;
            float tanHalfFovH = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView) * camera.aspect;
            CameraUniform cameraUniform = view.cameraUniform;

            var parameters = new TemporalSgsrParameters
            {
                renderSizeAndRcp = new Vector4(renderSize.x, renderSize.y, 1.0f / renderSize.x, 1.0f / renderSize.y),
                displaySizeAndRcp = new Vector4(displaySize.x, displaySize.y, 1.0f / displaySize.x, 1.0f / displaySize.y),
                jitterAndFov = new Vector4(cameraUniform.currentJitterUV.x * renderSize.x, cameraUniform.currentJitterUV.y * renderSize.y, tanHalfFovH * 2.0f, requirements.sharpness),
                previousJitter = new Vector4(cameraUniform.previousJitterUV.x * cameraUniform.priorRenderSize.x, cameraUniform.previousJitterUV.y * cameraUniform.priorRenderSize.y, 0, 0),
                previousFromCurrentClip = cameraUniform.matrix_LastViewFlipYJitterProj * cameraUniform.matrix_InvViewFlipYJitterProj,
                resetHistory = resetHistory ? 1 : 0,
                reversedZ = SystemInfo.usesReversedZBuffer ? 1 : 0,
                preExposure = 1.0f,
                padding0 = 0,
            };

            RGTextureHandle ycocgHandle = graph.CreateTexture(
                TemporalReconstructionUtility.CreateScratchDescriptor(
                    renderSize.x, renderSize.y, "SgsrYCoCg", GraphicsFormat.R32_UInt, FilterMode.Point, ETextureSizingDomain.Internal));
            RGTextureHandle mdaHandle = graph.CreateTexture(
                TemporalReconstructionUtility.CreateScratchDescriptor(
                    renderSize.x, renderSize.y, "SgsrMotionDepthAlpha", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, ETextureSizingDomain.Internal));
            RGTextureHandle clipAlphaHandle = graph.CreateTexture(
                TemporalReconstructionUtility.CreateScratchDescriptor(
                    renderSize.x, renderSize.y, "SgsrMotionDepthClipAlpha", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Internal));

            // Pass 1: SGSR_Convert (Depth dilation + UV motion + YCoCg 11/11/10 encode).
            {
                RGComputePassHandle<SgsrConvertPassData> passHandle = graph.AddComputePass<SgsrConvertPassData>(s_ConvertSampler);
                ref SgsrConvertPassData passData = ref passHandle.GetPassData();
                passData.shader = convertKernel.shader;
                passData.kernelIndex = convertKernel.index;
                passData.reactiveKeyword = resources.kernels.sgsrReactiveMask;
                passData.hasReactiveMask = executionContext.hasReactiveMask;
                passData.groups = new int2(convertKernel.GroupsX(renderSize.x), convertKernel.GroupsY(renderSize.y));
                passData.parametersHandle = passHandle.WriteConstants(in parameters);
                passData.sceneColorHandle = passHandle.ReadTexture(executionContext.sceneColorHandle);
                passData.depthHandle = passHandle.ReadTexture(executionContext.depthHandle);
                passData.motionHandle = passHandle.ReadTexture(executionContext.motionHandle);
                passData.reactiveMaskHandle = executionContext.hasReactiveMask ? passHandle.ReadTexture(executionContext.reactiveMaskHandle) : default;
                passData.ycocgWriteHandle = passHandle.WriteTexture(ycocgHandle);
                passData.mdaWriteHandle = passHandle.WriteTexture(mdaHandle);
                passData.depthHistoryWriteHandle = passHandle.WriteTexture(depthWriteHandle);

                passHandle.SetExecuteFunc((in SgsrConvertPassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetKeyword(data.shader, data.reactiveKeyword, data.hasReactiveMask);
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, SgsrShaderIDs.CBV_Sgsr, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_SceneColorTexture, data.sceneColorHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_DepthTexture, data.depthHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, PassShaderIDs.SRV_MotionTexture, data.motionHandle);
                    if (data.hasReactiveMask)
                    {
                        cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, TemporalShaderIDs.SRV_ReactiveMaskTexture, data.reactiveMaskHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.UAV_SgsrYCoCg, data.ycocgWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.UAV_SgsrMotionDepthAlpha, data.mdaWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.UAV_SgsrDepth, data.depthHistoryWriteHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Pass 2: SGSR_Activate (Depth disocclusion clipping & luma history tracking).
            {
                RGComputePassHandle<SgsrActivatePassData> passHandle = graph.AddComputePass<SgsrActivatePassData>(s_ActivateSampler);
                ref SgsrActivatePassData passData = ref passHandle.GetPassData();
                passData.shader = activateKernel.shader;
                passData.kernelIndex = activateKernel.index;
                passData.groups = new int2(activateKernel.GroupsX(renderSize.x), activateKernel.GroupsY(renderSize.y));
                passData.parametersHandle = passHandle.WriteConstants(in parameters);
                passData.ycocgReadHandle = passHandle.ReadTexture(ycocgHandle);
                passData.mdaReadHandle = passHandle.ReadTexture(mdaHandle);
                passData.currentDepthReadHandle = passHandle.ReadTexture(depthWriteHandle);
                passData.previousDepthReadHandle = passHandle.ReadTexture(previousDepthHandle);
                passData.prevLumaHistoryHandle = passHandle.ReadTexture(prevLumaHandle);
                passData.lumaHistoryWriteHandle = passHandle.WriteTexture(lumaWriteHandle);
                passData.clipAlphaWriteHandle = passHandle.WriteTexture(clipAlphaHandle);

                passHandle.SetExecuteFunc((in SgsrActivatePassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, SgsrShaderIDs.CBV_Sgsr, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrYCoCg, data.ycocgReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrMotionDepthAlpha, data.mdaReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrCurrentDepth, data.currentDepthReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrPreviousDepth, data.previousDepthReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrPrevLumaHistory, data.prevLumaHistoryHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.UAV_SgsrLumaHistory, data.lumaHistoryWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.UAV_SgsrMotionDepthClipAlpha, data.clipAlphaWriteHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Pass 3: SGSR_Upscale (Directional Lanczos Upsample + Temporal Rectification + Unified Unsharpened History).
            {
                RGComputePassHandle<SgsrUpscalePassData> passHandle = graph.AddComputePass<SgsrUpscalePassData>(s_UpscaleSampler);
                ref SgsrUpscalePassData passData = ref passHandle.GetPassData();
                passData.shader = upscaleKernel.shader;
                passData.kernelIndex = upscaleKernel.index;
                passData.groups = new int2(upscaleKernel.GroupsX(displaySize.x), upscaleKernel.GroupsY(displaySize.y));
                passData.parametersHandle = passHandle.WriteConstants(in parameters);
                passData.ycocgReadHandle = passHandle.ReadTexture(ycocgHandle);
                passData.clipAlphaReadHandle = passHandle.ReadTexture(clipAlphaHandle);
                passData.prevHistoryReadHandle = passHandle.ReadTexture(prevHistoryHandle);
                passData.historyWriteHandle = passHandle.WriteTexture(historyWriteHandle);
                passData.temporalHistoryWriteHandle = passHandle.WriteTexture(executionContext.historyColorWriteHandle);
                passData.displayOutputHandle = passHandle.WriteTexture(executionContext.outputHandle);

                passHandle.SetExecuteFunc((in SgsrUpscalePassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, SgsrShaderIDs.CBV_Sgsr, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrYCoCg, data.ycocgReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrMotionDepthClipAlpha, data.clipAlphaReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.SRV_SgsrPrevHistory, data.prevHistoryReadHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, SgsrShaderIDs.UAV_SgsrHistory, data.historyWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, TemporalShaderIDs.UAV_TemporalHistoryTexture, data.temporalHistoryWriteHandle);
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
