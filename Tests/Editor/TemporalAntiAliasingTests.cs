using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

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
            CameraFrameState frameState = null;
            try
            {
                Camera camera = go.AddComponent<Camera>();
                frameState = new CameraFrameState(7);
                frameState.cameraUniform.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight));

                Assert.IsTrue(frameState.cameraUniform.historyReset);
                Assert.AreEqual(0.0f, frameState.cameraUniform.jitter.x);
                Assert.AreEqual(0.0f, frameState.cameraUniform.jitter.y);
                Assert.AreEqual(frameState.cameraUniform.matrix_ViewProj, frameState.cameraUniform.matrix_ViewJitterProj);
                Assert.AreEqual(frameState.cameraUniform.matrix_ViewFlipYProj, frameState.cameraUniform.matrix_ViewFlipYJitterProj);
            }
            finally
            {
                frameState?.Dispose();
                Object.DestroyImmediate(go);
                if (ownsInitialization)
                    manager.Deinitialize();
            }
        }

        [Test]
        public void FrameGap_ForcesHistoryResetAndZeroJitter()
        {
            GameObject go = new GameObject("TAATestCameraGap");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                CameraUniform uniform = new CameraUniform();
                uniform.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight));
                uniform.Commit();

                Assert.IsFalse(CameraFrameState.ShouldForceHistoryReset(false, 10, 11, cameraType: CameraType.Game));
                uniform.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight), forceHistoryReset: false);
                Assert.IsFalse(uniform.historyReset);

                Assert.IsTrue(CameraFrameState.ShouldForceHistoryReset(false, 10, 12, cameraType: CameraType.Game));
                uniform.UpdateCurrFrameData(camera, new Unity.Mathematics.int2(camera.pixelWidth, camera.pixelHeight), forceHistoryReset: true);
                Assert.IsTrue(uniform.historyReset);
                Assert.AreEqual(0.0f, uniform.jitter.x);
                Assert.AreEqual(0.0f, uniform.jitter.y);
                Assert.AreEqual(uniform.matrix_ViewProj, uniform.matrix_ViewJitterProj);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }



        [Test]
        public void SceneViewSparseRepaintsDoNotDiscardSuccessfulHistory()
        {
            Assert.IsFalse(CameraFrameState.ShouldForceHistoryReset(false, 10, 120, CameraType.SceneView));
            Assert.IsTrue(CameraFrameState.ShouldForceHistoryReset(true, 10, 120, CameraType.SceneView));
        }
    }
}
