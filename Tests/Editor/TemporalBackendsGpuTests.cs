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
        public void MotionVector_InvalidClipRejectsHistoryWithoutNonFiniteOutput()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/MotionVectorContract.compute"));
            using var current = new ComputeBuffer(6, 16);
            using var previous = new ComputeBuffer(6, 16);
            using var result = new ComputeBuffer(6, 8);
            try
            {
                current.SetData(new[]
                {
                    new Vector4(0.4f, 0.2f, 0, 2), new Vector4(0.4f, 0.2f, 0, 2),
                    new Vector4(0.4f, 0.2f, 0, 2), new Vector4(0.4f, 0.2f, 0, 2),
                    new Vector4(0.4f, 0.2f, 0, 2), new Vector4(float.NaN, 0, 0, 1),
                });
                previous.SetData(new[]
                {
                    new Vector4(0, 0, 0, 2), new Vector4(0, 0, 0, -1),
                    new Vector4(float.NaN, 0, 0, 1), new Vector4(float.PositiveInfinity, 0, 0, 1),
                    new Vector4(8, 0, 0, 1), new Vector4(0, 0, 0, 1),
                });
                int kernel = shader.FindKernel("MotionVectorContract");
                shader.SetBuffer(kernel, "CurrentClips", current);
                shader.SetBuffer(kernel, "PreviousClips", previous);
                shader.SetBuffer(kernel, "Velocities", result);
                shader.Dispatch(kernel, 6, 1, 1);
                var velocities = new Vector2[6];
                result.GetData(velocities);
                Assert.That(velocities[0].x, Is.EqualTo(0.1f).Within(1e-6f));
                Assert.That(velocities[0].y, Is.EqualTo(0.05f).Within(1e-6f));
                for (int i = 1; i < velocities.Length; ++i)
                    Assert.That(velocities[i], Is.EqualTo(new Vector2(2, 2)), $"invalid clip {i}");
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(8, 8)]
        [TestCase(1, 1)]
        [TestCase(1, 8)]
        [TestCase(1, 3)]
        [TestCase(5, 3)]
        public void StpReduction_SeparatelyReducesBothVectorsAndSumWithinEachTile(int width, int height)
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/StpReductionContract.compute"));
            using var inputA = new ComputeBuffer(64, 16);
            using var inputB = new ComputeBuffer(64, 16);
            using var outputA = new ComputeBuffer(64, 16);
            using var outputB = new ComputeBuffer(64, 16);
            using var outputSum = new ComputeBuffer(64, 16);
            try
            {
                var a = new Vector4[64];
                var b = new Vector4[64];
                for (int i = 0; i < 64; ++i)
                {
                    a[i] = new Vector4(-i, i, i % 16 == 7 ? 1000 : -5, -100 - i);
                    b[i] = new Vector4(2 * i, -2 * i, -1 - i, i % 16 == 11 ? 2 : -1000);
                }
                inputA.SetData(a);
                inputB.SetData(b);
                int kernel = shader.FindKernel("StpReductionContract");
                shader.SetInts("InputDimensions", width, height);
                shader.SetBuffer(kernel, "InputA", inputA);
                shader.SetBuffer(kernel, "InputB", inputB);
                shader.SetBuffer(kernel, "OutputA", outputA);
                shader.SetBuffer(kernel, "OutputB", outputB);
                shader.SetBuffer(kernel, "OutputSum", outputSum);
                shader.Dispatch(kernel, 1, 1, 1);
                var actualA = new Vector4[64];
                var actualB = new Vector4[64];
                var actualSum = new Vector4[64];
                outputA.GetData(actualA);
                outputB.GetData(actualB);
                outputSum.GetData(actualSum);
                for (int i = 0; i < 64; ++i)
                {
                    int SourceLane(int lane)
                    {
                        int x = Math.Min((lane & 1) | ((lane >> 2) & 6), width - 1);
                        int y = Math.Min(((lane >> 1) & 3) | ((lane >> 3) & 4), height - 1);
                        return (x & 1) | ((y & 3) << 1) | ((x & 6) << 2) | ((y & 4) << 3);
                    }
                    Vector4 maxA = a[SourceLane(i & ~15)], maxB = b[SourceLane(i & ~15)], sum = Vector4.zero;
                    for (int j = i & ~15; j < (i & ~15) + 16; ++j)
                    {
                        maxA = Vector4.Max(maxA, a[SourceLane(j)]);
                        maxB = Vector4.Max(maxB, b[SourceLane(j)]);
                        sum += a[SourceLane(j)];
                    }
                    Assert.That(actualA[i], Is.EqualTo(maxA), $"lane {i} A");
                    Assert.That(actualB[i], Is.EqualTo(maxB), $"lane {i} B");
                    Assert.That(actualSum[i], Is.EqualTo(sum), $"lane {i} sum");
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

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
        public void StpOrthographicConstants_ProjectThreeMovingOffAxisFrames()
        {
            var go = new GameObject("StpOrthographicCamera");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                camera.orthographic = true;
                var settings = new TemporalSettings(ETemporalBackend.STP, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality, 0.35f);
                var system = new TemporalReconstructionSystem(TemporalDeviceCapabilities.AllCrossPlatformSupported);
                TemporalFrameRequirements requirements = system.PlanFrame(
                    new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, in settings, false), out _);
                var uniform = new CameraUniform();
                camera.projectionMatrix = Matrix4x4.Ortho(-2.6f, 3.4f, -1.8f, 2.2f, 0.1f, 100f);
                camera.transform.SetPositionAndRotation(new Vector3(-0.3f, 0.1f, 0), Quaternion.Euler(-3, 4, 1));
                uniform.UpdateCurrFrameData(camera, requirements.renderSize, EHistoryReset.NewCamera, in requirements, 1f / 60f);
                uniform.Commit();
                camera.projectionMatrix = Matrix4x4.Ortho(-3f, 3.5f, -1.6f, 2.4f, 0.1f, 100f);
                camera.transform.SetPositionAndRotation(new Vector3(0.2f, 0.1f, 0.1f), Quaternion.Euler(1, 5, 0));
                uniform.UpdateCurrFrameData(camera, requirements.renderSize, EHistoryReset.None, in requirements, 1f / 60f);
                uniform.Commit();
                camera.projectionMatrix = Matrix4x4.Ortho(-3.2f, 3.2f, -2.1f, 1.9f, 0.1f, 100f);
                camera.transform.SetPositionAndRotation(new Vector3(0.8f, 0.2f, 0.3f), Quaternion.Euler(3, 7, 1));
                uniform.UpdateCurrFrameData(camera, requirements.renderSize, EHistoryReset.None, in requirements, 1f / 60f);
                TemporalStpParameters k = StpTemporalBackend.BuildParameters(camera, uniform, in requirements, 16, true);
                Assert.That(k.commonConstant.w, Is.EqualTo(1));
                Vector4 world = new Vector4(0.3f, 0.4f, 8f, 1f);
                Vector4 priorView = uniform.lastWorldToView * world;
                Vector4 priorClip = uniform.lastUnjitteredProj * priorView;
                float px = priorClip.x / priorClip.w, py = priorClip.y / priorClip.w, z = -priorView.z;
                Vector4 a = k.setupPerViewConstants0, b = k.setupPerViewConstants1, c = k.setupPerViewConstants2;
                Vector4 d = k.setupPerViewConstants3, e = k.setupPerViewConstants4, f = k.setupPerViewConstants5;
                Vector4 g = k.setupPerViewConstants6, h = k.setupPerViewConstants7;
                float qx = px * (z * a.x + a.y) + z * a.z;
                float qy = py * (z * a.w + b.x) + z * b.y;
                float fw = qx * d.z + qy * d.w + z * e.x + e.y;
                Vector2 forward = new Vector2(qx * b.z + qy * b.w + z * c.x + c.y,
                    qx * c.z + qy * c.w + z * d.x + d.y) / fw;
                float bw = qx * g.z + qy * g.w + z * h.x + h.y;
                Vector2 backward = new Vector2(qx * e.z + qy * e.w + z * f.x + f.y,
                    qx * f.z + qy * f.w + z * g.x + g.y) / bw;
                Vector4 currentClip = uniform.unjitteredProj * uniform.matrix_WorldToView * world;
                Vector4 previousClip = uniform.lastLastUnjitteredProj * uniform.lastLastWorldToView * world;
                Assert.That(forward.x, Is.EqualTo(currentClip.x / currentClip.w).Within(0.0001f));
                Assert.That(forward.y, Is.EqualTo(currentClip.y / currentClip.w).Within(0.0001f));
                Assert.That(backward.x, Is.EqualTo(previousClip.x / previousClip.w).Within(0.0001f));
                Assert.That(backward.y, Is.EqualTo(previousClip.y / previousClip.w).Within(0.0001f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FsrReconstruction_AtomicallyKeepsNearestDepthWhenMotionProjectsMultiplePixels()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders required.");

            const int size = 16;
            bool reversed = SystemInfo.usesReversedZBuffer;
            float nearDepth = reversed ? 0.9f : 0.1f;
            float farDepth = reversed ? 0.2f : 0.8f;
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalFSR.compute"));
            var owned = new List<Object> { shader };
            using var constantBuffer = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalFsrParameters>(), ComputeBufferType.Constant);
            using var reconstructed = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size * size, sizeof(uint));
            try
            {
                int2 dimensions = new int2(size);
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60f, 1f, 0.1f, 100f), false);
                var constants = new TemporalFsrParameters
                {
                    renderSize = dimensions,
                    previousRenderSize = dimensions,
                    upscaleSize = dimensions,
                    previousUpscaleSize = dimensions,
                    maxRenderSize = dimensions,
                    maxUpscaleSize = dimensions,
                    deviceToViewDepth = new Vector4(-projection[2, 2], projection[2, 3], 1f, 1f),
                    motionVectorScale = new Vector2(-1f, -1f),
                    downscaleFactor = Vector2.one,
                    viewSpaceToMeters = 1f,
                    jitterSequenceLength = 8f,
                };
                Texture2D input = CreateTex(size, size, (_, _) => Color.white, owned);
                Texture2D depth = CreateTex(size, size, (x, _) => new Color(x < 5 ? nearDepth : farDepth, 0, 0, 0), owned);
                Texture2D motion = CreateTex(size, size, (x, _) => new Color(x < 6 ? (x - 8f) / size : 0f, 0, 0, 0), owned);
                RenderTexture dilatedDepth = CreateRt(size, size, GraphicsFormat.R32_SFloat, FilterMode.Point, owned);
                RenderTexture dilatedMotion = CreateRt(size, size, GraphicsFormat.R16G16_SFloat, FilterMode.Point, owned);
                RenderTexture farthestDepth = CreateRt(size, size, GraphicsFormat.R16_SFloat, FilterMode.Point, owned);
                RenderTexture luma = CreateRt(size, size, GraphicsFormat.R16_SFloat, FilterMode.Point, owned);
                RenderTexture locks = CreateRt(size, size, GraphicsFormat.R8_UNorm, FilterMode.Point, owned);

                constantBuffer.SetData(new[] { constants });
                shader.SetConstantBuffer("CBV_TemporalFsrParameters", constantBuffer, 0, constantBuffer.stride);
                int clear = shader.FindKernel("FSR_Clear");
                shader.SetBuffer(clear, "rw_reconstructed_previous_nearest_depth", reconstructed);
                shader.SetTexture(clear, "rw_new_locks", locks);
                shader.Dispatch(clear, 2, 2, 1);
                int prepare = shader.FindKernel("FSR_PrepareInputs");
                shader.SetTexture(prepare, "r_input_color_jittered", input);
                shader.SetTexture(prepare, "r_input_depth", depth);
                shader.SetTexture(prepare, "r_input_motion_vectors", motion);
                shader.SetBuffer(prepare, "rw_reconstructed_previous_nearest_depth", reconstructed);
                shader.SetTexture(prepare, "rw_dilated_depth", dilatedDepth);
                shader.SetTexture(prepare, "rw_dilated_motion_vectors", dilatedMotion);
                shader.SetTexture(prepare, "rw_farthest_depth", farthestDepth);
                shader.SetTexture(prepare, "rw_current_luma", luma);
                shader.Dispatch(prepare, 2, 2, 1);

                var bitPatterns = new uint[size * size];
                reconstructed.GetData(bitPatterns);
                Assert.That(BitConverter.Int32BitsToSingle((int)bitPatterns[8 + 8 * size]), Is.EqualTo(nearDepth).Within(1e-5f));
                Assert.That(BitConverter.Int32BitsToSingle((int)bitPatterns[13 + 8 * size]), Is.EqualTo(farDepth).Within(1e-5f));
            }
            finally { CleanupOwned(owned); }
        }

        [TestCase(0.5f)]
        [TestCase(1f)]
        public void FSRAndSGSR_DisplayRCASAgreeOnFormerSGSRBlackAndBrightPixels(float sharpness)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");

            var fsr = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalFSR.compute"));
            var sgsr = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalSGSR.compute"));
            var owned = new List<Object> { fsr, sgsr };
            using var fsrBuffer = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalFsrParameters>(), ComputeBufferType.Constant);
            using var sgsrBuffer = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalSgsrParameters>(), ComputeBufferType.Constant);
            try
            {
                const int width = 33, height = 19, sampleY = 3;
                const int darkSampleX = 2, brightSampleX = 6;
                var size = new int2(width, height);
                Texture2D input = CreateTex(width, height, (x, y) =>
                {
                    float value = (x, y) switch
                    {
                        (darkSampleX, sampleY) => 0.43994140625f,
                        (brightSampleX, sampleY) => 0.44140625f,
                        (darkSampleX or brightSampleX, sampleY - 1) => 0.375f,
                        _ => 0.5f,
                    };
                    return new Color(value, value, value, 1f);
                }, owned);
                RenderTexture fsrOutput = CreateRt(width, height, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, owned);
                RenderTexture sgsrOutput = CreateRt(width, height, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, owned);
                ClearRt(fsrOutput, 2f);
                ClearRt(sgsrOutput, 3f);
                var fsrParameters = new TemporalFsrParameters
                {
                    upscaleSize = size,
                    rcasConfig = new Vector4(TemporalReconstructionUtility.ResolveRCASGain(sharpness), 0f, 0f, 0f),
                };
                var sgsrParameters = SgsrTemporalBackend.BuildParameters(size, size, Vector2.zero, Matrix4x4.identity,
                    true, SystemInfo.usesReversedZBuffer, false, sharpness);
                fsrBuffer.SetData(new[] { fsrParameters });
                sgsrBuffer.SetData(new[] { sgsrParameters });

                int fsrKernel = fsr.FindKernel("FSR_RCAS");
                fsr.SetConstantBuffer("CBV_TemporalFsrParameters", fsrBuffer, 0, fsrBuffer.stride);
                fsr.SetTexture(fsrKernel, "r_rcas_input", input);
                fsr.SetTexture(fsrKernel, "rw_upscaled_output", fsrOutput);
                fsr.Dispatch(fsrKernel, (width + 15) / 16, (height + 15) / 16, 1);

                int sgsrKernel = sgsr.FindKernel("SGSR_Sharpen");
                sgsr.SetConstantBuffer("CBV_TemporalSgsrParameters", sgsrBuffer, 0, sgsrBuffer.stride);
                sgsr.SetTexture(sgsrKernel, "SRV_SgsrDisplayInput", input);
                sgsr.SetTexture(sgsrKernel, "UAV_TemporalOutputTexture", sgsrOutput);
                sgsr.Dispatch(sgsrKernel, (width + 15) / 16, (height + 7) / 8, 1);

                Color[] fsrPixels = ReadRt(fsrOutput);
                Color[] sgsrPixels = ReadRt(sgsrOutput);
                Assert.AreEqual(fsrPixels, sgsrPixels, "The display RCAS must agree for the same input and slider value.");
                AssertFiniteNonBlack(sgsrPixels, sgsrPixels.Length);
                Assert.That(sgsrPixels[sgsrPixels.Length - 1].r, Is.InRange(0.495f, 0.505f));
                Assert.That(sgsrPixels[sampleY * width + darkSampleX].r, Is.InRange(0.3f, 0.5f));
                Assert.That(sgsrPixels[sampleY * width + brightSampleX].r, Is.InRange(0.3f, 0.5f));
            }
            finally { CleanupOwned(owned); }
        }

        [TestCase(16, 16, 0f)]
        [TestCase(17, 19, 1f)]
        public void ComputeTemporalSGSR_MultiFrameHdrAndSharpenIsolation(int width, int height, float sharpness)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");
            }

            int2 renderSize = new int2(width, height);
            int2 displaySize = renderSize * 2;
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalSGSR.compute"));
            var owned = new List<Object> { shader };
            using var parametersBuffer = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<TemporalSgsrParameters>(), ComputeBufferType.Constant);
            try
            {
                Color expected = new Color(2.0f, 0.4f, 0.8f, 1f);
                bool reversedDepth = SystemInfo.usesReversedZBuffer;
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60f, 1f, 0.1f, 100f), false);
                Texture2D color = CreateTex(width, height, (_, _) => expected, owned);
                Texture2D depth = CreateTex(width, height, (_, _) => new Color(0.5f, 0, 0, 0), owned);
                Texture2D motion = CreateTex(width, height, (_, _) => Color.clear, owned);
                GraphicsFormat hdr = GraphicsFormat.R16G16B16A16_SFloat;
                RenderTexture colorLuma = CreateRt(width, height, hdr, FilterMode.Point, owned);
                RenderTexture motionDepth = CreateRt(width, height, hdr, FilterMode.Bilinear, owned);
                RenderTexture depthClip = CreateRt(width, height, GraphicsFormat.R16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture historyA = CreateRt(displaySize.x, displaySize.y, hdr, FilterMode.Bilinear, owned);
                RenderTexture historyB = CreateRt(displaySize.x, displaySize.y, hdr, FilterMode.Bilinear, owned);
                RenderTexture publicHistory = CreateRt(displaySize.x, displaySize.y, hdr, FilterMode.Bilinear, owned);
                RenderTexture output = CreateRt(displaySize.x, displaySize.y, hdr, FilterMode.Bilinear, owned);

                int convert = shader.FindKernel("SGSR_Convert");
                int activate = shader.FindKernel("SGSR_Activate");
                int upscale = shader.FindKernel("SGSR_Upscale");
                int sharpen = shader.FindKernel("SGSR_Sharpen");
                shader.SetConstantBuffer("CBV_TemporalSgsrParameters", parametersBuffer, 0, parametersBuffer.stride);
                shader.SetTexture(convert, "SRV_SceneColorTexture", color);
                shader.SetTexture(convert, "SRV_DepthTexture", depth);
                shader.SetTexture(convert, "SRV_MotionTexture", motion);
                shader.SetTexture(convert, "UAV_SgsrColorLuma", colorLuma);
                shader.SetTexture(convert, "UAV_SgsrMotionDepthLuma", motionDepth);
                shader.SetTexture(activate, "SRV_SgsrMotionDepthLuma", motionDepth);
                shader.SetTexture(activate, "UAV_SgsrDepthClip", depthClip);
                shader.SetTexture(upscale, "SRV_SgsrColorLuma", colorLuma);
                shader.SetTexture(upscale, "SRV_SgsrMotionDepthLuma", motionDepth);
                shader.SetTexture(upscale, "SRV_SgsrDepthClip", depthClip);
                shader.SetTexture(upscale, "UAV_TemporalHistoryTexture", publicHistory);
                shader.SetTexture(sharpen, "UAV_TemporalOutputTexture", output);

                Vector2[] jitterPixels = { Vector2.zero, new Vector2(-0.4f, 0.3f), new Vector2(0.25f, -0.4f), new Vector2(0.4f, 0.4f) };
                for (int frame = 0; frame < jitterPixels.Length; ++frame)
                {
                    Vector2 jitter = Vector2.Scale(jitterPixels[frame], new Vector2(1f / width, 1f / height));
                    TemporalSgsrParameters parameters = SgsrTemporalBackend.BuildParameters(renderSize, displaySize,
                        jitter, projection, false, reversedDepth, frame == 0, sharpness);
                    parametersBuffer.SetData(new[] { parameters });
                    shader.SetTexture(upscale, "SRV_SgsrPreviousHistory", historyA);
                    shader.SetTexture(upscale, "UAV_SgsrHistory", historyB);
                    shader.SetTexture(sharpen, "SRV_SgsrDisplayInput", historyB);
                    shader.Dispatch(convert, (width + 15) / 16, (height + 7) / 8, 1);
                    shader.Dispatch(activate, (width + 15) / 16, (height + 7) / 8, 1);
                    shader.Dispatch(upscale, (displaySize.x + 15) / 16, (displaySize.y + 7) / 8, 1);
                    Color[] accumulation = ReadRt(publicHistory);
                    shader.Dispatch(sharpen, (displaySize.x + 15) / 16, (displaySize.y + 7) / 8, 1);
                    Color[] display = ReadRt(output);
                    Assert.AreEqual(accumulation, ReadRt(publicHistory), "Display filter wrote into shared history.");
                    foreach (int index in new[] { 0, displaySize.x * displaySize.y / 2, display.Length - 1 })
                    {
                        Assert.That(accumulation[index].r, Is.EqualTo(expected.r).Within(0.045f), $"history frame {frame}, pixel {index}");
                        Assert.That(display[index].r, Is.EqualTo(expected.r).Within(0.045f), $"display frame {frame}, pixel {index}");
                        Assert.That(display[index].g, Is.EqualTo(expected.g).Within(0.025f));
                        Assert.That(display[index].b, Is.EqualTo(expected.b).Within(0.025f));
                        Assert.AreEqual(1f, accumulation[index].a);
                    }
                    (historyA, historyB) = (historyB, historyA);
                }
            }
            finally { CleanupOwned(owned); }
        }

        [TestCase(16, 16)]
        [TestCase(33, 35)]
        [TestCase(1, 9)]
        [TestCase(1, 1)]
        public void ComputeTemporalSTP_TwoFrameDispatchProducesFiniteUpscaledHdrOutput(int rw, int rh)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("Compute shaders and AsyncGPUReadback required.");
            }

            int dw = rw * 3 / 2, dh = rh * 3 / 2;
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

                RenderTexture interColor = CreateRt(rw, rh, GraphicsFormat.A2B10G10R10_UNormPack32, FilterMode.Bilinear, owned);
                RenderTexture interConv = CreateRt(cw, ch, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture interWeights = CreateRt(rw, rh, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);

                RenderTexture dmA = CreateRt(rw, rh, GraphicsFormat.R32_UInt, FilterMode.Point, owned);
                RenderTexture dmB = CreateRt(rw, rh, GraphicsFormat.R32_UInt, FilterMode.Point, owned);
                RenderTexture lumaA = CreateRt(rw, rh, GraphicsFormat.R8G8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture lumaB = CreateRt(rw, rh, GraphicsFormat.R8G8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture convA = CreateRt(cw, ch, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture convB = CreateRt(cw, ch, GraphicsFormat.R8_UNorm, FilterMode.Bilinear, owned);
                RenderTexture feedA = CreateRt(dw, dh, GraphicsFormat.A2B10G10R10_UNormPack32, FilterMode.Bilinear, owned);
                RenderTexture feedB = CreateRt(dw, dh, GraphicsFormat.A2B10G10R10_UNormPack32, FilterMode.Bilinear, owned);

                RenderTexture histA = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
                RenderTexture histB = CreateRt(dw, dh, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, owned);
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
                    dilConstants0 = new Vector4(1.0f / cw, 1.0f / ch, BitConverter.Int32BitsToSingle(cw), BitConverter.Int32BitsToSingle(ch)),
                    taaConstants0 = new Vector4((float)rw / dw, (float)rh / dh, 0.5f * rw / dw, 0.5f * rh / dh),
                    taaConstants1 = new Vector4(1.0f / rw, 1.0f / rh, 1.0f / dw, 1.0f / dh),
                    taaConstants2 = new Vector4(0.5f / dw, 0.5f / dh, -0.5f / rw, 0.5f / rh),
                    taaConstants3 = new Vector4(0.5f / rw, 0.5f / rh, dw, dh),
                    sharpenConstants =new Vector4(dw, dh, 0.35f, 0.0f),
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
                        RenderTexture nextHistory = frame == 0 ? histB : histA;

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
                        if (frame == 0) ClearRt(interConv, 0);
                        shader.Dispatch(kSetup, (rw + 7) / 8, (rh + 7) / 8, 1);
                        if (frame == 0)
                        {
                            Color[] zeroSeed = ReadRt(interConv);
                            ClearRt(interConv, 1);
                            shader.Dispatch(kSetup, (rw + 7) / 8, (rh + 7) / 8, 1);
                            Color[] oneSeed = ReadRt(interConv);
                            for (int i = 0; i < cw * ch; ++i)
                                Assert.That(oneSeed[i].r, Is.EqualTo(zeroSeed[i].r), $"STP setup did not write convergence texel {i} of {cw}x{ch}");
                        }

                        shader.SetVector("_StpIntermediateConvergenceSRV_InfinityView", new Vector4(cw, ch, cw, ch));
                        shader.SetVector("_StpLumaSRV_InfinityView", new Vector4(rw, rh, rw, rh));
                        shader.SetTexture(kPre, "_StpBlueNoiseIn", blueNoise);
                        shader.SetTexture(kPre, "_StpIntermediateConvergenceSRV", interConv);
                        shader.SetTexture(kPre, "_StpLumaSRV", nextLuma);
                        shader.SetTexture(kPre, "_StpConvergence", nextConv);
                        shader.SetTexture(kPre, "_StpIntermediateWeights", interWeights);
                        if (frame == 0) ClearRt(nextConv, 0);
                        shader.Dispatch(kPre, (rw + 7) / 8, (rh + 7) / 8, 1);
                        if (frame == 0)
                        {
                            Color[] zeroSeed = ReadRt(nextConv);
                            ClearRt(nextConv, 1);
                            shader.Dispatch(kPre, (rw + 7) / 8, (rh + 7) / 8, 1);
                            Color[] oneSeed = ReadRt(nextConv);
                            for (int i = 0; i < cw * ch; ++i)
                                Assert.That(oneSeed[i].r, Is.EqualTo(zeroSeed[i].r), $"STP pre-TAA did not write convergence texel {i} of {cw}x{ch}");
                        }

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
                        shader.SetTexture(kTaa, "_StpHistoryOutput", nextHistory);
                        shader.Dispatch(kTaa, (dw + 7) / 8, (dh + 7) / 8, 1);

                        shader.SetTexture(kSharp, "_StpHistoryInput", nextHistory);
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

        static void ClearRt(RenderTexture target, float value)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(false, true, new Color(value, value, value, value));
            RenderTexture.active = previous;
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
