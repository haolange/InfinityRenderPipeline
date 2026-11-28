using System;
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
    // GPU-RECORD: CBV_TemporalStpParameters Shaders/Pass/Temporal/Compute_TemporalSTP.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalStpParameters
    {
        public Vector4 commonConstant;
        public Vector4 setupConstants0;
        public Vector4 setupConstants1;
        public Vector4 setupConstants2;
        public Vector4 setupConstants3;
        public Vector4 setupConstants4;
        public Vector4 setupConstants5;
        public Vector4 setupPerViewConstants0;
        public Vector4 setupPerViewConstants1;
        public Vector4 setupPerViewConstants2;
        public Vector4 setupPerViewConstants3;
        public Vector4 setupPerViewConstants4;
        public Vector4 setupPerViewConstants5;
        public Vector4 setupPerViewConstants6;
        public Vector4 setupPerViewConstants7;
        public Vector4 dilConstants0;
        public Vector4 taaConstants0;
        public Vector4 taaConstants1;
        public Vector4 taaConstants2;
        public Vector4 taaConstants3;
        public Vector4 sharpenConstants;
    }

    /// <summary>
    /// Native RGBuilder backend implementing Unity Spatial-Temporal Post-Processing (STP) (PRD §10.2).
    /// Consumes CameraUniform's 3-frame unjittered projection/view/position history without CoreRP RenderGraph or RTHandle.
    /// </summary>
    internal sealed class StpTemporalBackend : ITemporalExecutionBackend
    {
        ETemporalBackend ITemporalExecutionBackend.Backend => ETemporalBackend.STP;

        static class StpShaderIDs
        {
            internal static readonly int CBV_Stp = Shader.PropertyToID("CBV_TemporalStpParameters");
            internal static readonly int BlueNoiseIn = Shader.PropertyToID("_StpBlueNoiseIn");
            internal static readonly int InputColor = Shader.PropertyToID("_StpInputColor");
            internal static readonly int InputDepth = Shader.PropertyToID("_StpInputDepth");
            internal static readonly int InputMotion = Shader.PropertyToID("_StpInputMotion");
            internal static readonly int ReactiveMask = Shader.PropertyToID("_StpReactiveMask");
            internal static readonly int IntermediateColor = Shader.PropertyToID("_StpIntermediateColor");
            internal static readonly int IntermediateConvergence = Shader.PropertyToID("_StpIntermediateConvergence");
            internal static readonly int IntermediateWeights = Shader.PropertyToID("_StpIntermediateWeights");
            internal static readonly int PriorDepthMotion = Shader.PropertyToID("_StpPriorDepthMotion");
            internal static readonly int DepthMotion = Shader.PropertyToID("_StpDepthMotion");
            internal static readonly int PriorLuma = Shader.PropertyToID("_StpPriorLuma");
            internal static readonly int Luma = Shader.PropertyToID("_StpLuma");
            internal static readonly int PriorConvergence = Shader.PropertyToID("_StpPriorConvergence");
            internal static readonly int Convergence = Shader.PropertyToID("_StpConvergence");
            internal static readonly int PriorFeedback = Shader.PropertyToID("_StpPriorFeedback");
            internal static readonly int DepthMotionSRV = Shader.PropertyToID("_StpDepthMotionSRV");
            internal static readonly int Feedback = Shader.PropertyToID("_StpFeedback");
            internal static readonly int IntermediateColorSRV = Shader.PropertyToID("_StpIntermediateColorSRV");
            internal static readonly int IntermediateConvergenceSRV = Shader.PropertyToID("_StpIntermediateConvergenceSRV");
            internal static readonly int IntermediateWeightsSRV = Shader.PropertyToID("_StpIntermediateWeightsSRV");
            internal static readonly int LumaSRV = Shader.PropertyToID("_StpLumaSRV");
            internal static readonly int ConvergenceSRV = Shader.PropertyToID("_StpConvergenceSRV");
            internal static readonly int HistoryOutput = Shader.PropertyToID("_StpHistoryOutput");
            internal static readonly int HistoryInput = Shader.PropertyToID("_StpHistoryInput");
            internal static readonly int DisplayOutput = Shader.PropertyToID("_StpDisplayOutput");

            internal static readonly int HistoryDepthMotionID = Shader.PropertyToID("StpDepthMotion");
            internal static readonly int HistoryLumaID = Shader.PropertyToID("StpLuma");
            internal static readonly int HistoryConvergenceID = Shader.PropertyToID("StpConvergence");
            internal static readonly int HistoryFeedbackID = Shader.PropertyToID("StpFeedback");
        }

        static readonly ProfilingSampler s_StpSetupSampler = new ProfilingSampler("ComputeTemporalSTP.Setup");
        static readonly ProfilingSampler s_StpPreTaaSampler = new ProfilingSampler("ComputeTemporalSTP.PreTaa");
        static readonly ProfilingSampler s_StpTaaSampler = new ProfilingSampler("ComputeTemporalSTP.Taa");
        static readonly ProfilingSampler s_StpSharpenSampler = new ProfilingSampler("ComputeTemporalSTP.Sharpen");

        struct StpSetupPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public LocalKeyword reactiveKeyword;
            public bool hasReactiveMask;
            public int2 groups;
            public Texture2D blueNoiseTexture;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle inputColorHandle;
            public RGTextureHandle inputDepthHandle;
            public RGTextureHandle inputMotionHandle;
            public RGTextureHandle reactiveMaskHandle;
            public RGTextureHandle priorDepthMotionHandle;
            public RGTextureHandle priorLumaHandle;
            public RGTextureHandle priorConvergenceHandle;
            public RGTextureHandle priorFeedbackHandle;
            public RGTextureHandle depthMotionWriteHandle;
            public RGTextureHandle lumaWriteHandle;
            public RGTextureHandle intermediateColorHandle;
            public RGTextureHandle intermediateConvergenceHandle;
        }

        struct StpPreTaaPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public Texture2D blueNoiseTexture;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle intermediateConvergenceHandle;
            public RGTextureHandle lumaHandle;
            public RGTextureHandle convergenceWriteHandle;
            public RGTextureHandle intermediateWeightsHandle;
        }

        struct StpTaaPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public Texture2D blueNoiseTexture;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle intermediateColorHandle;
            public RGTextureHandle intermediateWeightsHandle;
            public RGTextureHandle convergenceHandle;
            public RGTextureHandle depthMotionHandle;
            public RGTextureHandle priorFeedbackHandle;
            public RGTextureHandle feedbackWriteHandle;
            public RGTextureHandle historyOutputHandle;
        }

        struct StpSharpenPassData
        {
            public ComputeShader shader;
            public int kernelIndex;
            public int2 groups;
            public RGConstantsHandle parametersHandle;
            public RGTextureHandle historyInputHandle;
            public RGTextureHandle displayOutputHandle;
        }

        public void Record(RGBuilder graph, InfinityRenderPipelineResources resources, in TemporalExecutionContext executionContext)
        {
            ComputeKernel setupKernel = resources.kernels.stpSetup;
            ComputeKernel preTaaKernel = resources.kernels.stpPreTaa;
            ComputeKernel taaKernel = resources.kernels.stpTaa;
            ComputeKernel sharpenKernel = resources.kernels.stpSharpen;
            TemporalReconstructionUtility.RequireKernel(setupKernel);
            TemporalReconstructionUtility.RequireKernel(preTaaKernel);
            TemporalReconstructionUtility.RequireKernel(taaKernel);
            TemporalReconstructionUtility.RequireKernel(sharpenKernel);

            Texture2D blueNoiseTexture = resources.textures.blueNoiseOwenScrambledSobol;
            if (blueNoiseTexture == null || !Mathf.IsPowerOfTwo(blueNoiseTexture.width))
            {
                throw new InvalidOperationException("InfinityRP: STP requires power-of-two GlobalSettings RuntimeTextures.blueNoiseOwenScrambledSobol.");
            }

            ViewContext view = executionContext.view;
            TemporalFrameRequirements requirements = view.temporalRequirements;
            int2 renderSize = requirements.renderSize;
            int2 displaySize = requirements.displaySize;
            int2 convergenceSize = new int2((renderSize.x + 3) >> 2, (renderSize.y + 3) >> 2);

            HistoryCache historyCache = view.historyCache;
            bool resetRequested = executionContext.resetHistory;

            TextureDescriptor depthMotionDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "StpDepthMotion", GraphicsFormat.R32_UInt, FilterMode.Point, ETextureSizingDomain.Internal);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, StpShaderIDs.HistoryDepthMotionID, in depthMotionDesc, resetRequested,
                out RGTextureHandle priorDepthMotionHandle, out RGTextureHandle depthMotionWriteHandle, out bool depthMotionValid);

            TextureDescriptor lumaDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                renderSize.x, renderSize.y, "StpLuma", GraphicsFormat.R8G8_UNorm, FilterMode.Bilinear, ETextureSizingDomain.Internal);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, StpShaderIDs.HistoryLumaID, in lumaDesc, resetRequested,
                out RGTextureHandle priorLumaHandle, out RGTextureHandle lumaWriteHandle, out bool lumaValid);

            TextureDescriptor convergenceDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                convergenceSize.x, convergenceSize.y, "StpConvergence", GraphicsFormat.R8_UNorm, FilterMode.Bilinear, ETextureSizingDomain.Internal);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, StpShaderIDs.HistoryConvergenceID, in convergenceDesc, resetRequested,
                out RGTextureHandle priorConvergenceHandle, out RGTextureHandle convergenceWriteHandle, out bool convergenceValid);

            TextureDescriptor feedbackDesc = TemporalReconstructionUtility.CreateScratchDescriptor(
                displaySize.x, displaySize.y, "StpFeedback", GraphicsFormat.A2B10G10R10_UNormPack32, FilterMode.Bilinear, ETextureSizingDomain.Display);
            TemporalReconstructionUtility.ReadPingPongHistory(
                historyCache, graph, StpShaderIDs.HistoryFeedbackID, in feedbackDesc, resetRequested,
                out RGTextureHandle priorFeedbackHandle, out RGTextureHandle feedbackWriteHandle, out bool feedbackValid);

            bool hasValidHistory = !resetRequested
                && depthMotionValid
                && lumaValid
                && convergenceValid
                && feedbackValid;

            TemporalStpParameters stpParameters = BuildParameters(
                view.camera,
                view.cameraUniform,
                in requirements,
                blueNoiseTexture.width,
                hasValidHistory);

            RGTextureHandle intermediateColorHandle = graph.CreateTexture(
                TemporalReconstructionUtility.CreateScratchDescriptor(
                    renderSize.x, renderSize.y, "StpIntermediateColor", GraphicsFormat.A2B10G10R10_UNormPack32, FilterMode.Bilinear, ETextureSizingDomain.Internal));
            RGTextureHandle intermediateConvergenceHandle = graph.CreateTexture(
                TemporalReconstructionUtility.CreateScratchDescriptor(
                    convergenceSize.x, convergenceSize.y, "StpIntermediateConvergence", GraphicsFormat.R8_UNorm, FilterMode.Bilinear, ETextureSizingDomain.Internal));
            RGTextureHandle intermediateWeightsHandle = graph.CreateTexture(
                TemporalReconstructionUtility.CreateScratchDescriptor(
                    renderSize.x, renderSize.y, "StpIntermediateWeights", GraphicsFormat.R8_UNorm, FilterMode.Bilinear, ETextureSizingDomain.Internal));

            // Stage 1: StpSetup (8x8 threads with 64-wide 8x16 lane remap).
            {
                RGComputePassHandle<StpSetupPassData> passHandle = graph.AddComputePass<StpSetupPassData>(s_StpSetupSampler);
                ref StpSetupPassData passData = ref passHandle.GetPassData();
                passData.shader = setupKernel.shader;
                passData.kernelIndex = setupKernel.index;
                passData.reactiveKeyword = resources.kernels.stpReactiveMask;
                passData.hasReactiveMask = executionContext.hasReactiveMask;
                passData.groups = new int2((renderSize.x + 7) >> 3, (renderSize.y + 7) >> 3);
                passData.blueNoiseTexture = blueNoiseTexture;
                passData.parametersHandle = passHandle.WriteConstants(in stpParameters);
                passData.inputColorHandle = passHandle.ReadTexture(executionContext.sceneColorHandle);
                passData.inputDepthHandle = passHandle.ReadTexture(executionContext.depthHandle);
                passData.inputMotionHandle = passHandle.ReadTexture(executionContext.motionHandle);
                passData.reactiveMaskHandle = executionContext.hasReactiveMask ? passHandle.ReadTexture(executionContext.reactiveMaskHandle) : default;
                passData.priorDepthMotionHandle = passHandle.ReadTexture(priorDepthMotionHandle);
                passData.priorLumaHandle = passHandle.ReadTexture(priorLumaHandle);
                passData.priorConvergenceHandle = passHandle.ReadTexture(priorConvergenceHandle);
                passData.priorFeedbackHandle = passHandle.ReadTexture(priorFeedbackHandle);
                passData.depthMotionWriteHandle = passHandle.WriteTexture(depthMotionWriteHandle);
                passData.lumaWriteHandle = passHandle.WriteTexture(lumaWriteHandle);
                passData.intermediateColorHandle = passHandle.WriteTexture(intermediateColorHandle);
                passData.intermediateConvergenceHandle = passHandle.WriteTexture(intermediateConvergenceHandle);

                passHandle.SetExecuteFunc((in StpSetupPassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetKeyword(data.shader, data.reactiveKeyword, data.hasReactiveMask);
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, StpShaderIDs.CBV_Stp, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.BlueNoiseIn, data.blueNoiseTexture, TextureView.Whole(data.blueNoiseTexture));
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.InputColor, data.inputColorHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.InputDepth, data.inputDepthHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.InputMotion, data.inputMotionHandle);
                    if (data.hasReactiveMask)
                    {
                        cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.ReactiveMask, data.reactiveMaskHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.PriorDepthMotion, data.priorDepthMotionHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.PriorLuma, data.priorLumaHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.PriorConvergence, data.priorConvergenceHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.PriorFeedback, data.priorFeedbackHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.DepthMotion, data.depthMotionWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.Luma, data.lumaWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.IntermediateColor, data.intermediateColorHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.IntermediateConvergence, data.intermediateConvergenceHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Stage 2: StpPreTaa (Dilation + Spatial Anti-Aliasing).
            {
                RGComputePassHandle<StpPreTaaPassData> passHandle = graph.AddComputePass<StpPreTaaPassData>(s_StpPreTaaSampler);
                ref StpPreTaaPassData passData = ref passHandle.GetPassData();
                passData.shader = preTaaKernel.shader;
                passData.kernelIndex = preTaaKernel.index;
                passData.groups = new int2((renderSize.x + 7) >> 3, (renderSize.y + 7) >> 3);
                passData.blueNoiseTexture = blueNoiseTexture;
                passData.parametersHandle = passHandle.WriteConstants(in stpParameters);
                passData.intermediateConvergenceHandle = passHandle.ReadTexture(intermediateConvergenceHandle);
                passData.lumaHandle = passHandle.ReadTexture(lumaWriteHandle);
                passData.convergenceWriteHandle = passHandle.WriteTexture(convergenceWriteHandle);
                passData.intermediateWeightsHandle = passHandle.WriteTexture(intermediateWeightsHandle);

                passHandle.SetExecuteFunc((in StpPreTaaPassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, StpShaderIDs.CBV_Stp, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.BlueNoiseIn, data.blueNoiseTexture, TextureView.Whole(data.blueNoiseTexture));
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.IntermediateConvergenceSRV, data.intermediateConvergenceHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.LumaSRV, data.lumaHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.Convergence, data.convergenceWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.IntermediateWeights, data.intermediateWeightsHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Stage 3: StpTaa (Temporal Upscale -> unified unsharpened TemporalHistoryColor).
            {
                RGComputePassHandle<StpTaaPassData> passHandle = graph.AddComputePass<StpTaaPassData>(s_StpTaaSampler);
                ref StpTaaPassData passData = ref passHandle.GetPassData();
                passData.shader = taaKernel.shader;
                passData.kernelIndex = taaKernel.index;
                passData.groups = new int2((displaySize.x + 7) >> 3, (displaySize.y + 7) >> 3);
                passData.blueNoiseTexture = blueNoiseTexture;
                passData.parametersHandle = passHandle.WriteConstants(in stpParameters);
                passData.intermediateColorHandle = passHandle.ReadTexture(intermediateColorHandle);
                passData.intermediateWeightsHandle = passHandle.ReadTexture(intermediateWeightsHandle);
                passData.convergenceHandle = passHandle.ReadTexture(convergenceWriteHandle);
                passData.depthMotionHandle = passHandle.ReadTexture(depthMotionWriteHandle);
                passData.priorFeedbackHandle = passHandle.ReadTexture(priorFeedbackHandle);
                passData.feedbackWriteHandle = passHandle.WriteTexture(feedbackWriteHandle);
                passData.historyOutputHandle = passHandle.WriteTexture(executionContext.historyColorWriteHandle);

                passHandle.SetExecuteFunc((in StpTaaPassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, StpShaderIDs.CBV_Stp, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.BlueNoiseIn, data.blueNoiseTexture, TextureView.Whole(data.blueNoiseTexture));
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.IntermediateColorSRV, data.intermediateColorHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.IntermediateWeightsSRV, data.intermediateWeightsHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.ConvergenceSRV, data.convergenceHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.DepthMotionSRV, data.depthMotionHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.PriorFeedback, data.priorFeedbackHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.Feedback, data.feedbackWriteHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.HistoryOutput, data.historyOutputHandle);
                    cmdEncoder.DispatchCompute(data.shader, data.kernelIndex, data.groups.x, data.groups.y, 1);
                });
            }

            // Stage 4: Display-Only Output Sharpen -> TemporalOutputBuffer (never pollutes TemporalHistoryColor).
            {
                RGComputePassHandle<StpSharpenPassData> passHandle = graph.AddComputePass<StpSharpenPassData>(s_StpSharpenSampler);
                ref StpSharpenPassData passData = ref passHandle.GetPassData();
                passData.shader = sharpenKernel.shader;
                passData.kernelIndex = sharpenKernel.index;
                passData.groups = new int2(sharpenKernel.GroupsX(displaySize.x), sharpenKernel.GroupsY(displaySize.y));
                passData.parametersHandle = passHandle.WriteConstants(in stpParameters);
                passData.historyInputHandle = passHandle.ReadTexture(executionContext.historyColorWriteHandle);
                passData.displayOutputHandle = passHandle.WriteTexture(executionContext.outputHandle);

                passHandle.SetExecuteFunc((in StpSharpenPassData data, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(data.shader, StpShaderIDs.CBV_Stp, data.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.HistoryInput, data.historyInputHandle);
                    cmdEncoder.SetComputeTextureParam(data.shader, data.kernelIndex, StpShaderIDs.DisplayOutput, data.displayOutputHandle);
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

        internal static TemporalStpParameters BuildParameters(
            Camera camera,
            CameraUniform cameraUniform,
            in TemporalFrameRequirements requirements,
            int blueNoiseWidth,
            bool hasValidHistory)
        {
            var constants = new TemporalStpParameters();

            int2 currentImageSize = requirements.renderSize;
            int2 priorImageSize = math.all(cameraUniform.priorRenderSize > 0) ? cameraUniform.priorRenderSize : currentImageSize;
            int2 outputImageSize = requirements.displaySize;
            float nearPlane = math.max(camera.nearClipPlane, 1e-4f);
            float farPlane = math.max(camera.farClipPlane, nearPlane + 1e-3f);

            int packedBlueNoiseWidthMinusOne = (blueNoiseWidth - 1) & 0xFF;
            int packedHasValidHistory = (hasValidHistory ? 1 : 0) << 8;
            int commonPacked = packedHasValidHistory | packedBlueNoiseWidthMinusOne;
            float zBufferParamZ = (farPlane - nearPlane) / (nearPlane * farPlane);
            float zBufferParamW = 1.0f / farPlane;
            // Setup reconstructs orthographic depth linearly instead of using the perspective reciprocal.
            constants.commonConstant = new Vector4(BitConverter.Int32BitsToSingle(commonPacked), zBufferParamZ, zBufferParamW, camera.orthographic ? 1.0f : 0.0f);

            constants.setupConstants0 = new Vector4(
                1.0f / currentImageSize.x,
                1.0f / currentImageSize.y,
                0.5f / currentImageSize.x,
                0.5f / currentImageSize.y);

            Vector2 jitC = -Vector2.Scale(cameraUniform.currentJitterUV, new Vector2(currentImageSize.x, currentImageSize.y));
            Vector2 jitP = -Vector2.Scale(cameraUniform.previousJitterUV, new Vector2(priorImageSize.x, priorImageSize.y));
            constants.setupConstants1 = new Vector4(
                jitC.x / currentImageSize.x - jitP.x / priorImageSize.x,
                jitC.y / currentImageSize.y - jitP.y / priorImageSize.y,
                jitC.x / currentImageSize.x,
                jitC.y / currentImageSize.y);

            float k0 = 1.0f / nearPlane;
            float k1 = 1.0f / Mathf.Log(k0 * farPlane, 2.0f);
            constants.setupConstants2 = new Vector4(outputImageSize.x, outputImageSize.y, k0, k1);

            Vector2 scaleBound = new Vector2(
                2.0f * (priorImageSize.x / (priorImageSize.x + 4.0f)),
                2.0f * (priorImageSize.y / (priorImageSize.y + 4.0f)));
            constants.setupConstants3 = new Vector4(scaleBound.x, scaleBound.y, -0.5f * scaleBound.x, -0.5f * scaleBound.y);

            float motionScale = (hasValidHistory && cameraUniform.lastDeltaTime > 1e-4f)
                ? Mathf.Clamp(cameraUniform.deltaTime / cameraUniform.lastDeltaTime, 0.1f, 10.0f)
                : 1.0f;
            constants.setupConstants4 = new Vector4(Mathf.Log(farPlane / nearPlane, 2.0f), nearPlane, motionScale, 0.0f);

            constants.setupConstants5 = new Vector4(
                currentImageSize.x,
                currentImageSize.y,
                outputImageSize.x / (Mathf.Ceil(outputImageSize.x / 4.0f) * 4.0f),
                outputImageSize.y / (Mathf.Ceil(outputImageSize.y / 4.0f) * 4.0f));

            Matrix4x4 currentProj = cameraUniform.unjitteredProj;
            Matrix4x4 currentView = BuildStpViewMatrix(cameraUniform.matrix_WorldToView, cameraUniform.preparedWorldPos);
            Matrix4x4 lastProj = hasValidHistory ? cameraUniform.lastUnjitteredProj : currentProj;
            Matrix4x4 lastView = hasValidHistory
                ? BuildStpViewMatrix(cameraUniform.lastWorldToView, cameraUniform.lastWorldPos)
                : currentView;
            Matrix4x4 lastLastProj = hasValidHistory && cameraUniform.lastLastUnjitteredProj != default
                ? cameraUniform.lastLastUnjitteredProj
                : lastProj;
            Matrix4x4 lastLastView = hasValidHistory && cameraUniform.lastLastWorldToView != default
                ? BuildStpViewMatrix(cameraUniform.lastLastWorldToView, cameraUniform.lastLastWorldPos)
                : lastView;

            PopulatePerViewConstants(currentProj, lastProj, lastLastProj, currentView, lastView, lastLastView, camera.orthographic, ref constants);

            int2 convergenceSize = (currentImageSize + 3) >> 2;
            constants.dilConstants0 = new Vector4(
                1.0f / convergenceSize.x,
                1.0f / convergenceSize.y,
                BitConverter.Int32BitsToSingle(convergenceSize.x),
                BitConverter.Int32BitsToSingle(convergenceSize.y));

            constants.taaConstants0 = new Vector4(
                (float)currentImageSize.x / outputImageSize.x,
                (float)currentImageSize.y / outputImageSize.y,
                0.5f * currentImageSize.x / outputImageSize.x - jitC.x,
                0.5f * currentImageSize.y / outputImageSize.y - jitC.y);
            constants.taaConstants1 = new Vector4(
                1.0f / currentImageSize.x,
                1.0f / currentImageSize.y,
                1.0f / outputImageSize.x,
                1.0f / outputImageSize.y);
            constants.taaConstants2 = new Vector4(
                0.5f / outputImageSize.x,
                0.5f / outputImageSize.y,
                jitC.x / currentImageSize.x - 0.5f / currentImageSize.x,
                jitC.y / currentImageSize.y + 0.5f / currentImageSize.y);
            constants.taaConstants3 = new Vector4(
                0.5f / currentImageSize.x,
                0.5f / currentImageSize.y,
                outputImageSize.x,
                outputImageSize.y);

            constants.sharpenConstants = new Vector4(outputImageSize.x, outputImageSize.y, requirements.sharpness, 0.0f);
            return constants;
        }

        static Matrix4x4 BuildStpViewMatrix(Matrix4x4 worldToView, Vector3 worldPosition)
        {
            Matrix4x4 view = worldToView;
            view.SetColumn(3, new Vector4(-worldPosition.x, -worldPosition.y, -worldPosition.z, 1.0f));
            return view;
        }

        static Matrix4x4 ExtractRotation(Matrix4x4 input)
        {
            Matrix4x4 output = input;
            output[0, 3] = 0.0f;
            output[1, 3] = 0.0f;
            output[2, 3] = 0.0f;
            output[3, 3] = 1.0f;
            return output;
        }

        static void PopulatePerViewConstants(
            Matrix4x4 currentProj,
            Matrix4x4 lastProj,
            Matrix4x4 lastLastProj,
            Matrix4x4 currentView,
            Matrix4x4 lastView,
            Matrix4x4 lastLastView,
            bool orthographic,
            ref TemporalStpParameters constants)
        {
            Vector4 prjPriABEF = new Vector4(lastProj[0, 0], Mathf.Abs(lastProj[1, 1]),
                orthographic ? lastProj[0, 2] : -lastProj[0, 2], orthographic ? lastProj[1, 2] : -lastProj[1, 2]);
            Vector4 prjPriCDGH = new Vector4(lastProj[2, 2], lastProj[2, 3], lastProj[3, 2], lastProj[3, 3]);

            Vector4 prjCurABEF = new Vector4(currentProj[0, 0], Mathf.Abs(currentProj[1, 1]), currentProj[0, 2], currentProj[1, 2]);
            Vector4 prjCurCDGH = new Vector4(currentProj[2, 2], currentProj[2, 3], currentProj[3, 2], currentProj[3, 3]);

            Matrix4x4 forwardTransform = ExtractRotation(currentView)
                * Matrix4x4.Translate(-currentView.GetColumn(3))
                * Matrix4x4.Translate(lastView.GetColumn(3))
                * ExtractRotation(lastView).transpose;
            Vector4 forIJKL = forwardTransform.GetRow(0);
            Vector4 forMNOP = forwardTransform.GetRow(1);
            Vector4 forQRST = forwardTransform.GetRow(2);

            Vector4 prjPrvABEF = new Vector4(lastLastProj[0, 0], Mathf.Abs(lastLastProj[1, 1]), lastLastProj[0, 2], lastLastProj[1, 2]);
            Vector4 prjPrvCDGH = new Vector4(lastLastProj[2, 2], lastLastProj[2, 3], lastLastProj[3, 2], lastLastProj[3, 3]);

            Matrix4x4 backwardTransform = ExtractRotation(lastLastView)
                * Matrix4x4.Translate(-lastLastView.GetColumn(3))
                * Matrix4x4.Translate(lastView.GetColumn(3))
                * ExtractRotation(lastView).transpose;
            if (orthographic)
            {
                // StpFor reconstructs XY directly and uses positive eye depth for Z.
                Matrix4x4 prior = ExtractRotation(lastView) * Matrix4x4.Translate(lastView.GetColumn(3));
                Matrix4x4 current = ExtractRotation(currentView) * Matrix4x4.Translate(currentView.GetColumn(3));
                Matrix4x4 previous = ExtractRotation(lastLastView) * Matrix4x4.Translate(lastLastView.GetColumn(3));
                Matrix4x4 fromStpView = Matrix4x4.Translate(new Vector3(-lastProj[0, 3] / prjPriABEF.x,
                    -lastProj[1, 3] / prjPriABEF.y, 0)) * Matrix4x4.Scale(new Vector3(1, 1, -1));
                forwardTransform = current * prior.inverse * fromStpView;
                backwardTransform = previous * prior.inverse * fromStpView;
                forIJKL = forwardTransform.GetRow(0);
                forMNOP = forwardTransform.GetRow(1);
                forQRST = forwardTransform.GetRow(2);
            }
            Vector4 bckIJKL = backwardTransform.GetRow(0);
            Vector4 bckMNOP = backwardTransform.GetRow(1);
            Vector4 bckQRST = backwardTransform.GetRow(2);

            constants.setupPerViewConstants0 = new Vector4(
                prjPriCDGH.z / prjPriABEF.x,
                prjPriCDGH.w / prjPriABEF.x,
                prjPriABEF.z / prjPriABEF.x,
                prjPriCDGH.z / prjPriABEF.y);

            constants.setupPerViewConstants1 = new Vector4(
                prjPriCDGH.w / prjPriABEF.y,
                prjPriABEF.w / prjPriABEF.y,
                forIJKL.x * prjCurABEF.x + forQRST.x * prjCurABEF.z,
                forIJKL.y * prjCurABEF.x + forQRST.y * prjCurABEF.z);

            constants.setupPerViewConstants2 = new Vector4(
                forIJKL.z * prjCurABEF.x + forQRST.z * prjCurABEF.z,
                forIJKL.w * prjCurABEF.x + forQRST.w * prjCurABEF.z + (orthographic ? currentProj[0, 3] : 0),
                forMNOP.x * prjCurABEF.y + forQRST.x * prjCurABEF.w,
                forMNOP.y * prjCurABEF.y + forQRST.y * prjCurABEF.w);

            constants.setupPerViewConstants3 = new Vector4(
                forMNOP.z * prjCurABEF.y + forQRST.z * prjCurABEF.w,
                forMNOP.w * prjCurABEF.y + forQRST.w * prjCurABEF.w + (orthographic ? currentProj[1, 3] : 0),
                forQRST.x * prjCurCDGH.z,
                forQRST.y * prjCurCDGH.z);

            constants.setupPerViewConstants4 = new Vector4(
                forQRST.z * prjCurCDGH.z,
                forQRST.w * prjCurCDGH.z + prjCurCDGH.w,
                bckIJKL.x * prjPrvABEF.x + bckQRST.x * prjPrvABEF.z,
                bckIJKL.y * prjPrvABEF.x + bckQRST.y * prjPrvABEF.z);

            constants.setupPerViewConstants5 = new Vector4(
                bckIJKL.z * prjPrvABEF.x + bckQRST.z * prjPrvABEF.z,
                bckIJKL.w * prjPrvABEF.x + bckQRST.w * prjPrvABEF.z + (orthographic ? lastLastProj[0, 3] : 0),
                bckMNOP.x * prjPrvABEF.y + bckQRST.x * prjPrvABEF.w,
                bckMNOP.y * prjPrvABEF.y + bckQRST.y * prjPrvABEF.w);

            constants.setupPerViewConstants6 = new Vector4(
                bckMNOP.z * prjPrvABEF.y + bckQRST.z * prjPrvABEF.w,
                bckMNOP.w * prjPrvABEF.y + bckQRST.w * prjPrvABEF.w + (orthographic ? lastLastProj[1, 3] : 0),
                bckQRST.x * prjPrvCDGH.z,
                bckQRST.y * prjPrvCDGH.z);

            constants.setupPerViewConstants7 = new Vector4(
                bckQRST.z * prjPrvCDGH.z,
                bckQRST.w * prjPrvCDGH.z + prjPrvCDGH.w,
                0.0f,
                0.0f);
        }
    }
}
