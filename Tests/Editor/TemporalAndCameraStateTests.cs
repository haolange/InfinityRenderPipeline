using System;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Temporal;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public sealed class TemporalAndCameraStateTests
    {
        sealed class CountingCameraExtension : ICameraStateExtension
        {
            internal static readonly CameraExtensionKey<CountingCameraExtension> Key = CameraExtensionKey<CountingCameraExtension>.Default;
            internal static readonly Func<ResourcePool, CountingCameraExtension> Factory = _ => new CountingCameraExtension();

            public int beginCount;
            public int commitCount;
            public int rollbackCount;
            public int failCount;
            public int flushCount;
            public int disposeCount;

            public void BeginFrame() => beginCount++;
            public void CommitFrame(FrameFeatureSet features, int frameCount) => commitCount++;
            public void RollbackFrame() => rollbackCount++;
            public void CameraFailed(bool queueUncertain) => failCount++;
            public void FlushRetired(ResourceReuseFence fence) => flushCount++;
            public void Dispose(ResourceReuseFence fence) => disposeCount++;
            public void Dispose() => disposeCount++;
        }

        [Test]
        public void CameraStateExtension_ParticipatesInLifecycleAndIsZeroGCInSteadyState()
        {
            VolumeManager manager = VolumeManager.instance;
            bool ownsInit = !manager.isInitialized;
            if (ownsInit) manager.Initialize(null, null);
            var pool = new ResourcePool();
            var state = new CameraFrameState(901, pool);
            try
            {
                CountingCameraExtension ext = state.GetOrCreateExtension(CountingCameraExtension.Key, CountingCameraExtension.Factory);
                Assert.AreSame(ext, state.GetOrCreateExtension(CountingCameraExtension.Key, CountingCameraExtension.Factory));

                state.BeginFrame();
                state.executeSucceeded = true;
                state.CommitFrame();
                state.BeginFrame();
                state.RollbackFrame();
                state.BeginFrame();
                state.CameraFailed(false);
                state.FlushRetired(default);

                Assert.AreEqual(3, ext.beginCount);
                Assert.AreEqual(1, ext.commitCount);
                Assert.AreEqual(1, ext.rollbackCount);
                Assert.AreEqual(1, ext.failCount);
                Assert.AreEqual(1, ext.flushCount);

                // Warm up typed extensions once, then verify steady-state O(1) lookup allocates zero bytes.
                _ = state.GetLightingState();
                _ = state.GetScreenSpaceState();
                _ = state.GetTemporalState();
                Recorder alloc = Recorder.Get("GC.Alloc");
                alloc.enabled = true;
                _ = state.GetOrCreateExtension(CountingCameraExtension.Key, CountingCameraExtension.Factory);
                _ = state.GetLightingState();
                _ = state.GetScreenSpaceState();
                _ = state.GetTemporalState();
                alloc.enabled = false;
                Assert.AreEqual(0, alloc.sampleBlockCount);

                state.Dispose();
                Assert.AreEqual(1, ext.disposeCount);
            }
            finally
            {
                pool.Dispose();
                if (ownsInit) manager.Deinitialize();
            }
        }

        [Test]
        public void TemporalReconstructionSystem_PlansRequirementsAcrossBackends_AndReportsFallback()
        {
            var system = new TemporalReconstructionSystem(TemporalDeviceCapabilities.AllCrossPlatformSupported);

            var nativeAa = new TemporalSettings(ETemporalBackend.Native, ETemporalReconstructionMode.NativeAA, ETemporalQuality.Quality, 0.35f);
            TemporalFrameRequirements aaReq = system.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, nativeAa, false),
                out TemporalBackendCapability aaCap);
            Assert.IsTrue(aaCap.supported);
            Assert.AreEqual(ETemporalBackend.Native, aaReq.activeBackend);
            Assert.AreEqual(new int2(1920, 1080), aaReq.renderSize);
            Assert.AreEqual(0.0f, aaReq.mipBias);
            Assert.IsTrue(aaReq.requiresJitter && aaReq.requiresMotionVectors && aaReq.requiresReactiveMask);

            var fsrUpscale = new TemporalSettings(ETemporalBackend.FSR, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Balanced, 0.5f);
            TemporalFrameRequirements fsrReq = system.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, fsrUpscale, false),
                out TemporalBackendCapability fsrCap);
            Assert.IsTrue(fsrCap.supported);
            Assert.AreEqual(ETemporalBackend.FSR, fsrReq.activeBackend);
            Assert.Less(fsrReq.renderSize.x, 1920);
            Assert.Less(fsrReq.mipBias, 0.0f);
            Assert.IsTrue(fsrReq.requiresReactiveMask);

            var sgsrUpscale = new TemporalSettings(ETemporalBackend.SGSR, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Balanced, 0.25f);
            TemporalFrameRequirements sgsrReq = system.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, sgsrUpscale, false),
                out TemporalBackendCapability sgsrCap);
            Assert.IsTrue(sgsrCap.supported);
            Assert.AreEqual(ETemporalBackend.SGSR, sgsrReq.activeBackend);
            Assert.AreEqual(ETemporalModeMask.Upscaling, sgsrCap.supportedModes);

            var stpUpscale = new TemporalSettings(ETemporalBackend.STP, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality, 0.35f);
            TemporalFrameRequirements stpReq = system.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, stpUpscale, false),
                out TemporalBackendCapability stpCap);
            Assert.IsTrue(stpCap.supported);
            Assert.AreEqual(ETemporalBackend.STP, stpReq.activeBackend);
            Assert.IsTrue(stpReq.requiresThreeFrameHistory);

            // Platform-gated SDK backends fall back deterministically (FSR -> STP -> Native).
            TemporalBackendCapability dlssCap = system.GetCapability(ETemporalBackend.DLSS);
            TemporalBackendCapability xessCap = system.GetCapability(ETemporalBackend.XeSS);
            Assert.IsFalse(dlssCap.supported);
            Assert.IsFalse(xessCap.supported);
            StringAssert.Contains("Windows", dlssCap.unsupportedReason);

            var dlssSettings = new TemporalSettings(ETemporalBackend.DLSS, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality, 0.4f);
            TemporalFrameRequirements dlssReq = system.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, dlssSettings, false),
                out _);
            Assert.AreEqual(ETemporalBackend.FSR, dlssReq.activeBackend);
            Assert.IsTrue(dlssReq.isFallback);

            // Switching backend or mode triggers an automatic history reset on TemporalCameraState.
            var temporalState = new TemporalCameraState();
            temporalState.PrepareFrame(in aaReq);
            temporalState.CommitFrame(null, 1);
            Assert.IsFalse(temporalState.RequiresBackendSwitchReset(in aaReq));
            Assert.IsTrue(temporalState.RequiresBackendSwitchReset(in fsrReq));
            temporalState.Dispose();
        }

        [Test]
        public void PipelineConfigurationResolver_ReportsTemporalCapabilityStatuses()
        {
            var caps = DeviceCapabilitySnapshot.AllSupported;
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            try
            {
                InfinityRenderPipelineAsset.Settings settings = asset.ResolveSettings();
                EffectivePipelineConfiguration cfg = PipelineConfigurationResolver.Evaluate(in settings, in caps);

                Assert.AreEqual(EConfigStatus.Supported, cfg.temporalBackendState.status);
                Assert.AreEqual(ETemporalBackend.Native, cfg.effectiveTemporalBackend);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
