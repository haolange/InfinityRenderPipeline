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
        public Vector4 renderSizeAndReciprocal;
        public Vector4 displaySizeAndReciprocal;
        public Vector4 jitterResetAndSharpness;
        public Vector4 depthProjection;
        public Vector4 projectionFlagsAndScale;
    }

    internal sealed class SgsrTemporalBackend : ITemporalExecutionBackend
    {
        ETemporalBackend ITemporalExecutionBackend.Backend => ETemporalBackend.SGSR;

        static readonly int s_Constants = Shader.PropertyToID("CBV_TemporalSgsrParameters");
        static readonly int s_ColorLumaRead = Shader.PropertyToID("SRV_SgsrColorLuma");
        static readonly int s_ColorLumaWrite = Shader.PropertyToID("UAV_SgsrColorLuma");
        static readonly int s_MotionDepthLumaRead = Shader.PropertyToID("SRV_SgsrMotionDepthLuma");
        static readonly int s_MotionDepthLumaWrite = Shader.PropertyToID("UAV_SgsrMotionDepthLuma");
        static readonly int s_DepthClipRead = Shader.PropertyToID("SRV_SgsrDepthClip");
        static readonly int s_DepthClipWrite = Shader.PropertyToID("UAV_SgsrDepthClip");
        static readonly int s_PreviousHistory = Shader.PropertyToID("SRV_SgsrPreviousHistory");
        static readonly int s_History = Shader.PropertyToID("UAV_SgsrHistory");
        static readonly int s_HistoryID = Shader.PropertyToID("SgsrHistory");
        static readonly int s_DisplayInput = Shader.PropertyToID("SRV_SgsrDisplayInput");

        static readonly ProfilingSampler s_ConvertSampler = new ProfilingSampler("ComputeTemporalSGSR.Convert");
        static readonly ProfilingSampler s_ActivateSampler = new ProfilingSampler("ComputeTemporalSGSR.Activate");
        static readonly ProfilingSampler s_UpscaleSampler = new ProfilingSampler("ComputeTemporalSGSR.Upscale");
        static readonly ProfilingSampler s_SharpenSampler = new ProfilingSampler("ComputeTemporalSGSR.Sharpen");

        struct ConvertData
        {
            public ComputeKernel kernel;
            public int2 size;
            public RGConstantsHandle constantsHandle;
            public RGTextureHandle colorHandle, depthHandle, motionHandle, colorLumaHandle, motionDepthLumaHandle;
        }

        struct ActivateData
        {
            public ComputeKernel kernel;
            public int2 size;
            public RGConstantsHandle constantsHandle;
            public RGTextureHandle motionDepthLumaHandle, depthClipHandle;
        }

        struct UpscaleData
        {
            public ComputeKernel kernel;
            public LocalKeyword reactiveKeyword;
            public bool hasReactiveMask;
            public int2 size;
            public RGConstantsHandle constantsHandle;
            public RGTextureHandle colorLumaHandle, motionDepthLumaHandle, depthClipHandle, reactiveMaskHandle;
            public RGTextureHandle previousHistoryHandle, historyHandle, historyColorHandle;
        }

        struct SharpenData
        {
            public ComputeKernel kernel;
            public int2 size;
            public RGConstantsHandle constantsHandle;
            public RGTextureHandle historyHandle, outputHandle;
        }

        public void Record(RGBuilder graph, InfinityRenderPipelineResources resources, in TemporalExecutionContext executionContext)
        {
            ComputeKernel convert = resources.kernels.sgsrConvert;
            ComputeKernel activate = resources.kernels.sgsrActivate;
            ComputeKernel upscale = resources.kernels.sgsrUpscale;
            ComputeKernel sharpen = resources.kernels.sgsrSharpen;
            TemporalReconstructionUtility.RequireKernel(convert);
            TemporalReconstructionUtility.RequireKernel(activate);
            TemporalReconstructionUtility.RequireKernel(upscale);
            TemporalReconstructionUtility.RequireKernel(sharpen);

            ViewContext view = executionContext.view;
            TemporalFrameRequirements requirements = view.temporalRequirements;
            int2 renderSize = requirements.renderSize;
            int2 displaySize = requirements.displaySize;
            TextureDescriptor historyDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                displaySize.x, displaySize.y, "SgsrHistory", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Display);
            TemporalReconstructionUtility.ReadPingPongHistory(
                view.historyCache, graph, s_HistoryID, in historyDesc, executionContext.resetHistory,
                out RGTextureHandle previousHistoryHandle, out RGTextureHandle historyHandle, out bool historyValid);

            TemporalSgsrParameters constants = BuildParameters(renderSize, displaySize,
                view.cameraUniform.currentJitterUV, view.cameraUniform.matrix_FlipYProj,
                view.camera.orthographic, SystemInfo.usesReversedZBuffer, !historyValid, requirements.sharpness);
            RGTextureHandle colorLumaHandle = graph.CreateTexture(TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "SgsrColorLuma", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, ETextureSizingDomain.Internal));
            RGTextureHandle motionDepthLumaHandle = graph.CreateTexture(TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "SgsrMotionDepthLuma", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Internal));
            RGTextureHandle depthClipHandle = graph.CreateTexture(TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "SgsrDepthClip", GraphicsFormat.R16_SFloat, FilterMode.Bilinear, ETextureSizingDomain.Internal));

            {
                RGComputePassHandle<ConvertData> passHandle = graph.AddComputePass<ConvertData>(s_ConvertSampler);
                ref ConvertData data = ref passHandle.GetPassData();
                data.kernel = convert;
                data.size = renderSize;
                data.constantsHandle = passHandle.WriteConstants(in constants);
                data.colorHandle = passHandle.ReadTexture(executionContext.sceneColorHandle);
                data.depthHandle = passHandle.ReadTexture(executionContext.depthHandle);
                data.motionHandle = passHandle.ReadTexture(executionContext.motionHandle);
                data.colorLumaHandle = passHandle.WriteTexture(colorLumaHandle);
                data.motionDepthLumaHandle = passHandle.WriteTexture(motionDepthLumaHandle);
                passHandle.SetExecuteFunc((in ConvertData parameters, in RGComputeEncoder encoder) =>
                {
                    ComputeKernel kernel = parameters.kernel;
                    encoder.SetComputeConstantBufferParam(kernel.shader, s_Constants, parameters.constantsHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, PassShaderIDs.SRV_SceneColorTexture, parameters.colorHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, PassShaderIDs.SRV_DepthTexture, parameters.depthHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, PassShaderIDs.SRV_MotionTexture, parameters.motionHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_ColorLumaWrite, parameters.colorLumaHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_MotionDepthLumaWrite, parameters.motionDepthLumaHandle);
                    encoder.DispatchCompute(kernel.shader, kernel.index, kernel.GroupsX(parameters.size.x), kernel.GroupsY(parameters.size.y), 1);
                });
            }

            {
                RGComputePassHandle<ActivateData> passHandle = graph.AddComputePass<ActivateData>(s_ActivateSampler);
                ref ActivateData data = ref passHandle.GetPassData();
                data.kernel = activate;
                data.size = renderSize;
                data.constantsHandle = passHandle.WriteConstants(in constants);
                data.motionDepthLumaHandle = passHandle.ReadTexture(motionDepthLumaHandle);
                data.depthClipHandle = passHandle.WriteTexture(depthClipHandle);
                passHandle.SetExecuteFunc((in ActivateData parameters, in RGComputeEncoder encoder) =>
                {
                    ComputeKernel kernel = parameters.kernel;
                    encoder.SetComputeConstantBufferParam(kernel.shader, s_Constants, parameters.constantsHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_MotionDepthLumaRead, parameters.motionDepthLumaHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_DepthClipWrite, parameters.depthClipHandle);
                    encoder.DispatchCompute(kernel.shader, kernel.index, kernel.GroupsX(parameters.size.x), kernel.GroupsY(parameters.size.y), 1);
                });
            }

            {
                RGComputePassHandle<UpscaleData> passHandle = graph.AddComputePass<UpscaleData>(s_UpscaleSampler);
                ref UpscaleData data = ref passHandle.GetPassData();
                data.kernel = upscale;
                data.size = displaySize;
                data.constantsHandle = passHandle.WriteConstants(in constants);
                data.reactiveKeyword = resources.kernels.sgsrReactiveMask;
                data.hasReactiveMask = executionContext.hasReactiveMask;
                data.colorLumaHandle = passHandle.ReadTexture(colorLumaHandle);
                data.motionDepthLumaHandle = passHandle.ReadTexture(motionDepthLumaHandle);
                data.depthClipHandle = passHandle.ReadTexture(depthClipHandle);
                data.reactiveMaskHandle = executionContext.hasReactiveMask ? passHandle.ReadTexture(executionContext.reactiveMaskHandle) : default;
                data.previousHistoryHandle = passHandle.ReadTexture(previousHistoryHandle);
                data.historyHandle = passHandle.WriteTexture(historyHandle);
                data.historyColorHandle = passHandle.WriteTexture(executionContext.historyColorWriteHandle);
                passHandle.SetExecuteFunc((in UpscaleData parameters, in RGComputeEncoder encoder) =>
                {
                    ComputeKernel kernel = parameters.kernel;
                    encoder.SetKeyword(kernel.shader, parameters.reactiveKeyword, parameters.hasReactiveMask);
                    encoder.SetComputeConstantBufferParam(kernel.shader, s_Constants, parameters.constantsHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_ColorLumaRead, parameters.colorLumaHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_MotionDepthLumaRead, parameters.motionDepthLumaHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_DepthClipRead, parameters.depthClipHandle);
                    if (parameters.hasReactiveMask)
                    {
                        encoder.SetComputeTextureParam(kernel.shader, kernel.index, TemporalShaderIDs.SRV_ReactiveMaskTexture, parameters.reactiveMaskHandle);
                    }
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_PreviousHistory, parameters.previousHistoryHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_History, parameters.historyHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, TemporalShaderIDs.UAV_TemporalHistoryTexture, parameters.historyColorHandle);
                    encoder.DispatchCompute(kernel.shader, kernel.index, kernel.GroupsX(parameters.size.x), kernel.GroupsY(parameters.size.y), 1);
                });
            }

            {
                RGComputePassHandle<SharpenData> passHandle = graph.AddComputePass<SharpenData>(s_SharpenSampler);
                ref SharpenData data = ref passHandle.GetPassData();
                data.kernel = sharpen;
                data.size = displaySize;
                data.constantsHandle = passHandle.WriteConstants(in constants);
                data.historyHandle = passHandle.ReadTexture(historyHandle);
                data.outputHandle = passHandle.WriteTexture(executionContext.outputHandle);
                passHandle.SetExecuteFunc((in SharpenData parameters, in RGComputeEncoder encoder) =>
                {
                    ComputeKernel kernel = parameters.kernel;
                    encoder.SetComputeConstantBufferParam(kernel.shader, s_Constants, parameters.constantsHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, s_DisplayInput, parameters.historyHandle);
                    encoder.SetComputeTextureParam(kernel.shader, kernel.index, TemporalShaderIDs.UAV_TemporalOutputTexture, parameters.outputHandle);
                    encoder.DispatchCompute(kernel.shader, kernel.index, kernel.GroupsX(parameters.size.x), kernel.GroupsY(parameters.size.y), 1);
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

        internal static TemporalSgsrParameters BuildParameters(int2 renderSize, int2 displaySize,
            Vector2 jitterUV, Matrix4x4 projection, bool orthographic, bool reversedZ, bool reset, float sharpness)
        {
            float areaRatio = (float)displaySize.x / renderSize.x * displaySize.y / renderSize.y;
            float projectedDepthOffset = orthographic ? projection[2, 2] : projection[2, 2] / projection[3, 2];
            return new TemporalSgsrParameters
            {
                renderSizeAndReciprocal = new Vector4(renderSize.x, renderSize.y, 1f / renderSize.x, 1f / renderSize.y),
                displaySizeAndReciprocal = new Vector4(displaySize.x, displaySize.y, 1f / displaySize.x, 1f / displaySize.y),
                jitterResetAndSharpness = new Vector4(jitterUV.x * renderSize.x, jitterUV.y * renderSize.y,
                    reset ? 1f : 0f, TemporalReconstructionUtility.ResolveRCASGain(sharpness)),
                depthProjection = new Vector4(projectedDepthOffset, projection[2, 3], 1f / projection[0, 0], 1f / projection[1, 1]),
                projectionFlagsAndScale = new Vector4(orthographic ? 1f : 0f, reversedZ ? 1f : 0f,
                    Mathf.Min(20f, areaRatio * areaRatio * areaRatio), 0f),
            };
        }
    }
}
