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
            VolumeManager.instance.Initialize(null, null);
            GameObject go = new GameObject("TAATestCamera");
            CameraFrameState frameState = null;
            try
            {
                Camera camera = go.AddComponent<Camera>();
                frameState = new CameraFrameState(7);
                frameState.cameraUniform.UpdateCurrFrameData(camera);

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
                VolumeManager.instance.Deinitialize();
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
                uniform.UpdateCurrFrameData(camera);
                uniform.UnpateUniformData(camera, true);

                Assert.IsFalse(CameraFrameState.ShouldForceHistoryReset(false, 10, 11));
                uniform.UpdateCurrFrameData(camera, forceHistoryReset: false);
                Assert.IsFalse(uniform.historyReset);

                Assert.IsTrue(CameraFrameState.ShouldForceHistoryReset(false, 10, 12));
                uniform.UpdateCurrFrameData(camera, forceHistoryReset: true);
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
        public void ResetRamp_ValidFramesZeroIsZero_EightIsFullWeight()
        {
            int validFrames = 0;
            float configured = 0.97f;
            float zero = ScreenSpaceHistoryUtility.RampTemporalWeight(configured, ref validFrames, resetHistory: true);
            Assert.AreEqual(0.0f, zero);
            Assert.AreEqual(1, validFrames);

            validFrames = 8;
            float full = ScreenSpaceHistoryUtility.RampTemporalWeight(configured, ref validFrames, resetHistory: false);
            Assert.AreEqual(configured, full);
            Assert.AreEqual(ScreenSpaceHistoryUtility.TemporalResetRampFrames, validFrames);

            int resetBlendFrames = 0;
            float resetBlend = ScreenSpaceHistoryUtility.RampTemporalWeight(1.0f, ref resetBlendFrames, resetHistory: true);
            Assert.AreEqual(0.0f, resetBlend);
            resetBlendFrames = 8;
            Assert.AreEqual(1.0f, ScreenSpaceHistoryUtility.RampTemporalWeight(1.0f, ref resetBlendFrames, resetHistory: false));
        }

        [Test]
        public void TaaShader_UsesHistoryNeighborhoodAndResetBlend()
        {
            string shaderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/TemporalAntiAliasing/Compute_TemporalAntiAliasing.compute"));
            Assert.IsTrue(File.Exists(shaderPath), shaderPath);
            string source = File.ReadAllText(shaderPath);
            Assert.IsTrue(source.Contains("HistoryDepthConfidence"));
            Assert.IsTrue(source.Contains("TAA_ResetBlend"));
            Assert.IsFalse(source.Contains("smoothstep(0.02, 0.1"));
            Assert.IsTrue(source.Contains("SampleOffsets[n]"));
        }
    }
}
