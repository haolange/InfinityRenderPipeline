using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderCaptureTests
    {
        static RenderCaptureRequest Request(string directory) => new RenderCaptureRequest
        {
            outputDirectory = directory, fixture = "CaptureLifecycle", scene = "NoMatchingScene",
            camera = "NoMatchingCamera", width = 1, height = 1, roi = new RectInt(0, 0, 1, 1)
        };

        [Test]
        public void CancelBeforeCamera_PersistsTerminalStateAndImmutableRequest()
        {
            string directory = Path.Combine(Path.GetTempPath(), "InfinityCaptureTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                RenderCaptureRequest request = Request(directory);
                var session = new RenderCaptureSession(request);
                request.camera = "ChangedAfterStart";
                Assert.AreEqual("NoMatchingCamera", session.request.camera);
                session.Cancel();
                Assert.IsTrue(session.Finished);
                StringAssert.Contains("Cancelled", File.ReadAllText(Path.Combine(directory, "capture.json")));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [UnityTest]
        public IEnumerator MissingCamera_TimesOutWithoutResources()
        {
            string directory = Path.Combine(Path.GetTempPath(), "InfinityCaptureTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                RenderCaptureRequest request = Request(directory);
                request.timeoutSeconds = 0.01f;
                var session = new RenderCaptureSession(request);
                yield return new WaitForSecondsRealtime(0.02f);
                session.Pump();
                Assert.IsTrue(session.Finished);
                StringAssert.Contains("TimedOut", File.ReadAllText(Path.Combine(directory, "capture.json")));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void EvidenceWriteFailure_StopsSessionWithoutMaskingOriginalFailure()
        {
            string directory = Path.Combine(Path.GetTempPath(), "InfinityCaptureTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                var session = new RenderCaptureSession(Request(directory));
                string receipt = Path.Combine(directory, "capture.json");
                File.Delete(receipt);
                Directory.CreateDirectory(receipt);
                LogAssert.Expect(LogType.Exception, new Regex("(UnauthorizedAccessException|IOException)"));
                Assert.DoesNotThrow(() => session.Fail("Original capture failure"));
                Assert.IsTrue(session.Finished);
                Directory.Delete(receipt);
                session.Fail("Secondary failure");
                string evidence = File.ReadAllText(receipt);
                StringAssert.Contains("Original capture failure", evidence);
                StringAssert.DoesNotContain("Secondary failure", evidence);
                StringAssert.Contains("persistenceError", evidence);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [UnityTest]
        public IEnumerator CancelQueuedReadback_RetainsStagingUntilSubmissionAndCallback()
        {
            string directory = Path.Combine(Path.GetTempPath(), "InfinityCaptureTest-" + Guid.NewGuid().ToString("N"));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            scene.name = "CaptureLifecycle-" + Guid.NewGuid().ToString("N");
            var cameraObject = new GameObject("CaptureLifecycleCamera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(1, 1, 0);
            target.Create();
            camera.targetTexture = target;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            asset.qualityVolumeProfile = profile;
            bool ownsVolumes = !VolumeManager.instance.isInitialized;
            if (ownsVolumes) VolumeManager.instance.Initialize(null, null);
            var state = new CameraFrameState(43);
            var commands = new CommandBuffer();
            RenderCaptureSession session = null;
            try
            {
                RenderCaptureRequest request = Request(directory);
                request.scene = scene.name; request.camera = camera.name;
                request.warmupFrames = 0;
                session = new RenderCaptureSession(request);
                session.PrepareCamera(camera, state, asset);
                var staging = session.Reserve("Lighting", "LifecycleFixture", new TextureDescriptor(1, 1)
                {
                    colorFormat = GraphicsFormat.R32_SFloat, dimension = TextureDimension.Tex2D
                }, false);
                // This isolated fixture exercises the same staging owner without rendering a camera.
                commands.SetRenderTarget(staging.texture);
                commands.ClearRenderTarget(false, true, new Color(0.25f, 0, 0, 1));
                commands.RequestAsyncReadback(staging.texture, staging.Complete);
                staging.lifetime.recorded = true;
                session.Cancel();
                Assert.IsFalse(session.Finished);
                Assert.IsFalse(staging.evidence.retired);
                Graphics.ExecuteCommandBuffer(commands);
                session.AfterSubmit();
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!session.Finished && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    session.Pump();
                    yield return null;
                }
                Assert.IsTrue(session.Finished, "Readback did not reach terminal retirement.");
                Assert.IsTrue(staging.evidence.submitted);
                Assert.IsTrue(staging.evidence.readbackTerminal);
                Assert.IsTrue(staging.evidence.retired);
                Assert.AreEqual(0, staging.evidence.statistics.nonFinite);
                Assert.AreEqual(0.25, staging.evidence.statistics.mean, 0.0001);
                StringAssert.Contains("Cancelled", File.ReadAllText(Path.Combine(directory, "capture.json")));
            }
            finally
            {
                AsyncGPUReadback.WaitAllRequests();
                session?.AfterSubmit();
                session?.Cancel();
                commands.Dispose(); state.Dispose();
                if (ownsVolumes) VolumeManager.instance.Deinitialize();
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(profile);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        struct AbandonedCaptureData { public RenderCaptureSession.Staging staging; public Exception failure; }

        [Test]
        public void AbandonedCaptureCommands_DoNotLatchQueuedOwnership()
        {
            string directory = Path.Combine(Path.GetTempPath(), "InfinityCaptureTest-" + Guid.NewGuid().ToString("N"));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            scene.name = "CaptureLifecycle-" + Guid.NewGuid().ToString("N");
            var cameraObject = new GameObject("CaptureLifecycleCamera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(1, 1, 0);
            target.Create();
            camera.targetTexture = target;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            asset.qualityVolumeProfile = profile;
            bool ownsVolumes = !VolumeManager.instance.isInitialized;
            if (ownsVolumes) VolumeManager.instance.Initialize(null, null);
            var state = new CameraFrameState(43);
            var commands = new CommandBuffer();
            RenderCaptureSession session = null;
            try
            {
                RenderCaptureRequest request = Request(directory);
                request.scene = scene.name; request.camera = camera.name;
                request.warmupFrames = 0;
                session = new RenderCaptureSession(request);
                session.PrepareCamera(camera, state, asset);
                var staging = session.Reserve("Lighting", "LifecycleFixture", new TextureDescriptor(1, 1)
                {
                    colorFormat = GraphicsFormat.R32_SFloat, dimension = TextureDimension.Tex2D
                }, false);
                var pool = new ResourcePool();
                try
                {
                    var graph = new RGBuilder("AbandonedCapture");
                    var error = new InvalidOperationException("Injected after readback recording, before queue acceptance");
                    try
                    {
                        using (RGTransferPassRef pass = graph.AddTransferPass<AbandonedCaptureData>(new ProfilingSampler("AbandonedReadback")))
                        {
                            pass.EnablePassCulling(false);
                            pass.SetQueueObserver(staging);
                            pass.GetPassData<AbandonedCaptureData>() = new AbandonedCaptureData { staging = staging, failure = error };
                            pass.SetExecuteFunc((in AbandonedCaptureData data, in RGTransferEncoder encoder, RGObjectPool objects) =>
                            {
                                encoder.RequestAsyncReadback(data.staging.texture, data.staging.Complete);
                                throw data.failure;
                            });
                        }
                        Assert.AreSame(error, Assert.Throws<InvalidOperationException>(() => graph.Execute(default, pool, commands)));
                        Assert.IsFalse(staging.lifetime.recorded);
                        session.Cancel();
                        Assert.IsTrue(session.Finished);
                        Assert.IsTrue(staging.evidence.retired);
                        Assert.IsFalse(staging.evidence.submitted);
                        Assert.IsFalse(staging.evidence.readbackTerminal);
                    }
                    finally { graph.Dispose(); }
                }
                finally { pool.Dispose(); }
            }
            finally
            {
                AsyncGPUReadback.WaitAllRequests();
                session?.AfterSubmit();
                session?.Cancel();
                commands.Dispose(); state.Dispose();
                if (ownsVolumes) VolumeManager.instance.Deinitialize();
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(profile);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void UncertainQueue_HoldsStagingUntilSubmittedReadbackDrain()
        {
            string directory = Path.Combine(Path.GetTempPath(), "InfinityCaptureTest-" + Guid.NewGuid().ToString("N"));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            scene.name = "CaptureLifecycle-" + Guid.NewGuid().ToString("N");
            var cameraObject = new GameObject("CaptureLifecycleCamera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(1, 1, 0);
            target.Create();
            camera.targetTexture = target;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            asset.qualityVolumeProfile = profile;
            bool ownsVolumes = !VolumeManager.instance.isInitialized;
            if (ownsVolumes) VolumeManager.instance.Initialize(null, null);
            var state = new CameraFrameState(43);
            var commands = new CommandBuffer();
            RenderCaptureSession session = null;
            try
            {
                RenderCaptureRequest request = Request(directory);
                request.scene = scene.name; request.camera = camera.name;
                request.warmupFrames = 0;
                session = new RenderCaptureSession(request);
                session.PrepareCamera(camera, state, asset);
                var staging = session.Reserve("Lighting", "LifecycleFixture", new TextureDescriptor(1, 1)
                {
                    colorFormat = GraphicsFormat.R32_SFloat, dimension = TextureDimension.Tex2D
                }, false);
                staging.OnQueueFailed();
                session.Cancel();
                Assert.IsFalse(session.Finished);
                Assert.IsFalse(staging.evidence.retired);
                session.AfterSubmit();
                Assert.IsTrue(session.Finished);
                Assert.IsTrue(staging.evidence.retired);
                Assert.IsTrue(staging.evidence.submitted);
                Assert.AreEqual(0, staging.evidence.bytes);
                StringAssert.Contains("Uncertain queue drained", staging.evidence.error);
            }
            finally
            {
                AsyncGPUReadback.WaitAllRequests();
                session?.AfterSubmit();
                session?.Cancel();
                commands.Dispose(); state.Dispose();
                if (ownsVolumes) VolumeManager.instance.Deinitialize();
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(profile);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [UnityTest]
        public IEnumerator CounterReadback_PreservesNonzeroFailureAndRetiresAfterSubmit()
        {
            string directory = Path.Combine(Path.GetTempPath(), "InfinityCaptureTest-" + Guid.NewGuid().ToString("N"));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            scene.name = "CaptureLifecycle-" + Guid.NewGuid().ToString("N");
            var cameraObject = new GameObject("CaptureLifecycleCamera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(1, 1, 0);
            target.Create();
            camera.targetTexture = target;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            asset.qualityVolumeProfile = profile;
            bool ownsVolumes = !VolumeManager.instance.isInitialized;
            if (ownsVolumes) VolumeManager.instance.Initialize(null, null);
            var state = new CameraFrameState(43);
            var commands = new CommandBuffer();
            var source = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, 1, sizeof(uint));
            RenderCaptureSession session = null;
            try
            {
                RenderCaptureRequest request = Request(directory);
                request.scene = scene.name; request.camera = camera.name;
                request.warmupFrames = 0;
                session = new RenderCaptureSession(request);
                session.PrepareCamera(camera, state, asset);
                var staging = session.ReserveCounter("ZBinOverflow", source, "CounterFixture", "Graphics");
                commands.SetBufferData(source, new uint[] { 7 });
                commands.CopyBuffer(source, staging.buffer);
                commands.RequestAsyncReadback(staging.buffer, staging.Complete);
                staging.lifetime.recorded = true;
                session.Cancel();
                Assert.IsFalse(session.Finished);
                Assert.IsFalse(staging.evidence.retired);
                Graphics.ExecuteCommandBuffer(commands);
                session.AfterSubmit();
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!session.Finished && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    session.Pump();
                    yield return null;
                }
                Assert.IsTrue(session.Finished, "Readback did not reach terminal retirement.");
                Assert.IsTrue(staging.evidence.submitted);
                Assert.IsTrue(staging.evidence.readbackTerminal);
                Assert.IsTrue(staging.evidence.retired);
                Assert.AreEqual(7u, staging.evidence.counter);
                Assert.AreEqual(4, staging.evidence.bytes);
                Assert.AreEqual(7u, BitConverter.ToUInt32(File.ReadAllBytes(Path.Combine(directory, staging.evidence.file)), 0));
                StringAssert.Contains("ZBin overflow detected", File.ReadAllText(Path.Combine(directory, "capture.json")));
            }
            finally
            {
                AsyncGPUReadback.WaitAllRequests();
                session?.AfterSubmit();
                session?.Cancel();
                commands.Dispose(); source.Dispose(); state.Dispose();
                if (ownsVolumes) VolumeManager.instance.Deinitialize();
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(profile);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void CaptureReceipt_PreservesActualSourceDescriptor()
        {
            var original = new RenderCaptureSession.BufferEvidence
            {
                sourceDescriptor = new TextureDescriptor(17, 13)
                {
                    name = "SourceLighting", colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    dimension = TextureDimension.Tex2D, enableRandomWrite = true,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                }
            };
            string json = JsonUtility.ToJson(original);
            StringAssert.Contains("sourceDescriptor", json);
            var restored = JsonUtility.FromJson<RenderCaptureSession.BufferEvidence>(json);
            Assert.AreEqual(original.sourceDescriptor, restored.sourceDescriptor);
        }

        [Test]
        public void RecordedStaging_RetiresOnlyAfterSubmitAndReadbackTerminal()
        {
            var lifetime = new CaptureRetirement { recorded = true };
            Assert.Throws<InvalidOperationException>(() => lifetime.Retire());
            lifetime.terminal = true;
            Assert.Throws<InvalidOperationException>(() => lifetime.Retire());
            lifetime.submitted = true;
            Assert.DoesNotThrow(() => lifetime.Retire());
            Assert.Throws<InvalidOperationException>(() => lifetime.Retire());
        }
        [Test]
        public void CancelBeforeRecording_CanReleaseReservedStaging()
        {
            var lifetime = new CaptureRetirement();
            Assert.DoesNotThrow(() => lifetime.Retire());
        }
        [Test]
        public void RawStatistics_KeepNonFiniteOutsideRoiAsFailure()
        {
            byte[] bytes = new byte[8];
            Array.Copy(BitConverter.GetBytes(0.25f), 0, bytes, 0, 4);
            Array.Copy(BitConverter.GetBytes(float.NaN), 0, bytes, 4, 4);
            var stats = RawCaptureStatistics.Compute(bytes, GraphicsFormat.R32_SFloat, 2, 1, new RectInt(0, 0, 1, 1));
            Assert.AreEqual(1, stats.nonFinite);
            Assert.AreEqual(1, stats.roiSamples);
            Assert.AreEqual(0.25, stats.mean);
            Assert.IsTrue(float.IsNaN(BitConverter.ToSingle(bytes, 4)));
        }
        [Test]
        public void RawStatistics_HalfInfinityAndPackedFloatNaNAreDetected()
        {
            var half = RawCaptureStatistics.Compute(new byte[] { 0, 0x7c }, GraphicsFormat.R16_SFloat, 1, 1, new RectInt(0, 0, 1, 1));
            Assert.AreEqual(1, half.nonFinite);
            var packed = RawCaptureStatistics.Compute(BitConverter.GetBytes(0x7c1u), GraphicsFormat.B10G11R11_UFloatPack32, 1, 1, new RectInt(0, 0, 1, 1));
            Assert.AreEqual(1, packed.nonFinite);
        }
        [Test]
        public void RawStatistics_RejectTruncatedReadback()
        {
            Assert.Throws<InvalidOperationException>(() => RawCaptureStatistics.Compute(new byte[3], GraphicsFormat.R32_SFloat, 1, 1, new RectInt(0, 0, 1, 1)));
        }
    }
}
