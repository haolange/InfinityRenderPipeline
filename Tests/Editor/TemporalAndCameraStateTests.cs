using System;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Temporal;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        public void TemporalReconstructionSystem_PlansRequirementsBeforeSetup_AndReportsUnsupportedExternalBackends()
        {
            var system = new TemporalReconstructionSystem();

            var taaSettings = new TemporalSettings(ETemporalBackend.NativeTAA, ETemporalReconstructionMode.NativeAA, ETemporalQuality.Quality, ERenderResolutionPolicy.FixedScale, 1.0f, 0.35f);
            TemporalFrameRequirements taaReq = system.PlanFrame(new TemporalPlanInput(new int2(1920, 1080), true, true, taaSettings, false), out var taaCap);
            Assert.IsTrue(taaCap.supported);
            Assert.AreEqual(ETemporalBackend.NativeTAA, taaReq.activeBackend);
            Assert.AreEqual(new int2(1920, 1080), taaReq.renderSize);
            Assert.IsTrue(taaReq.requiresJitter && taaReq.requiresMotionVectors && taaReq.requiresPreviousDepth && taaReq.requiresReactiveMask);

            var taauSettings = new TemporalSettings(ETemporalBackend.NativeTAAU, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Balanced, ERenderResolutionPolicy.QualityPreset, 1.0f, 0.5f);
            TemporalFrameRequirements taauReq = system.PlanFrame(new TemporalPlanInput(new int2(1920, 1080), true, true, taauSettings, false), out var taauCap);
            Assert.IsTrue(taauCap.supported);
            Assert.AreEqual(ETemporalBackend.NativeTAAU, taauReq.activeBackend);
            Assert.Less(taauReq.renderSize.x, 1920);
            Assert.Less(taauReq.mipBias, 0.0f);
            Assert.IsFalse(taauReq.requiresReactiveMask);

            // External SDK backends must report Unsupported rather than pretending support.
            TemporalBackendCapability dlssCap = system.GetCapability(ETemporalBackend.DLSS);
            TemporalBackendCapability fsrCap = system.GetCapability(ETemporalBackend.FSR);
            TemporalBackendCapability xessCap = system.GetCapability(ETemporalBackend.XeSS);
            Assert.IsFalse(dlssCap.supported);
            Assert.IsFalse(fsrCap.supported);
            Assert.IsFalse(xessCap.supported);
            StringAssert.Contains("not integrated", dlssCap.unsupportedReason);

            // Switching backend triggers an automatic history reset on TemporalCameraState.
            var temporalState = new TemporalCameraState();
            temporalState.PrepareFrame(in taaReq);
            temporalState.CommitFrame(null, 1);
            Assert.IsFalse(temporalState.RequiresBackendSwitchReset(in taaReq));
            Assert.IsTrue(temporalState.RequiresBackendSwitchReset(in taauReq));
            temporalState.Dispose();
        }

        [Test]
        public void PipelineConfigurationResolver_Resolves64BitMotionMetadataAndCapabilityStatuses()
        {
            var caps = DeviceCapabilitySnapshot.AllSupported;
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            try
            {
                InfinityRenderPipelineAsset.Settings settings = asset.ResolveSettings();
                EffectivePipelineConfiguration cfg = PipelineConfigurationResolver.Evaluate(in settings, in caps);

                Assert.AreEqual(GraphicsFormat.R32G32_SFloat, cfg.motionMetadataFormat);
                Assert.AreEqual(64, cfg.motionMetadataBitsPerPixel);
                Assert.AreEqual(EConfigStatus.Supported, cfg.gBufferLayoutState.status);
                Assert.AreEqual(EConfigStatus.Supported, cfg.directDecalState.status);
                Assert.AreEqual(EConfigStatus.Inactive, cfg.renderScaleState.status);

                var dlssTemporal = new TemporalSettings(ETemporalBackend.DLSS, ETemporalReconstructionMode.Upscaling, ETemporalQuality.Quality, ERenderResolutionPolicy.QualityPreset, 0.77f, 0.5f);
                TemporalFrameRequirements dlssReq = new TemporalReconstructionSystem().PlanFrame(
                    new TemporalPlanInput(new int2(1280, 720), true, true, dlssTemporal, false), out _);
                Assert.AreEqual(ETemporalBackend.NativeTAA, dlssReq.activeBackend);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
