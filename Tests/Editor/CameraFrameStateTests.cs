using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class CameraFrameStateTests
    {
        [TestCase(CameraType.Preview)]
        [TestCase(CameraType.Reflection)]
        public void CameraUniform_NonTemporalCamerasNeverJitterAcrossCommits(CameraType type)
        {
            var go = new GameObject("NonTemporalCameraHistory");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.cameraType = type;
                var data = new CameraUniform();
                for (int frame = 0; frame < 16; frame++)
                {
                    data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(640, 360));
                    Assert.AreEqual(frame == 0, data.historyReset);
                    Assert.AreEqual(Unity.Mathematics.float2.zero, data.jitter);
                    Assert.AreEqual(data.matrix_Proj, data.matrix_JitterProj);
                    Assert.AreEqual(data.matrix_FlipYProj, data.matrix_FlipYJitterProj);
                    data.Commit();
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void CameraUniform_GameJittersOnlyAfterSuccessfulHistoryAndResetsOnCut()
        {
            var go = new GameObject("TemporalCameraHistory");
            try
            {
                var camera = go.AddComponent<Camera>();
                var data = new CameraUniform();
                data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(640, 360));
                Assert.AreEqual(Unity.Mathematics.float2.zero, data.jitter);
                data.Commit();
                data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(640, 360));
                Assert.IsFalse(data.historyReset);
                Assert.AreNotEqual(Unity.Mathematics.float2.zero, data.jitter);
                data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(640, 360), true);
                Assert.IsTrue(data.historyReset);
                Assert.AreEqual(Unity.Mathematics.float2.zero, data.jitter);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void CameraUniform_ResetSeedsFinitePreviousProjectionWithoutCommittingHistory()
        {
            var go = new GameObject("FreshCameraHistory");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(2, 4, -8), Quaternion.Euler(13, 27, 0));
                var data = new CameraUniform();
                data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight));
                Assert.IsTrue(data.historyReset);
                Assert.AreEqual(data.matrix_ViewProj, data.matrix_LastViewProj);
                Assert.AreEqual(data.matrix_ViewFlipYJitterProj, data.matrix_LastViewFlipYJitterProj);
                data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight));
                Assert.IsTrue(data.historyReset, "A preparation is not a successful history commit.");
                data.Commit();
                data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight));
                Assert.IsFalse(data.historyReset);
                data.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight), true);
                Assert.IsTrue(data.historyReset);
                Assert.AreEqual(data.matrix_ViewProj, data.matrix_LastViewProj);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void FrameFeatureSet_RequestProduceReset_Works()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            Assert.IsTrue(features.ShouldRecord(EFrameFeature.Depth));
            Assert.IsFalse(features.IsProduced(EFrameFeature.Depth));

            features.MarkProduced(EFrameFeature.Depth);
            Assert.IsTrue(features.IsProduced(EFrameFeature.Depth));

            features.Reset();
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.Depth));
            Assert.IsFalse(features.IsProduced(EFrameFeature.Depth));
        }

        [Test]
        public void FrameFeatureSet_DBufferIsOptional()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            features.MarkProduced(EFrameFeature.Depth);
            features.Request(EFrameFeature.GBuffer);
            features.MarkSupported(EFrameFeature.GBuffer);
            features.MarkProduced(EFrameFeature.GBuffer);
            features.Request(EFrameFeature.DeferredShading);
            features.MarkSupported(EFrameFeature.DeferredShading);
            features.MarkProduced(EFrameFeature.DeferredShading);
            features.Request(EFrameFeature.TAA);
            features.MarkSupported(EFrameFeature.TAA);
            features.MarkProduced(EFrameFeature.TAA);
            features.Request(EFrameFeature.Display);
            features.MarkSupported(EFrameFeature.Display);
            features.MarkProduced(EFrameFeature.Display);

            Assert.DoesNotThrow(() => features.EnsureRequiredProducers(requiresTemporalAntiAliasing: true));
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.DBuffer));
        }

        [Test]
        public void FrameFeatureSet_EnsureRequiredProducers_ThrowsWhenMissing()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            features.MarkProduced(EFrameFeature.Depth);
            Assert.Throws<InvalidOperationException>(() => features.EnsureRequiredProducers(requiresTemporalAntiAliasing: true));
        }

        [Test]
        public void ScreenSpaceHistory_TracksAcceptedProductionAcrossDisableAndFailure()
        {
            var manager = VolumeManager.instance;
            bool initialize = !manager.isInitialized;
            if (initialize) manager.Initialize(null, null);
            CameraFrameState state = null;
            try
            {
                state = new CameraFrameState(73);
                state.features.MarkProduced(EFrameFeature.SSR);
                state.features.MarkProduced(EFrameFeature.SSGI);
                state.features.MarkProduced(EFrameFeature.GTAO);
                state.executeSucceeded = true;
                state.CommitFrame();
                Assert.IsTrue(state.acceptedSSR && state.acceptedSSGI && state.acceptedGTAO);
                int phase = state.acceptedSSGIFrameIndex;
                state.features.Reset();
                state.RollbackFrame();
                state.CommitFrame();
                Assert.IsTrue(state.acceptedSSR && state.acceptedSSGI && state.acceptedGTAO, "Rejected camera submissions preserve accepted history facts.");
                Assert.AreEqual(phase, state.acceptedSSGIFrameIndex);
                state.executeSucceeded = true;
                state.CommitFrame();
                Assert.IsFalse(state.acceptedSSR || state.acceptedSSGI || state.acceptedGTAO, "A successfully rendered disabled frame breaks effect-history continuity.");
            }
            finally { state?.Dispose(); if (initialize) manager.Deinitialize(); }
        }

        [Test]
        public void CameraFrameState_CreateDispose_DestroysVolumeStack()
        {
            var manager = VolumeManager.instance;
            bool ownsInitialization = !manager.isInitialized;
            if (ownsInitialization)
                manager.Initialize(null, null);
            var existingStack = manager.stack;
            CameraFrameState frameState = null;
            try
            {
                frameState = new CameraFrameState(42);
                Assert.AreEqual(42, frameState.cameraId);
                Assert.IsNotNull(frameState.volumeStack);
                Assert.IsNotNull(frameState.historyCache);
                Assert.IsNotNull(frameState.features);
                Assert.IsNotNull(frameState.cameraUniform);
                Assert.AreEqual(0.0f, frameState.exposureState.evCompensation);

                var cameraStack = frameState.volumeStack;
                Assert.IsTrue(cameraStack.isValid);
                frameState.Dispose();
                frameState = null;
                Assert.IsFalse(cameraStack.isValid);
                Assert.IsTrue(manager.isInitialized);
                Assert.AreSame(existingStack, manager.stack);
                Assert.IsTrue(existingStack.isValid);
            }
            finally
            {
                frameState?.Dispose();
                if (ownsInitialization)
                    manager.Deinitialize();
            }
        }

        [Test]
        public void ShouldForceHistoryReset_NewStateOrFrameGap()
        {
            Assert.IsTrue(CameraFrameState.ShouldForceHistoryReset(newlyCreated: true, lastSeenFrame: 0, frameCount: 1, cameraType: CameraType.Game));
            Assert.IsTrue(CameraFrameState.ShouldForceHistoryReset(newlyCreated: false, lastSeenFrame: 10, frameCount: 12, cameraType: CameraType.Game));
            Assert.IsFalse(CameraFrameState.ShouldForceHistoryReset(newlyCreated: false, lastSeenFrame: 10, frameCount: 11, cameraType: CameraType.Game));
            Assert.IsFalse(CameraFrameState.ShouldForceHistoryReset(newlyCreated: false, lastSeenFrame: 10, frameCount: 10, cameraType: CameraType.Game));
        }

        [Test]
        public void Recycle_OnlyInactiveGameViewsExpire()
        {

            Assert.IsFalse(CameraFrameState.ShouldRecycle(0, 120, CameraType.SceneView));
            Assert.IsFalse(CameraFrameState.ShouldRecycle(0, 121, CameraType.SceneView));
            Assert.IsFalse(CameraFrameState.ShouldRecycle(0, 8, CameraType.Game));
            Assert.IsTrue(CameraFrameState.ShouldRecycle(0, 9, CameraType.Game));
        }
    }
}
