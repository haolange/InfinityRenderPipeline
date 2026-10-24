using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshOcclusionIntegrationTests
    {
        MeshWorld m_World;
        MeshScene m_MeshScene;
        MeshInstanceId m_Instance;
        EMeshBackendPolicy? m_PreviousBackend;
        Scene m_PreviousScene;
        GameObject m_Root;
        Camera m_Camera;
        GameObject m_Occluder;
        Material m_NativeMaterial, m_MeshMaterial;
        RenderTexture m_Target;
        EditorWindow m_GameView, m_PreviousFocus;
        Action<ScriptableRenderContext, List<Camera>> m_End;
        int m_Submits, m_Contexts, m_InitialInstances, m_InitialDraws;
        float m_InitialMatrixRatio;
        bool m_CaptureNext, m_Requested, m_Registered, m_PreviousCapture, m_PreviousPause, m_StateCaptured;
        bool m_EnteredPlayMode;
        bool m_PreviousRunBackground;
        List<AsyncGPUReadbackRequest> m_Readbacks;
        Exception m_CallbackFailure;
        int m_DepthWidth, m_DepthHeight;
        RGGraphSnapshot m_Snapshot;

        [UnityTest]
        public IEnumerator NativeOccluderMove_RecoversMeshAndActuallyWritesDepth()
        {
            if (!SystemInfo.supportsAsyncGPUReadback || !MeshDrawGPUBackend.SupportsIndirect)
                Assert.Ignore("The active API cannot run GPU indirect visibility/readback.");
            yield return new EnterPlayMode();
            m_EnteredPlayMode = true;
            m_PreviousRunBackground = Application.runInBackground;
            Application.runInBackground = true;
            var pipeline = RenderPipelineManager.currentPipeline as InfinityRenderPipeline;
            Assert.IsNotNull(pipeline);
            m_PreviousCapture = RGDebugSession.captureEnabled; m_PreviousPause = RGDebugSession.paused; m_PreviousFocus = EditorWindow.focusedWindow; m_StateCaptured = true;
            m_World = (MeshWorld)typeof(InfinityRenderPipeline).GetField("m_MeshWorld", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pipeline);
            m_PreviousBackend = (EMeshBackendPolicy?)typeof(MeshWorld).GetField("m_BackendOverride", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m_World);
            m_MeshScene = pipeline.renderContext.GetMeshScene();
            m_InitialInstances = m_MeshScene.LogicalInstanceCount; m_InitialDraws = m_MeshScene.DrawCount;
            m_InitialMatrixRatio = m_MeshScene.MatrixDuplicateRatio;
            m_World.SetBackendOverride(EMeshBackendPolicy.GpuIndirect);
            m_PreviousScene = SceneManager.GetActiveScene();
            m_Root = new GameObject("InfinityVisibility.Integration") { hideFlags = HideFlags.DontSave };
            m_Target = new RenderTexture(128, 128, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
            Assert.IsTrue(m_Target.Create());
            var cameraObject = new GameObject("InfinityVisibility.IntegrationCamera");
            cameraObject.transform.SetParent(m_Root.transform);
            m_Camera = cameraObject.AddComponent<Camera>();
            m_Camera.transform.SetPositionAndRotation(new Vector3(1000, 1000, 1000), Quaternion.identity);
            m_Camera.cullingMask = 1 << 31; m_Camera.nearClipPlane = .1f; m_Camera.farClipPlane = 30;
            m_Camera.fieldOfView = 60; m_Camera.aspect = 1; m_Camera.depth = 100; m_Camera.targetTexture = m_Target;
            m_Occluder = GameObject.CreatePrimitive(PrimitiveType.Cube); m_Occluder.name = "InfinityVisibility.NativeOccluder";
            m_Occluder.transform.SetParent(m_Root.transform);
            m_Occluder.layer = 31; m_Occluder.transform.position = new Vector3(1000, 1000, 1002); m_Occluder.transform.localScale = Vector3.one * 1.5f;
            var native = m_Occluder.GetComponent<Renderer>(); native.shadowCastingMode = ShadowCastingMode.Off;
            Assert.IsFalse(m_Occluder.TryGetComponent<MeshComponent>(out _), "Fixture occluder must use the native RendererList.");
            m_NativeMaterial = new Material(Shader.Find("InfinityPipeline/InfinityLit")); native.sharedMaterial = m_NativeMaterial;
            m_MeshMaterial = new Material(Shader.Find("InfinityPipeline/InfinityLit-Instanced")) { enableInstancing = true };
            // GBuffer cannot repair a missing depth recovery draw in this fixture.
            m_MeshMaterial.SetInt("_ZTest", (int)CompareFunction.Equal); m_MeshMaterial.SetInt("_ZWrite", 0);
            var mesh = m_Occluder.GetComponent<MeshFilter>().sharedMesh;
            using (var update = m_MeshScene.BeginUpdate())
            {
                var transform = update.CreateTransform(float4x4.TRS(new float3(1000,1000,1003), quaternion.identity, new float3(.5f)));
                m_Instance = update.CreateInstance(transform, new FBound(new float3(1000,1000,1003), new float3(.25f)),
                    1 << 31, 1, EMeshInstanceFlags.Visible | EMeshInstanceFlags.ReceiveShadow, EMotionType.Object, ECastShadowMethod.Off);
                update.CreateDraw(m_Instance, UnityEntityId.ToUInt64(mesh), 0, UnityEntityId.ToUInt64(m_MeshMaterial),
                    EPassEligibility.Depth | EPassEligibility.GBuffer | EPassEligibility.Motion, 2000, 0);
                update.Commit(); m_Registered = true;
            }
            SceneManager.SetActiveScene(m_PreviousScene);
            RGDebugSession.captureEnabled = true; RGDebugSession.paused = false;
            m_GameView = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().FullName == "UnityEditor.GameView");
            m_End = (context, cameras) =>
            {
                m_Contexts++;
                if (!cameras.Contains(m_Camera)) return;
                m_Submits++;
                if (!m_CaptureNext || m_Requested) return;
                m_Requested = true;
                try
                {
                    Assert.IsTrue(m_Camera.TryGetCullingParameters(out var parameters));
                    var view = MeshView.FromCamera(m_Camera, ref parameters);
                    var phases = new[] { EMeshVisibilityPhase.First, EMeshVisibilityPhase.Deferred, EMeshVisibilityPhase.Recovered, EMeshVisibilityPhase.Final };
                    m_Readbacks = new List<AsyncGPUReadbackRequest>(5);
                    for (int i = 0; i < phases.Length; ++i)
                    {
                        view.visibilityPhase = phases[i];
                        m_Readbacks.Add(AsyncGPUReadback.Request(m_World.GetGpuVisibilityBufferRef(view, m_MeshScene.InstanceHighWater).buffer));
                    }
                    var states = (Dictionary<int, CameraFrameState>)typeof(InfinityRenderPipeline).GetField("m_CameraStates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pipeline);
                    var state = states.Values.Single(s => s.camera == m_Camera);
                    Assert.IsTrue(state.acceptedHiZ);
                    Assert.IsTrue(state.historyCache.TryGetCommittedTexture(HiZPassUtilityData.HistoryFurthest, out var depth));
                    m_DepthWidth = depth.rt.width; m_DepthHeight = depth.rt.height;
                    m_Readbacks.Add(AsyncGPUReadback.Request(depth.rt, 0));
                    m_Snapshot = RGDebugSession.latest[m_Camera.name];
                }
                catch (Exception error) { m_CallbackFailure = error; }
            };
            RenderPipelineManager.endContextRendering += m_End;
            m_GameView?.Focus();
            yield return Wait(() => m_Submits >= 4, "Test camera did not finish four accepted warmup submissions.");
            m_Occluder.transform.position += Vector3.right * 10;
            m_CaptureNext = true;
            yield return Wait(() => m_Requested, "No camera submission after moving the native occluder.");
            if (m_CallbackFailure != null) throw m_CallbackFailure;
            yield return Wait(() => m_Readbacks.All(r => r.done), "Visibility/depth readbacks did not drain.");
            foreach (var readback in m_Readbacks) Assert.IsFalse(readback.hasError);
            int index = (int)m_Instance.Index;
            uint[] actual = m_Readbacks.Take(4).Select(r => r.GetData<uint>()[index]).ToArray();
            CollectionAssert.AreEqual(new uint[] {0,1,1,1}, actual, "Prediction, deferred, recovered and final lists must describe the same object.");
            float centerDepth = m_Readbacks[4].GetData<float>()[(m_DepthHeight / 2) * m_DepthWidth + m_DepthWidth / 2];
            var clip = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false) * m_Camera.worldToCameraMatrix * new Vector4(1000,1000,1002.75f,1);
            Assert.That(centerDepth, Is.EqualTo(clip.z / clip.w).Within(5e-4f), "Recovery must issue a real depth draw, not just update visibility/args.");
            var live = m_Snapshot.passes.Where(p => !p.culled).ToList();
            int early = live.FindIndex(p => p.name == "ComputeEarlyFurthestHiZ"), recovery = live.FindIndex(p => p.name == "RasterDepthRecovery");
            Assert.GreaterOrEqual(early, 0); Assert.Greater(recovery, early);
            Assert.Less(recovery, live.FindIndex(p => p.name == "RasterGBuffer"));
            Assert.IsFalse(live[early].asyncCompute, "Immediate depth recovery must not cross to async compute.");
            TestContext.WriteLine("Recovered mesh depth=" + centerDepth + "; object lists=" + string.Join(",", actual));
        }
        IEnumerator Wait(Func<bool> condition, string message)
        {
            double deadline = EditorApplication.timeSinceStartup + 20;
            while (!condition() && EditorApplication.timeSinceStartup < deadline)
            { m_GameView?.Repaint(); SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
            Assert.IsTrue(condition(), message);
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (!m_StateCaptured)
            {
                if (m_EnteredPlayMode)
                {
                    Application.runInBackground = m_PreviousRunBackground;
                    yield return new ExitPlayMode();
                }
                yield break;
            }
            if (m_Camera != null) m_Camera.enabled = false;
            if (m_Registered)
            {
                using (var update = m_MeshScene.BeginUpdate()) { update.RemoveInstance(m_Instance); update.Commit(); }
                m_Registered = false;
            }
            if (m_Occluder != null) m_Occluder.SetActive(false);
            if (m_End != null)
            {
                int next = m_Contexts + 2;
                yield return Wait(() => m_Contexts >= next, "Cleanup requires a source-scene submission after removing the temporary draw.");
            }
            if (m_Readbacks != null) yield return Wait(() => m_Readbacks.All(r => r.done), "Cleanup readbacks still in flight.");
            if (m_Target != null && m_Target.IsCreated())
            {
                var completion = AsyncGPUReadback.Request(m_Target);
                yield return Wait(() => completion.done, "Temporary target retirement did not finish.");
                Assert.IsFalse(completion.hasError);
            }
            if (m_Camera != null) UnityEngine.Object.DestroyImmediate(m_Camera.gameObject);
            if (m_Root != null) UnityEngine.Object.DestroyImmediate(m_Root);
            if (m_End != null)
            {
                int next = m_Contexts + 2;
                yield return Wait(() => m_Contexts >= next, "Temporary camera state retirement did not advance.");
                RenderPipelineManager.endContextRendering -= m_End; m_End = null;
            }
            if (m_Target != null) { m_Target.Release(); UnityEngine.Object.DestroyImmediate(m_Target); }
            if (m_NativeMaterial != null) UnityEngine.Object.DestroyImmediate(m_NativeMaterial);
            if (m_MeshMaterial != null) UnityEngine.Object.DestroyImmediate(m_MeshMaterial);
            if (m_World != null) m_World.SetBackendOverride(m_PreviousBackend);
            RGDebugSession.captureEnabled = m_PreviousCapture; RGDebugSession.paused = m_PreviousPause;
            if (!m_PreviousCapture) RGDebugSession.Clear();
            if (m_PreviousScene.IsValid() && m_PreviousScene.isLoaded) SceneManager.SetActiveScene(m_PreviousScene);
            if (m_PreviousFocus != null) m_PreviousFocus.Focus();
            int instances = m_MeshScene?.LogicalInstanceCount ?? m_InitialInstances;
            int draws = m_MeshScene?.DrawCount ?? m_InitialDraws;
            float ratio = m_MeshScene?.MatrixDuplicateRatio ?? m_InitialMatrixRatio;
            if (m_EnteredPlayMode)
            {
                Application.runInBackground = m_PreviousRunBackground;
                yield return new ExitPlayMode();
            }
            if (m_MeshScene != null)
            {
                Assert.AreEqual(m_InitialInstances, instances);
                Assert.AreEqual(m_InitialDraws, draws);
                Assert.AreEqual(m_InitialMatrixRatio, ratio);
            }
        }
    }
}
