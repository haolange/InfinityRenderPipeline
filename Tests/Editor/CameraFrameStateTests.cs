using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class CameraFrameStateTests
    {
        static Unity.Mathematics.int2 Size(Camera camera) => new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight);

        static TemporalFrameRequirements ActiveNativeReq(Unity.Mathematics.int2 size) => new TemporalFrameRequirements(
            ETemporalBackend.Native, ETemporalBackend.Native, ETemporalProvider.Native,
            ETemporalReconstructionMode.NativeAA, ETemporalQuality.Quality,
            size, size, 1.0f, 0.0f, 0.35f, 8, 1.0f, true, true, true, true, false, false, false);

        [TestCase(CameraType.Preview)]
        [TestCase(CameraType.Reflection)]
        public void HistoryReset_NonTemporalCamerasResetEveryFrameAndNeverJitter(CameraType type)
        {
            var manager = VolumeManager.instance;
            bool ownsInitialization = !manager.isInitialized;
            if (ownsInitialization) manager.Initialize(null, null);
            var go = new GameObject("NonTemporalCameraHistory");
            var pool = new ResourcePool();
            CameraFrameState state = null;
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.cameraType = type;
                state = new CameraFrameState(1, pool);
                var dimensions = new CameraDimensionDescriptor(Size(camera), 1.0f, false, new Rect(0, 0, 1, 1));
                TemporalFrameRequirements disabledReq = TemporalFrameRequirements.Disabled(Size(camera));
                for (int frame = 0; frame < 16; frame++)
                {
                    EHistoryReset reset = state.ResolveHistoryReset(camera, CameraRenderPolicy.For(type), frame == 0, frame - 1, frame, dimensions);
                    Assert.AreEqual(EHistoryReset.NotTemporal, reset);
                    state.cameraUniform.UpdateCurrFrameData(camera, Size(camera), reset, in disabledReq, 1f / 60f);
                    Assert.AreEqual(Unity.Mathematics.float2.zero, state.cameraUniform.jitter);
                    Assert.AreEqual(state.cameraUniform.matrix_Proj * state.cameraUniform.matrix_WorldToView, state.cameraUniform.matrix_ViewJitterProj);
                    Assert.AreEqual(state.cameraUniform.matrix_FlipYProj, state.cameraUniform.matrix_FlipYJitterProj);
                    state.preparedDimensions = dimensions;
                    state.cameraUniform.Commit();
                }
            }
            finally
            {
                state?.Dispose();
                pool.Dispose();
                UnityEngine.Object.DestroyImmediate(go);
                if (ownsInitialization) manager.Deinitialize();
            }
        }

        [Test]
        public void HistoryReset_ReasonsFollowCommitGapRollbackDimensionsAndCut()
        {
            var manager = VolumeManager.instance;
            bool ownsInitialization = !manager.isInitialized;
            if (ownsInitialization) manager.Initialize(null, null);
            var go = new GameObject("TemporalCameraHistory");
            var pool = new ResourcePool();
            CameraFrameState state = null;
            try
            {
                var camera = go.AddComponent<Camera>();
                var policy = CameraRenderPolicy.For(CameraType.Game);
                state = new CameraFrameState(2, pool);
                var dimensions = new CameraDimensionDescriptor(Size(camera), 1.0f, false, new Rect(0, 0, 1, 1));
                TemporalFrameRequirements req = ActiveNativeReq(Size(camera));
                Assert.AreEqual(EHistoryReset.NewCamera, state.ResolveHistoryReset(camera, policy, true, 0, 1, dimensions));
                Assert.AreEqual(EHistoryReset.NewCamera, state.ResolveHistoryReset(camera, policy, false, 1, 2, dimensions),
                    "A preparation is not a committed frame.");
                state.cameraUniform.UpdateCurrFrameData(camera, Size(camera), EHistoryReset.NewCamera, in req, 1f / 60f);
                state.preparedDimensions = dimensions;
                state.cameraUniform.Commit();

                Assert.AreEqual(EHistoryReset.None, state.ResolveHistoryReset(camera, policy, false, 2, 3, dimensions));
                Assert.AreEqual(EHistoryReset.Unseen, state.ResolveHistoryReset(camera, policy, false, 2, 4, dimensions));
                state.requiresHistoryReset = true;
                Assert.AreEqual(EHistoryReset.RolledBack, state.ResolveHistoryReset(camera, policy, false, 2, 3, dimensions));
                state.requiresHistoryReset = false;
                var resized = new CameraDimensionDescriptor(Size(camera) + 2, 1.0f, false, new Rect(0, 0, 1, 1));
                Assert.AreEqual(EHistoryReset.Dimensions, state.ResolveHistoryReset(camera, policy, false, 2, 3, resized));
                camera.transform.position += new Vector3(10, 0, 0);
                Assert.AreEqual(EHistoryReset.CameraCut, state.ResolveHistoryReset(camera, policy, false, 2, 3, dimensions));
            }
            finally
            {
                state?.Dispose();
                pool.Dispose();
                UnityEngine.Object.DestroyImmediate(go);
                if (ownsInitialization) manager.Deinitialize();
            }
        }

        [Test]
        public void CameraUniform_JittersOnlyWithoutResetAndSeedsPreviousProjectionOnReset()
        {
            var go = new GameObject("FreshCameraHistory");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(2, 4, -8), Quaternion.Euler(13, 27, 0));
                TemporalFrameRequirements req = ActiveNativeReq(Size(camera));
                var data = new CameraUniform();
                data.UpdateCurrFrameData(camera, Size(camera), EHistoryReset.NewCamera, in req, 1f / 60f);
                Assert.AreEqual(Unity.Mathematics.float2.zero, data.jitter);
                Assert.AreEqual(data.matrix_ViewFlipYProj, data.matrix_LastViewFlipYProj);
                Assert.AreEqual(data.matrix_ViewFlipYJitterProj, data.matrix_LastViewFlipYJitterProj);
                Assert.IsFalse(data.hasCommittedView, "A preparation is not a committed frame.");
                data.Commit();
                Assert.IsTrue(data.hasCommittedView);
                Assert.IsFalse(data.IsCut(camera));
                data.UpdateCurrFrameData(camera, Size(camera), EHistoryReset.None, in req, 1f / 60f);
                Assert.AreNotEqual(Unity.Mathematics.float2.zero, data.jitter);
                data.UpdateCurrFrameData(camera, Size(camera), EHistoryReset.CameraCut, in req, 1f / 60f);
                Assert.AreEqual(Unity.Mathematics.float2.zero, data.jitter);
                Assert.AreEqual(data.matrix_ViewFlipYProj, data.matrix_LastViewFlipYProj);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void TemporalJitter_PreservesCustomOrthographicProjection()
        {
            var go = new GameObject("CustomOrthographicJitterCamera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.orthographic = true;
                Matrix4x4 projection = Matrix4x4.Ortho(-2, 6, -3, 5, 0.2f, 200);
                projection.m01 = 0.17f;
                camera.projectionMatrix = projection;
                var size = new Unity.Mathematics.int2(800, 600);
                var jitter = new Unity.Mathematics.float2(0.25f, -0.375f);
                Matrix4x4 gpu = default, flipY = default;
                TemporalJitter.GetJitteredOrthographicProjectionMatrix(camera, size, jitter, ref gpu, ref flipY);

                Vector4[] points = { new Vector4(-1, 2, -4, 1), new Vector4(3, -2, -50, 1) };
                foreach (bool renderIntoTexture in new[] { true, false })
                {
                    Matrix4x4 originalGPU = GL.GetGPUProjectionMatrix(projection, renderIntoTexture);
                    Matrix4x4 jitteredGPU = renderIntoTexture ? gpu : flipY;
                    foreach (Vector4 point in points)
                    {
                        Vector4 before = originalGPU * point;
                        Vector4 after = jitteredGPU * point;
                        float deltaX = 0.5f * (after.x / after.w - before.x / before.w);
                        float deltaY = 0.5f * (after.y / after.w - before.y / before.w);
                        Assert.That(deltaX, Is.EqualTo(-jitter.x / size.x).Within(1e-6f));
                        float yOrientation = Mathf.Sign(originalGPU.m11 / projection.m11);
                        Assert.That(deltaY, Is.EqualTo(-jitter.y / size.y * yOrientation).Within(1e-6f));
                        Assert.That(after.z / after.w, Is.EqualTo(before.z / before.w).Within(1e-6f));
                    }
                }
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
            features.Request(EFrameFeature.TemporalReconstruction);
            features.MarkSupported(EFrameFeature.TemporalReconstruction);
            features.MarkProduced(EFrameFeature.TemporalReconstruction);
            features.Request(EFrameFeature.Display);
            features.MarkSupported(EFrameFeature.Display);
            features.MarkProduced(EFrameFeature.Display);

            Assert.DoesNotThrow(() => features.EnsureRequiredProducers(InfinityRenderPipeline.s_Capabilities.requiredProducers, true));
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.DBuffer));
        }

        [Test]
        public void FrameFeatureSet_EnsureRequiredProducers_ThrowsWhenMissing()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            features.MarkProduced(EFrameFeature.Depth);
            Assert.Throws<InvalidOperationException>(() => features.EnsureRequiredProducers(InfinityRenderPipeline.s_Capabilities.requiredProducers, true));
        }

        [Test]
        public void ScreenSpaceHistory_TracksAcceptedProductionAcrossDisableAndFailure()
        {
            var manager = VolumeManager.instance;
            bool initialize = !manager.isInitialized;
            if (initialize) manager.Initialize(null, null);
            var pool = new ResourcePool();
            CameraFrameState state = null;
            try
            {
                state = new CameraFrameState(73, pool);
                state.features.MarkProduced(EFrameFeature.SSR);
                state.features.MarkProduced(EFrameFeature.SSGI);
                state.features.MarkProduced(EFrameFeature.GTAO);
                state.executeSucceeded = true;
                state.CommitFrame();
                ScreenSpaceCameraState ssState = state.GetScreenSpaceState();
                Assert.IsTrue(ssState.acceptedSSR && ssState.acceptedSSGI && ssState.acceptedGTAO);
                int phase = ssState.acceptedSSGIFrameIndex;
                state.features.Reset();
                state.RollbackFrame();
                state.CommitFrame();
                Assert.IsTrue(ssState.acceptedSSR && ssState.acceptedSSGI && ssState.acceptedGTAO, "Rejected camera submissions preserve accepted history facts.");
                Assert.AreEqual(phase, ssState.acceptedSSGIFrameIndex);
                state.executeSucceeded = true;
                state.CommitFrame();
                Assert.IsFalse(ssState.acceptedSSR || ssState.acceptedSSGI || ssState.acceptedGTAO, "A successfully rendered disabled frame breaks effect-history continuity.");
            }
            finally { state?.Dispose(); pool.Dispose(); if (initialize) manager.Deinitialize(); }
        }

        [Test]
        public void CameraFrameState_CreateDispose_DestroysVolumeStack()
        {
            var manager = VolumeManager.instance;
            bool ownsInitialization = !manager.isInitialized;
            if (ownsInitialization)
                manager.Initialize(null, null);
            var existingStack = manager.stack;
            var pool = new ResourcePool();
            CameraFrameState frameState = null;
            try
            {
                frameState = new CameraFrameState(42, pool);
                Assert.AreEqual(42, frameState.cameraId);
                Assert.IsNotNull(frameState.volumeStack);
                Assert.IsNotNull(frameState.historyCache);
                Assert.IsNotNull(frameState.features);
                Assert.IsNotNull(frameState.cameraUniform);

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
                pool.Dispose();
                if (ownsInitialization)
                    manager.Deinitialize();
            }
        }

        [Test]
        public void SkippedFrame_OnlyGameCamerasWithAFrameGap()
        {
            Assert.IsTrue(CameraFrameState.SkippedFrame(previousSeenFrame: 10, frameCount: 12, cameraType: CameraType.Game));
            Assert.IsFalse(CameraFrameState.SkippedFrame(previousSeenFrame: 10, frameCount: 11, cameraType: CameraType.Game));
            Assert.IsFalse(CameraFrameState.SkippedFrame(previousSeenFrame: 10, frameCount: 10, cameraType: CameraType.Game));
            Assert.IsFalse(CameraFrameState.SkippedFrame(previousSeenFrame: 10, frameCount: 120, cameraType: CameraType.SceneView));
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
