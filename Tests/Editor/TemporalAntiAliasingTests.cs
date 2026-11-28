using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TemporalAntiAliasingTests
    {
        [Test]
        public void NewCameraFrameState_HistoryResetAndZeroJitter()
        {
            var manager = VolumeManager.instance;
            bool ownsInitialization = !manager.isInitialized;
            if (ownsInitialization)
                manager.Initialize(null, null);
            GameObject go = new GameObject("TAATestCamera");
            var pool = new ResourcePool();
            CameraFrameState frameState = null;
            try
            {
                Camera camera = go.AddComponent<Camera>();
                frameState = new CameraFrameState(7, pool);
                var size = new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight);
                var requirements = new InfinityTech.Rendering.Temporal.TemporalFrameRequirements(
                    InfinityTech.Rendering.Temporal.ETemporalBackend.Native,
                    InfinityTech.Rendering.Temporal.ETemporalBackend.Native,
                    InfinityTech.Rendering.Temporal.ETemporalProvider.Native,
                    InfinityTech.Rendering.Temporal.ETemporalReconstructionMode.NativeAA,
                    InfinityTech.Rendering.Temporal.ETemporalQuality.Quality,
                    size, size, 1.0f, 0.0f, 0.35f, 8, 1.0f, true, true, true, true, false, false, false);
                EHistoryReset reset = frameState.ResolveHistoryReset(camera, CameraRenderPolicy.For(CameraType.Game), true, 0, 1,
                    new CameraDimensionDescriptor(size, 1.0f, false, new Rect(0, 0, 1, 1)));
                frameState.cameraUniform.UpdateCurrFrameData(camera, size, reset, in requirements, 1.0f / 60.0f);

                Assert.AreEqual(EHistoryReset.NewCamera, reset);
                Assert.AreEqual(0.0f, frameState.cameraUniform.jitter.x);
                Assert.AreEqual(0.0f, frameState.cameraUniform.jitter.y);
                Assert.AreEqual(frameState.cameraUniform.matrix_Proj * frameState.cameraUniform.matrix_WorldToView, frameState.cameraUniform.matrix_ViewJitterProj);
                Assert.AreEqual(frameState.cameraUniform.matrix_ViewFlipYProj, frameState.cameraUniform.matrix_ViewFlipYJitterProj);
            }
            finally
            {
                frameState?.Dispose();
                pool.Dispose();
                Object.DestroyImmediate(go);
                if (ownsInitialization)
                    manager.Deinitialize();
            }
        }
    }
}
