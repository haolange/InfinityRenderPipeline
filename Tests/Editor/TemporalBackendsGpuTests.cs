using System;
using System.Collections.Generic;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Temporal;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public sealed class TemporalBackendsGpuTests
    {
        [Test]
        public void CapabilityFallbackChain_FollowsFsrThenStpThenNative()
        {
            var stpOnly = new TemporalDeviceCapabilities(nativeSupported: true, stpSupported: true, fsrSupported: false, sgsrSupported: false);
            var nativeOnly = new TemporalDeviceCapabilities(nativeSupported: true, stpSupported: false, fsrSupported: false, sgsrSupported: false);
            var settings = new TemporalSettings(ETemporalBackend.DLSS, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality, 0.35f);
            var input = new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, in settings, false);

            TemporalFrameRequirements reqAll = new TemporalReconstructionSystem(TemporalDeviceCapabilities.AllCrossPlatformSupported).PlanFrame(in input, out _);
            TemporalFrameRequirements reqStp = new TemporalReconstructionSystem(stpOnly).PlanFrame(in input, out _);
            TemporalFrameRequirements reqNative = new TemporalReconstructionSystem(nativeOnly).PlanFrame(in input, out _);

            Assert.AreEqual(ETemporalBackend.FSR, reqAll.activeBackend);
            Assert.AreEqual(ETemporalBackend.STP, reqStp.activeBackend);
            Assert.AreEqual(ETemporalBackend.Native, reqNative.activeBackend);
        }

        [Test]
        public void SgsrModeLock_DoesNotDeadlockQualityPreset()
        {
            var sgsrSystem = new TemporalReconstructionSystem(TemporalDeviceCapabilities.AllCrossPlatformSupported);
            var authoredNativeAa = new TemporalSettings(ETemporalBackend.SGSR, ETemporalReconstructionMode.NativeAA, ETemporalQuality.Balanced, 0.25f);
            TemporalFrameRequirements req = sgsrSystem.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, in authoredNativeAa, false),
                out TemporalBackendCapability cap);

            Assert.IsTrue(cap.HasSingleSupportedMode(out ETemporalReconstructionMode onlyMode));
            Assert.AreEqual(ETemporalReconstructionMode.Upscaling, onlyMode);
            Assert.AreEqual(ETemporalReconstructionMode.Upscaling, req.mode);
            Assert.Less(req.renderScale, 1.0f);

            var resolverSettings = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            try
            {
                InfinityRenderPipelineAsset.Settings baseSettings = resolverSettings.ResolveSettings();
                EffectivePipelineConfiguration cfg = PipelineConfigurationResolver.Evaluate(in baseSettings, DeviceCapabilitySnapshot.AllSupported, sgsrSystem);
                Assert.IsNotNull(cfg.temporalModeState);
                // Verify SGSR asset directly via SerializedObject so authored NativeAA + Upscaling quality stays unlocked.
                var so = new SerializedObject(resolverSettings);
                so.FindProperty("m_TemporalBackend").enumValueIndex = (int)ETemporalBackend.SGSR;
                so.FindProperty("m_TemporalMode").enumValueIndex = (int)ETemporalReconstructionMode.NativeAA;
                so.FindProperty("m_TemporalQuality").enumValueIndex = (int)ETemporalQuality.Quality;
                so.ApplyModifiedPropertiesWithoutUndo();

                InfinityRenderPipelineAsset.Settings sgsrAssetSettings = resolverSettings.ResolveSettings();
                EffectivePipelineConfiguration sgsrCfg = PipelineConfigurationResolver.Evaluate(in sgsrAssetSettings, DeviceCapabilitySnapshot.AllSupported, sgsrSystem);
                Assert.AreEqual(EConfigStatus.Required, sgsrCfg.temporalModeState.status);
                Assert.AreEqual(ETemporalReconstructionMode.Upscaling, sgsrCfg.effectiveTemporalMode);
                Assert.AreEqual(EConfigStatus.Supported, sgsrCfg.temporalQualityState.status);
                Assert.Less(sgsrCfg.effectiveTemporalMipBias, 0.0f);
            }
            finally
            {
                Object.DestroyImmediate(resolverSettings);
            }
        }

        [Test]
        public void CameraForceNativeAA_RoutesUpscalingOnlyBackendToNativeOneX()
        {
            var system = new TemporalReconstructionSystem(TemporalDeviceCapabilities.AllCrossPlatformSupported);
            var sgsrSettings = new TemporalSettings(ETemporalBackend.SGSR, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Performance, 0.35f);
            TemporalFrameRequirements req = system.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.ForceNativeAA, in sgsrSettings, false),
                out _);

            Assert.AreEqual(ETemporalBackend.Native, req.activeBackend);
            Assert.AreEqual(ETemporalReconstructionMode.NativeAA, req.mode);
            Assert.AreEqual(1.0f, req.renderScale);
            Assert.AreEqual(0.0f, req.mipBias);
            Assert.AreEqual(new int2(1920, 1080), req.renderSize);
        }

        [Test]
        public void ViewUniform_PublishesExactTemporalMipBias()
        {
            var manager = VolumeManager.instance;
            bool ownsInit = !manager.isInitialized;
            if (ownsInit) manager.Initialize(null, null);
            var pool = new ResourcePool();
            CameraFrameState frameState = null;
            var go = new GameObject("MipBiasCam");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                frameState = new CameraFrameState(77, pool);
                var req = new TemporalFrameRequirements(
                    ETemporalBackend.FSR, ETemporalBackend.FSR, ETemporalProvider.AMD,
                    ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality,
                    new int2(1920, 1080), new int2(1280, 720), 1.0f / 1.5f, -0.5849625f,
                    0.35f, 12, 1.0f, true, true, true, true, true, false, false);
                var cmd = new CommandBuffer();
                frameState.cameraUniform.UpdateCurrFrameData(camera, req.renderSize, EHistoryReset.None, in req, 1.0f / 60.0f);
                frameState.cameraUniform.SetUniformData(cmd, req.renderSize, frameState.viewUniform);
                cmd.Release();

                // FSR mip bias formula: log2(renderScale) - 1.0f.
                TemporalFrameRequirements planned = new TemporalReconstructionSystem(TemporalDeviceCapabilities.AllCrossPlatformSupported).PlanFrame(
                    new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline,
                        new TemporalSettings(ETemporalBackend.FSR, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality, 0.35f), false),
                    out _);
                Assert.That(planned.mipBias, Is.EqualTo(Mathf.Log(planned.renderScale, 2.0f) - 1.0f).Within(0.01f));
            }
            finally
            {
                frameState?.Dispose();
                pool.Dispose();
                Object.DestroyImmediate(go);
                if (ownsInit) manager.Deinitialize();
            }
        }

        [Test]
        public void StpParameters_UseActualJitterAcrossHistoryReset()
        {
            var go = new GameObject("StpJitterCamera");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                var settings = new TemporalSettings(ETemporalBackend.STP, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality, 0.35f);
                var system = new TemporalReconstructionSystem(TemporalDeviceCapabilities.AllCrossPlatformSupported);
                TemporalFrameRequirements requirements = system.PlanFrame(
                    new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, in settings, false), out _);
                var uniform = new CameraUniform();
                uniform.UpdateCurrFrameData(camera, requirements.renderSize, EHistoryReset.NewCamera, in requirements, 1f / 60f);
                TemporalStpParameters reset = StpTemporalBackend.BuildParameters(camera, uniform, in requirements, 16, false);
                Assert.That(reset.setupConstants1, Is.EqualTo(Vector4.zero));
                uniform.Commit();
                uniform.UpdateCurrFrameData(camera, requirements.renderSize, EHistoryReset.None, in requirements, 1f / 60f);
                TemporalStpParameters next = StpTemporalBackend.BuildParameters(camera, uniform, in requirements, 16, true);
                Assert.That(next.setupConstants1.x, Is.EqualTo(-uniform.currentJitterUV.x).Within(1e-7f));
                Assert.That(next.setupConstants1.y, Is.EqualTo(-uniform.currentJitterUV.y).Within(1e-7f));
                Assert.That(next.setupConstants1.z, Is.EqualTo(-uniform.currentJitterUV.x).Within(1e-7f));
                Assert.That(next.setupConstants1.w, Is.EqualTo(-uniform.currentJitterUV.y).Within(1e-7f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FsrSharpening_PreservesFlatHdrAtMaximumSharpness()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");
            const int size = 8;
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalFSR.compute"));
            var owned = new List<Object> { shader };
            var cb = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalFsrParameters>(), ComputeBufferType.Constant);
            try
            {
                Color expected = new Color(2.0f, 0.6f, 0.2f, 1);
                Texture2D input = CreateTex(size, size, (_, _) => expected, owned);
                RenderTexture output = CreateRt(size, size, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, owned);
                var parameters = new TemporalFsrParameters
                {
                    displayResolution = new Vector4(size, size, 1f / size, 1f / size),
                    sharpness = 1,
                };
                cb.SetData(new[] { parameters });
                int kernel = shader.FindKernel("FSR_RCAS");
                shader.SetConstantBuffer("CBV_TemporalFsrParameters", cb, 0, cb.stride);
                shader.SetTexture(kernel, "SRV_FsrHistoryRead", input);
                shader.SetTexture(kernel, "UAV_TemporalOutputTexture", output);
                shader.Dispatch(kernel, 1, 1, 1);
                foreach (Color pixel in ReadRt(output))
                    for (int c = 0; c < 3; ++c)
                    {
                        Assert.IsTrue(float.IsFinite(pixel[c]));
                        Assert.That(pixel[c], Is.EqualTo(expected[c]).Within(0.01f));
                    }
                Texture2D isolated = CreateTex(size, size, (x, y) => x == 4 && y == 4 ? new Color(10, 2, 1, 1) : Color.black, owned);
                shader.SetTexture(kernel, "SRV_FsrHistoryRead", isolated);
                shader.Dispatch(kernel, 1, 1, 1);
                Color highlight = ReadRt(output)[size * 4 + 4];
                Assert.IsTrue(float.IsFinite(highlight.r) && float.IsFinite(highlight.g) && float.IsFinite(highlight.b));
                Assert.Greater(highlight.r, 1);
                Assert.That(highlight.g / highlight.r, Is.EqualTo(0.2f).Within(0.01f));
                Assert.That(highlight.b / highlight.r, Is.EqualTo(0.1f).Within(0.01f));
            }
            finally
            {
                cb.Release();
                CleanupOwned(owned);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void FsrAccumulation_UsesPreviousDepthForDisocclusion(bool reversedZ, bool orthographic)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");
            const int size = 8;
            const float near = 0.1f, far = 100f;
            float DeviceDepth(float distance)
            {
                float reversed = orthographic ? (far - distance) / (far - near)
                    : (near * far / distance - near) / (far - near);
                return reversedZ ? reversed : 1f - reversed;
            }
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalFSR.compute"));
            var owned = new List<Object> { shader };
            var cb = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalFsrParameters>(), ComputeBufferType.Constant);
            try
            {
                Texture2D color = CreateTex(size, size, (_, _) => new Color(0.3f, 0.2f, 0.1f, 1), owned);
                Texture2D prepared = CreateTex(size, size, (_, _) => new Color(0, 0, DeviceDepth(10), 0), owned);
                Texture2D currentLuma = CreateTex(size, size, (_, _) => new Color(0, 0, DeviceDepth(10), 0), owned);
                Texture2D sameDepth = CreateTex(size, size, (_, _) => new Color(0, 0, DeviceDepth(10), 0), owned);
                Texture2D nearDepth = CreateTex(size, size, (_, _) => new Color(0, 0, DeviceDepth(1), 0), owned);
                Texture2D translatedDepth = CreateTex(size, size, (_, _) => new Color(0, 0, DeviceDepth(5), 0), owned);
                Texture2D invalidMotion = CreateTex(size, size, (_, _) => new Color(float.NaN, float.PositiveInfinity, DeviceDepth(10), 0), owned);
                Texture2D invalidHistory = CreateTex(size, size, (_, _) => new Color(float.NaN, float.NaN, float.NaN, 1), owned);
                Texture2D invalidLock = CreateTex(size, size, (_, _) => new Color(float.NaN, float.NaN, 0, 0), owned);
                Texture2D gradient = CreateTex(size, size, (x, _) => new Color(0.1f * x, 0.1f * x, 0.1f * x, 1), owned);
                Texture2D lockHistory = CreateTex(size, size, (_, _) => new Color(4, 0, 0, 0), owned);
                RenderTexture output = CreateRt(size, size, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, owned);
                RenderTexture lockOutput = CreateRt(size, size, GraphicsFormat.R16G16_SFloat, FilterMode.Point, owned);
                var parameters = new TemporalFsrParameters
                {
                    displayResolution = new Vector4(size, size, 1f / size, 1f / size),
                    renderResolution = new Vector4(size, size, 1f / size, 1f / size),
                    depthParams = new Vector4(near, far, reversedZ ? 1 : 0, orthographic ? 1 : 0),
                    previousFromCurrentClip = Matrix4x4.identity,
                    jitterPhaseCount = 8,
                };
                cb.SetData(new[] { parameters });
                int kernel = shader.FindKernel("FSR_Accumulate");
                shader.SetConstantBuffer("CBV_TemporalFsrParameters", cb, 0, cb.stride);
                foreach (string name in new[] { "SRV_SceneColorTexture", "SRV_HistoryColorTexture", "SRV_FsrPreparedInput", "SRV_FsrLumaHistory", "SRV_FsrLumaCurrent", "SRV_FsrLockHistory" })
                    shader.SetVector(name + "_InfinityView", new Vector4(size, size, size, size));
                shader.SetTexture(kernel, "SRV_SceneColorTexture", color);
                shader.SetTexture(kernel, "SRV_HistoryColorTexture", color);
                shader.SetTexture(kernel, "SRV_FsrPreparedInput", prepared);
                shader.SetTexture(kernel, "SRV_FsrLumaCurrent", currentLuma);
                shader.SetTexture(kernel, "SRV_FsrLockHistory", lockHistory);
                shader.SetTexture(kernel, "UAV_FsrLockHistory", lockOutput);
                shader.SetTexture(kernel, "UAV_TemporalHistoryTexture", output);
                shader.SetTexture(kernel, "SRV_FsrLumaHistory", sameDepth);
                shader.Dispatch(kernel, 1, 1, 1);
                float retained = ReadRt(lockOutput)[size * 4 + 4].r;
                shader.SetTexture(kernel, "SRV_FsrLumaHistory", nearDepth);
                shader.Dispatch(kernel, 1, 1, 1);
                float rejected = ReadRt(lockOutput)[size * 4 + 4].r;
                Assert.That(retained, Is.GreaterThan(4.5f));
                Assert.That(rejected, Is.LessThan(1.1f));
                shader.SetTexture(kernel, "SRV_HistoryColorTexture", invalidHistory);
                shader.SetTexture(kernel, "SRV_FsrLockHistory", invalidLock);
                shader.Dispatch(kernel, 1, 1, 1);
                AssertFiniteNonBlack(ReadRt(output), size * size);
                shader.SetTexture(kernel, "SRV_HistoryColorTexture", color);
                shader.SetTexture(kernel, "SRV_FsrLockHistory", lockHistory);
                parameters.previousFromCurrentClip = Matrix4x4.identity;
                parameters.previousFromCurrentClip.m23 = DeviceDepth(5) - DeviceDepth(10);
                cb.SetData(new[] { parameters });
                shader.SetTexture(kernel, "SRV_FsrLumaHistory", translatedDepth);
                shader.Dispatch(kernel, 1, 1, 1);
                Assert.That(ReadRt(lockOutput)[size * 4 + 4].r, Is.GreaterThan(4.5f), "The previous clip transform must preserve matching depth.");
                parameters.previousFromCurrentClip.m23 = 2f;
                cb.SetData(new[] { parameters });
                shader.Dispatch(kernel, 1, 1, 1);
                Assert.That(ReadRt(lockOutput)[size * 4 + 4].r, Is.LessThan(1.1f), "A point outside the previous depth range must reject history.");
                parameters.previousFromCurrentClip = Matrix4x4.identity;
                parameters.resetHistory = 1;
                parameters.jitterAndFlags = new Vector4(0.25f, 0, 0, 0);
                cb.SetData(new[] { parameters });
                shader.SetTexture(kernel, "SRV_SceneColorTexture", gradient);
                shader.Dispatch(kernel, 1, 1, 1);
                float positivePhase = ReadRt(output)[size * 4 + 4].r;
                parameters.jitterAndFlags.x = -0.25f;
                cb.SetData(new[] { parameters });
                shader.Dispatch(kernel, 1, 1, 1);
                float negativePhase = ReadRt(output)[size * 4 + 4].r;
                Assert.That(positivePhase, Is.LessThan(negativePhase - 0.002f), "Current reconstruction must respond to the jitter phase.");
                parameters.resetHistory = 0;
                parameters.jitterAndFlags = Vector4.zero;
                cb.SetData(new[] { parameters });
                shader.SetTexture(kernel, "SRV_SceneColorTexture", color);
                shader.SetTexture(kernel, "SRV_FsrPreparedInput", invalidMotion);
                shader.SetTexture(kernel, "SRV_HistoryColorTexture", invalidHistory);
                shader.Dispatch(kernel, 1, 1, 1);
                AssertFiniteNonBlack(ReadRt(output), size * size);
            }
            finally
            {
                cb.Release();
                CleanupOwned(owned);
            }
        }

        [Test]
        public void ComputeTemporalFSR_TwoFrameDispatchProducesFiniteUpscaledHdrOutput()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");
            }

            const int rw = 16, rh = 16, dw = 24, dh = 24;
            ComputeShader shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalFSR.compute"));
            var owned = new List<Object> { shader };
            try
            {
                Texture2D sceneColor = CreateTex(rw, rh, (x, y) => new Color(0.2f + 0.02f * x, 0.15f + 0.01f * y, 0.08f, 1.0f), owned);
                Texture2D depth = CreateTex(rw, rh, (_, _) => new Color(0.5f, 0, 0, 0), owned);
                Texture2D motion = CreateTex(rw, rh, (_, _) => Color.clear, owned);

                RenderTexture prepared = CreateRt(rw, rh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture lumaA = CreateRt(rw, rh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture lumaB = CreateRt(rw, rh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture lockA = CreateRt(dw, dh, GraphicsFormat.R16G16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture lockB = CreateRt(dw, dh, GraphicsFormat.R16G16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture histA = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture histB = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture output = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);

                int kPrep = shader.FindKernel("FSR_PrepareInputs");
                int kLuma = shader.FindKernel("FSR_LumaInstability");
                int kAcc = shader.FindKernel("FSR_Accumulate");
                int kRcas = shader.FindKernel("FSR_RCAS");

                var cbv = new TemporalFsrParameters
                {
                    displayResolution = new Vector4(dw, dh, 1.0f / dw, 1.0f / dh),
                    renderResolution = new Vector4(rw, rh, 1.0f / rw, 1.0f / rh),
                    depthParams = new Vector4(0.1f, 100.0f, 1.0f, 0.0f),
                    jitterAndFlags = Vector4.zero,
                    previousFromCurrentClip = Matrix4x4.identity,
                    resetHistory = 1,
                    jitterPhaseCount = 12,
                    sharpness = 0.35f,
                    deltaTime = 1.0f / 60.0f,
                };
                var cb = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalFsrParameters>(), ComputeBufferType.Constant);
                try
                {
                    for (int frame = 0; frame < 2; ++frame)
                    {
                        cbv.resetHistory = frame == 0 ? 1 : 0;
                        cb.SetData(new[] { cbv });
                        RenderTexture prevLuma = frame == 0 ? lumaA : lumaB;
                        RenderTexture nextLuma = frame == 0 ? lumaB : lumaA;
                        RenderTexture prevLock = frame == 0 ? lockA : lockB;
                        RenderTexture nextLock = frame == 0 ? lockB : lockA;
                        RenderTexture prevHist = frame == 0 ? histA : histB;
                        RenderTexture nextHist = frame == 0 ? histB : histA;

                        shader.SetConstantBuffer("CBV_TemporalFsrParameters", cb, 0, cb.stride);

                        shader.SetTexture(kPrep, "SRV_SceneColorTexture", sceneColor);
                        shader.SetTexture(kPrep, "SRV_DepthTexture", depth);
                        shader.SetTexture(kPrep, "SRV_MotionTexture", motion);
                        shader.SetTexture(kPrep, "UAV_FsrPreparedInput", prepared);
                        shader.Dispatch(kPrep, (rw + 7) / 8, (rh + 7) / 8, 1);

                        shader.SetVector("SRV_FsrPreparedInput_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetVector("SRV_FsrLumaHistory_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetVector("SRV_FsrLumaCurrent_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetTexture(kLuma, "SRV_FsrPreparedInput", prepared);
                        shader.SetTexture(kLuma, "SRV_FsrLumaHistory", prevLuma);
                        shader.SetTexture(kLuma, "UAV_FsrLumaHistory", nextLuma);
                        shader.Dispatch(kLuma, (rw + 7) / 8, (rh + 7) / 8, 1);

                        shader.SetVector("SRV_SceneColorTexture_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetVector("SRV_HistoryColorTexture_InfinityView", new Vector4(dw, dh, dw, dh));
                        shader.SetVector("SRV_FsrLockHistory_InfinityView", new Vector4(dw, dh, dw, dh));
                        shader.SetTexture(kAcc, "SRV_SceneColorTexture", sceneColor);
                        shader.SetTexture(kAcc, "SRV_HistoryColorTexture", prevHist);
                        shader.SetTexture(kAcc, "SRV_FsrPreparedInput", prepared);
                        shader.SetTexture(kAcc, "SRV_FsrLumaHistory", prevLuma);
                        shader.SetTexture(kAcc, "SRV_FsrLumaCurrent", nextLuma);
                        shader.SetTexture(kAcc, "SRV_FsrLockHistory", prevLock);
                        shader.SetTexture(kAcc, "UAV_FsrLockHistory", nextLock);
                        shader.SetTexture(kAcc, "UAV_TemporalHistoryTexture", nextHist);
                        shader.Dispatch(kAcc, (dw + 7) / 8, (dh + 7) / 8, 1);

                        shader.SetTexture(kRcas, "SRV_FsrHistoryRead", nextHist);
                        shader.SetTexture(kRcas, "UAV_TemporalOutputTexture", output);
                        shader.Dispatch(kRcas, (dw + 7) / 8, (dh + 7) / 8, 1);
                    }

                    AssertFiniteNonBlack(ReadRt(output), dw * dh);
                }
                finally
                {
                    cb.Release();
                }
            }
            finally
            {
                CleanupOwned(owned);
            }
        }

        [Test]
        public void ComputeTemporalSGSR_TwoFrameDispatchProducesFiniteUpscaledHdrOutput()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");
            }

            const int rw = 16, rh = 16, dw = 24, dh = 24;
            ComputeShader shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalSGSR.compute"));
            var owned = new List<Object> { shader };
            try
            {
                Texture2D sceneColor = CreateTex(rw, rh, (x, y) => new Color(0.25f + 0.01f * x, 0.18f, 0.10f + 0.01f * y, 1.0f), owned);
                Texture2D depth = CreateTex(rw, rh, (_, _) => new Color(0.5f, 0, 0, 0), owned);
                Texture2D motion = CreateTex(rw, rh, (_, _) => Color.clear, owned);
                Texture2D invalidMotion = CreateTex(rw, rh, (_, _) => new Color(float.NaN, float.PositiveInfinity, 0, 0), owned);
                Texture2D invalidHistory = CreateTex(dw, dh, (_, _) => new Color(float.NaN, float.NaN, float.NaN, float.NaN), owned);
                Texture2D nearDepth = CreateTex(rw, rh, (_, _) => new Color(0.2f, 0, 0, 0), owned);
                Texture2D reactiveClip = CreateTex(rw, rh, (_, _) => new Color(0, 0, 0, 350), owned);
                Texture2D stableClip = CreateTex(rw, rh, (_, _) => Color.clear, owned);

                RenderTexture ycocg = CreateRt(rw, rh, GraphicsFormat.R32_UInt, FilterMode.Point, owned);
                RenderTexture mda = CreateRt(rw, rh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, owned);
                RenderTexture depthA = CreateRt(rw, rh, GraphicsFormat.R32_SFloat, FilterMode.Point, owned);
                RenderTexture depthB = CreateRt(rw, rh, GraphicsFormat.R32_SFloat, FilterMode.Point, owned);
                RenderTexture clipAlpha = CreateRt(rw, rh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture lumaA = CreateRt(rw, rh, GraphicsFormat.R32_UInt, FilterMode.Point, owned);
                RenderTexture lumaB = CreateRt(rw, rh, GraphicsFormat.R32_UInt, FilterMode.Point, owned);
                RenderTexture sgsrHistA = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture sgsrHistB = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture temporalHist = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture output = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);

                int kConv = shader.FindKernel("SGSR_Convert");
                int kAct = shader.FindKernel("SGSR_Activate");
                int kUp = shader.FindKernel("SGSR_Upscale");

                var cbv = new TemporalSgsrParameters
                {
                    renderSizeAndRcp = new Vector4(rw, rh, 1.0f / rw, 1.0f / rh),
                    displaySizeAndRcp = new Vector4(dw, dh, 1.0f / dw, 1.0f / dh),
                    jitterAndFov = new Vector4(0.0f, 0.0f, 1.0f, 0.25f),
                    previousFromCurrentClip = Matrix4x4.identity,
                    resetHistory = 1,
                    reversedZ = 1,
                    preExposure = 1.0f,
                    padding0 = 0,
                };
                var cb = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalSgsrParameters>(), ComputeBufferType.Constant);
                try
                {
                    for (int frame = 0; frame < 3; ++frame)
                    {
                        cbv.resetHistory = frame == 0 ? 1 : 0;
                        cb.SetData(new[] { cbv });
                        bool even = (frame & 1) == 0;
                        RenderTexture prevLuma = even ? lumaA : lumaB;
                        RenderTexture nextLuma = even ? lumaB : lumaA;
                        RenderTexture prevDepth = even ? depthA : depthB;
                        RenderTexture nextDepth = even ? depthB : depthA;
                        RenderTexture prevHist = even ? sgsrHistA : sgsrHistB;
                        RenderTexture nextHist = even ? sgsrHistB : sgsrHistA;

                        shader.SetConstantBuffer("CBV_TemporalSgsrParameters", cb, 0, cb.stride);

                        shader.SetTexture(kConv, "SRV_SceneColorTexture", sceneColor);
                        shader.SetTexture(kConv, "SRV_DepthTexture", depth);
                        shader.SetTexture(kConv, "SRV_MotionTexture", frame == 2 ? invalidMotion : motion);
                        shader.SetTexture(kConv, "UAV_SgsrYCoCg", ycocg);
                        shader.SetTexture(kConv, "UAV_SgsrMotionDepthAlpha", mda);
                        shader.SetTexture(kConv, "UAV_SgsrDepth", nextDepth);
                        shader.Dispatch(kConv, (rw + 7) / 8, (rh + 7) / 8, 1);

                        shader.SetTexture(kAct, "SRV_SgsrYCoCg", ycocg);
                        shader.SetTexture(kAct, "SRV_SgsrMotionDepthAlpha", mda);
                        shader.SetTexture(kAct, "SRV_SgsrCurrentDepth", nextDepth);
                        shader.SetTexture(kAct, "SRV_SgsrPreviousDepth", prevDepth);
                        shader.SetTexture(kAct, "SRV_SgsrPrevLumaHistory", prevLuma);
                        shader.SetTexture(kAct, "UAV_SgsrLumaHistory", nextLuma);
                        shader.SetTexture(kAct, "UAV_SgsrMotionDepthClipAlpha", clipAlpha);
                        shader.Dispatch(kAct, (rw + 7) / 8, (rh + 7) / 8, 1);
                        if (frame == 1)
                        {
                            int center = rw * (rh / 2) + rw / 2;
                            Assert.That(ReadRt(clipAlpha)[center].b, Is.LessThan(0.1f));
                            shader.SetTexture(kAct, "SRV_SgsrPreviousDepth", nearDepth);
                            shader.Dispatch(kAct, (rw + 7) / 8, (rh + 7) / 8, 1);
                            Assert.That(ReadRt(clipAlpha)[center].b, Is.GreaterThan(0.9f));
                            cbv.previousFromCurrentClip.m23 = 0.3f;
                            cb.SetData(new[] { cbv });
                            shader.Dispatch(kAct, (rw + 7) / 8, (rh + 7) / 8, 1);
                            Assert.That(ReadRt(clipAlpha)[center].b, Is.LessThan(0.1f), "Previous clip translation must be applied before comparing depth.");
                            cbv.previousFromCurrentClip.m23 = 2f;
                            cb.SetData(new[] { cbv });
                            shader.Dispatch(kAct, (rw + 7) / 8, (rh + 7) / 8, 1);
                            Assert.That(ReadRt(clipAlpha)[center].b, Is.GreaterThan(0.9f));
                            cbv.previousFromCurrentClip = Matrix4x4.identity;
                            cb.SetData(new[] { cbv });
                            shader.SetTexture(kAct, "SRV_SgsrPreviousDepth", prevDepth);
                            shader.Dispatch(kAct, (rw + 7) / 8, (rh + 7) / 8, 1);
                        }

                        shader.SetVector("SRV_SgsrMotionDepthClipAlpha_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetVector("SRV_SgsrPrevHistory_InfinityView", new Vector4(dw, dh, dw, dh));
                        shader.SetTexture(kUp, "SRV_SgsrYCoCg", ycocg);
                        shader.SetTexture(kUp, "SRV_SgsrMotionDepthClipAlpha", clipAlpha);
                        shader.SetTexture(kUp, "SRV_SgsrPrevHistory", frame == 0 || frame == 2 ? invalidHistory : prevHist);
                        shader.SetTexture(kUp, "UAV_SgsrHistory", nextHist);
                        shader.SetTexture(kUp, "UAV_TemporalHistoryTexture", temporalHist);
                        shader.SetTexture(kUp, "UAV_TemporalOutputTexture", output);
                        shader.Dispatch(kUp, (dw + 7) / 8, (dh + 7) / 8, 1);
                        AssertFiniteNonBlack(ReadRt(output), dw * dh);
                        if (frame == 1)
                        {
                            shader.SetTexture(kAct, "SRV_SgsrPreviousDepth", nearDepth);
                            shader.Dispatch(kAct, (rw + 7) / 8, (rh + 7) / 8, 1);
                            shader.SetTexture(kUp, "SRV_SgsrPrevHistory", invalidHistory);
                            shader.Dispatch(kUp, (dw + 7) / 8, (dh + 7) / 8, 1);
                            AssertFiniteNonBlack(ReadRt(output), dw * dh);

                            shader.SetTexture(kUp, "SRV_SgsrMotionDepthClipAlpha", reactiveClip);
                            shader.SetTexture(kUp, "SRV_SgsrPrevHistory", nextHist);
                            shader.SetTexture(kUp, "UAV_SgsrHistory", prevHist);
                            shader.Dispatch(kUp, (dw + 7) / 8, (dh + 7) / 8, 1);
                            int center = dw * (dh / 2) + dw / 2;
                            float reactiveWeight = ReadRt(prevHist)[center].a;
                            Assert.That(reactiveWeight, Is.GreaterThan(0.3f));
                            shader.SetTexture(kUp, "SRV_SgsrMotionDepthClipAlpha", stableClip);
                            RenderTexture read = prevHist, write = nextHist;
                            for (int step = 0; step < 4; ++step)
                            {
                                shader.SetTexture(kUp, "SRV_SgsrPrevHistory", read);
                                shader.SetTexture(kUp, "UAV_SgsrHistory", write);
                                shader.Dispatch(kUp, (dw + 7) / 8, (dh + 7) / 8, 1);
                                (read, write) = (write, read);
                            }
                            Assert.That(ReadRt(read)[center].a, Is.LessThan(0.03f));
                        }
                    }

                    AssertFiniteNonBlack(ReadRt(output), dw * dh);
                }
                finally
                {
                    cb.Release();
                }
            }
            finally
            {
                CleanupOwned(owned);
            }
        }

        [Test]
        public void ComputeTemporalSTP_TwoFrameDispatchProducesFiniteUpscaledHdrOutput()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");
            }

            const int rw = 16, rh = 16, dw = 24, dh = 24;
            int cw = (rw + 3) >> 2, ch = (rh + 3) >> 2;
            ComputeShader shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalSTP.compute"));
            var owned = new List<Object> { shader };
            try
            {
                Texture2D sceneColor = CreateTex(rw, rh, (x, y) => new Color(0.22f + 0.01f * x, 0.16f + 0.01f * y, 0.09f, 1.0f), owned);
                Texture2D depth = CreateTex(rw, rh, (_, _) => new Color(0.5f, 0, 0, 0), owned);
                Texture2D motion = CreateTex(rw, rh, (_, _) => Color.clear, owned);
                Texture2D blueNoise = CreateTex(16, 16, (x, y) => new Color(((x * 7 + y * 13) & 255) / 255.0f, 0, 0, 1), owned);

                RenderTexture interColor = CreateRt(rw, rh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture interConv = CreateRt(cw, ch, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture interWeights = CreateRt(rw, rh, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);

                RenderTexture dmA = CreateRt(rw, rh, GraphicsFormat.R32_UInt, FilterMode.Point, owned);
                RenderTexture dmB = CreateRt(rw, rh, GraphicsFormat.R32_UInt, FilterMode.Point, owned);
                RenderTexture lumaA = CreateRt(rw, rh, GraphicsFormat.R8G8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture lumaB = CreateRt(rw, rh, GraphicsFormat.R8G8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture convA = CreateRt(cw, ch, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture convB = CreateRt(cw, ch, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture feedA = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture feedB = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);

                RenderTexture histOut = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture displayOut = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);

                int kSetup = shader.FindKernel("StpSetup");
                int kPre = shader.FindKernel("StpPreTaa");
                int kTaa = shader.FindKernel("StpTaa");
                int kSharp = shader.FindKernel("StpSharpen");

                Matrix4x4 proj = Matrix4x4.Perspective(60.0f, 1.0f, 0.1f, 100.0f);
                var cbv = new TemporalStpParameters
                {
                    setupConstants0 = new Vector4(1.0f / rw, 1.0f / rh, 0.5f / rw, 0.5f / rh),
                    setupConstants1 = Vector4.zero,
                    setupConstants2 = new Vector4(dw, dh, 10.0f, 1.0f / Mathf.Log(1000.0f, 2.0f)),
                    setupConstants3 = new Vector4(2.0f * rw / (rw + 4.0f), 2.0f * rh / (rh + 4.0f), -rw / (rw + 4.0f), -rh / (rh + 4.0f)),
                    setupConstants4 = new Vector4(Mathf.Log(1000.0f, 2.0f), 0.1f, 1.0f, 0.0f),
                    setupConstants5 = new Vector4(rw, rh, dw / (Mathf.Ceil(dw / 4.0f) * 4.0f), dh / (Mathf.Ceil(dh / 4.0f) * 4.0f)),
                    dilConstants0 = new Vector4(4.0f / rw, 4.0f / rh, BitConverter.Int32BitsToSingle(rw >> 2), BitConverter.Int32BitsToSingle(rh >> 2)),
                    taaConstants0 = new Vector4((float)rw / dw, (float)rh / dh, 0.5f * rw / dw, 0.5f * rh / dh),
                    taaConstants1 = new Vector4(1.0f / rw, 1.0f / rh, 1.0f / dw, 1.0f / dh),
                    taaConstants2 = new Vector4(0.5f / dw, 0.5f / dh, -0.5f / rw, 0.5f / rh),
                    taaConstants3 = new Vector4(0.5f / rw, 0.5f / rh, dw, dh),
                    sharpenConstants = new Vector4(dw, dh, 0.35f, 0.0f),
                };
                cbv.setupPerViewConstants0 = new Vector4(proj[3, 2] / proj[0, 0], 0, 0, proj[3, 2] / Mathf.Abs(proj[1, 1]));
                cbv.setupPerViewConstants1 = new Vector4(0, 0, proj[0, 0], 0);
                cbv.setupPerViewConstants2 = new Vector4(0, 0, 0, Mathf.Abs(proj[1, 1]));
                cbv.setupPerViewConstants4 = new Vector4(0, 0, proj[0, 0], 0);
                cbv.setupPerViewConstants5 = new Vector4(0, 0, 0, Mathf.Abs(proj[1, 1]));

                var cb = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalStpParameters>(), ComputeBufferType.Constant);
                try
                {
                    for (int frame = 0; frame < 2; ++frame)
                    {
                        int commonPacked = ((frame > 0 ? 1 : 0) << 8) | 15;
                        cbv.commonConstant = new Vector4(BitConverter.Int32BitsToSingle(commonPacked), 9.99f, 0.01f, 0.0f);
                        cb.SetData(new[] { cbv });

                        RenderTexture priorDm = frame == 0 ? dmA : dmB;
                        RenderTexture nextDm = frame == 0 ? dmB : dmA;
                        RenderTexture priorLuma = frame == 0 ? lumaA : lumaB;
                        RenderTexture nextLuma = frame == 0 ? lumaB : lumaA;
                        RenderTexture priorConv = frame == 0 ? convA : convB;
                        RenderTexture nextConv = frame == 0 ? convB : convA;
                        RenderTexture priorFeed = frame == 0 ? feedA : feedB;
                        RenderTexture nextFeed = frame == 0 ? feedB : feedA;

                        shader.SetConstantBuffer("CBV_TemporalStpParameters", cb, 0, cb.stride);

                        shader.SetVector("_StpPriorConvergence_InfinityView", new Vector4(cw, ch, cw, ch));
                        shader.SetVector("_StpPriorFeedback_InfinityView", new Vector4(dw, dh, dw, dh));
                        shader.SetVector("_StpPriorLuma_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetVector("_StpPriorDepthMotion_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetTexture(kSetup, "_StpBlueNoiseIn", blueNoise);
                        shader.SetTexture(kSetup, "_StpInputColor", sceneColor);
                        shader.SetTexture(kSetup, "_StpInputDepth", depth);
                        shader.SetTexture(kSetup, "_StpInputMotion", motion);
                        shader.SetTexture(kSetup, "_StpPriorDepthMotion", priorDm);
                        shader.SetTexture(kSetup, "_StpPriorLuma", priorLuma);
                        shader.SetTexture(kSetup, "_StpPriorConvergence", priorConv);
                        shader.SetTexture(kSetup, "_StpPriorFeedback", priorFeed);
                        shader.SetTexture(kSetup, "_StpDepthMotion", nextDm);
                        shader.SetTexture(kSetup, "_StpLuma", nextLuma);
                        shader.SetTexture(kSetup, "_StpIntermediateColor", interColor);
                        shader.SetTexture(kSetup, "_StpIntermediateConvergence", interConv);
                        shader.Dispatch(kSetup, (rw + 7) / 8, (rh + 7) / 8, 1);

                        shader.SetVector("_StpIntermediateConvergenceSRV_InfinityView", new Vector4(cw, ch, cw, ch));
                        shader.SetVector("_StpLumaSRV_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetTexture(kPre, "_StpBlueNoiseIn", blueNoise);
                        shader.SetTexture(kPre, "_StpIntermediateConvergenceSRV", interConv);
                        shader.SetTexture(kPre, "_StpLumaSRV", nextLuma);
                        shader.SetTexture(kPre, "_StpConvergence", nextConv);
                        shader.SetTexture(kPre, "_StpIntermediateWeights", interWeights);
                        shader.Dispatch(kPre, (rw + 7) / 8, (rh + 7) / 8, 1);

                        shader.SetVector("_StpIntermediateColorSRV_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetVector("_StpIntermediateWeightsSRV_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetVector("_StpConvergenceSRV_InfinityView", new Vector4(cw, ch, cw, ch));
                        shader.SetVector("_StpDepthMotionSRV_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetTexture(kTaa, "_StpBlueNoiseIn", blueNoise);
                        shader.SetTexture(kTaa, "_StpIntermediateColorSRV", interColor);
                        shader.SetTexture(kTaa, "_StpIntermediateWeightsSRV", interWeights);
                        shader.SetTexture(kTaa, "_StpConvergenceSRV", nextConv);
                        shader.SetTexture(kTaa, "_StpDepthMotionSRV", nextDm);
                        shader.SetTexture(kTaa, "_StpPriorFeedback", priorFeed);
                        shader.SetTexture(kTaa, "_StpFeedback", nextFeed);
                        shader.SetTexture(kTaa, "_StpHistoryOutput", histOut);
                        shader.Dispatch(kTaa, (dw + 7) / 8, (dh + 7) / 8, 1);

                        shader.SetTexture(kSharp, "_StpHistoryInput", histOut);
                        shader.SetTexture(kSharp, "_StpDisplayOutput", displayOut);
                        shader.Dispatch(kSharp, (dw + 7) / 8, (dh + 7) / 8, 1);
                    }

                    AssertFiniteNonBlack(ReadRt(displayOut), dw * dh);
                }
                finally
                {
                    cb.Release();
                }
            }
            finally
            {
                CleanupOwned(owned);
            }
        }

        static Texture2D CreateTex(int width, int height, Func<int, int, Color> fill, List<Object> owned)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var data = new Color[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    data[y * width + x] = fill(x, y);
                }
            }
            tex.SetPixels(data);
            tex.Apply(false, false);
            owned.Add(tex);
            return tex;
        }

        static RenderTexture CreateRt(int width, int height, GraphicsFormat format, FilterMode filterMode, List<Object> owned)
        {
            var rt = new RenderTexture(width, height, 0, format)
            {
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                enableRandomWrite = true,
            };
            Assert.IsTrue(rt.Create(), $"Failed to create RenderTexture ({format})");
            owned.Add(rt);
            return rt;
        }

        static Color[] ReadRt(RenderTexture rt)
        {
            AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAFloat);
            req.WaitForCompletion();
            Assert.IsFalse(req.hasError);
            return req.GetData<Color>().ToArray();
        }

        static void AssertFiniteNonBlack(Color[] pixels, int count)
        {
            float sum = 0.0f;
            for (int i = 0; i < count; ++i)
            {
                for (int c = 0; c < 4; ++c)
                {
                    Assert.IsTrue(float.IsFinite(pixels[i][c]), $"Non-finite pixel at {i} channel {c}");
                }
                sum += pixels[i].r + pixels[i].g + pixels[i].b;
            }
            Assert.Greater(sum / count, 0.05f, "Expected non-black upscaled HDR output.");
        }

        static void CleanupOwned(List<Object> owned)
        {
            foreach (Object item in owned)
            {
                if (item is RenderTexture rt)
                {
                    rt.Release();
                }
                Object.DestroyImmediate(item);
            }
        }
    }
}
