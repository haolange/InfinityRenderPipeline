using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Pipeline
{
    internal enum ERenderFaultPoint { BeforePassQueue, AfterPassQueue, AfterSubmit }

    [Serializable]
    internal sealed class RenderFaultRequest
    {
        public string outputDirectory, scene, camera;
        public float timeoutSeconds = 600;
    }

    internal static class RenderFaultValidation
    {
        internal static RenderFaultSession current;
        static bool s_ReadArguments;
        static string s_RequestFile;
        static DateTime s_RequestWrite;

        internal static void Start(RenderFaultRequest request)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Fault validation requires an already running Player or Editor Play session.");
            if (current != null && !current.Finished) throw new InvalidOperationException("An existing fault session must finish cleanup first.");
            if (RenderCaptureService.current != null && !RenderCaptureService.current.Finished)
                throw new InvalidOperationException("An existing capture must drain first.");
            current = new RenderFaultSession(request);
        }

        internal static void Tick(List<Camera> cameras)
        {
            if (!s_ReadArguments)
            {
                s_ReadArguments = true;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-infinityFaultRequest") s_RequestFile = Path.GetFullPath(args[i + 1]);
            }
            if (!string.IsNullOrEmpty(s_RequestFile) && File.Exists(s_RequestFile) && (current == null || current.Finished))
            {
                DateTime write = File.GetLastWriteTimeUtc(s_RequestFile);
                if (write != s_RequestWrite)
                {
                    s_RequestWrite = write;
                    Start(JsonUtility.FromJson<RenderFaultRequest>(File.ReadAllText(s_RequestFile)));
                }
            }
            current?.BeginFrame(cameras);
        }

        internal static void BeforeCamera(Camera camera, CameraFrameState state) => current?.BeforeCamera(camera, state);
        internal static void CameraResult(Camera camera, bool success, Exception error = null) => current?.CameraResult(camera, success, error);
        internal static void AtPass(ERenderFaultPoint point, string pass) => current?.AtPoint(point, pass);
        internal static void AfterSubmit() => current?.AfterSubmit();
    }

    internal sealed class RenderFaultSession
    {
        [Serializable] sealed class CameraEvidence
        {
            public string name;
            public int id, generationBefore, generationAfter, retired;
            public bool executed, reset, retiredAlive;
            public Vector3 position;
            public Matrix4x4 cameraWorldToView, uniformWorldToView;
        }
        [Serializable] sealed class FrameEvidence
        {
            public int frame, graphRetired, lightRetired, captureOutstanding;
            public bool submitted, nativeSubmitReached, originalException, asyncJoined;
            public string error;
            public List<CameraEvidence> cameras = new List<CameraEvidence>();
        }
        [Serializable] sealed class CaseEvidence
        {
            public string name, point, camera, pass, status = "Warming", faultCapture, recoveryCapture, error;
            public FrameEvidence faultFrame;
            public List<FrameEvidence> recoveryFrames = new List<FrameEvidence>();
        }
        [Serializable] sealed class Evidence
        {
            public string status = "WaitingForCameras", error, unity, platform, api, gpu, scene, primary, secondary;
            public bool temporaryCameraDestroyed, temporaryCameraStateRecycled;
            public Vector3 primaryPosition, secondaryPosition;
            public Quaternion cameraRotation;
            public Vector2Int cameraSize;
            public List<CaseEvidence> cases = new List<CaseEvidence>();
        }
        sealed class InjectedFault : Exception { internal InjectedFault(string message) : base(message) { } }
        sealed class CameraObservation
        {
            public Camera camera;
            public CameraFrameState state;
            public int generation;
            public bool executed;
        }

        readonly RenderFaultRequest m_Request;
        readonly Evidence m_Evidence;
        readonly Dictionary<int, CameraObservation> m_Cameras = new Dictionary<int, CameraObservation>();
        readonly double m_Started;
        Camera m_Primary, m_Secondary, m_CurrentCamera;
        int m_SecondaryId, m_Index, m_ArmedFrame = -1, m_FaultFrame = -1, m_CleanupFrame = -1;
        bool m_Ready, m_Fired, m_Recovery, m_NativeSubmitReached, m_Failed;
        InjectedFault m_Injected;
        internal bool Finished { get; private set; }
        internal string Status => m_Evidence.status;

        internal RenderFaultSession(RenderFaultRequest request)
        {
            if (request == null || !Path.IsPathRooted(request.outputDirectory ?? "") || Directory.Exists(request.outputDirectory) ||
                string.IsNullOrWhiteSpace(request.scene) || string.IsNullOrWhiteSpace(request.camera) || request.timeoutSeconds <= 0)
                throw new ArgumentException("Fault validation requires fixed scene/camera and a new absolute evidence directory.");
            m_Request = JsonUtility.FromJson<RenderFaultRequest>(JsonUtility.ToJson(request));
            Directory.CreateDirectory(m_Request.outputDirectory);
            File.WriteAllText(Path.Combine(m_Request.outputDirectory, "request.json"), JsonUtility.ToJson(m_Request, true));
            m_Started = Time.realtimeSinceStartupAsDouble;
            m_Evidence = new Evidence { unity = Application.unityVersion, platform = Application.platform.ToString(),
                api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName, scene = request.scene, primary = request.camera };
            Save();
        }

        internal void BeginFrame(List<Camera> cameras)
        {
            if (Finished) return;
            m_Cameras.Clear(); m_Ready = false; m_NativeSubmitReached = false;
            if (Time.realtimeSinceStartupAsDouble - m_Started > m_Request.timeoutSeconds) Fail("Fault suite timed out.");
            if (m_CleanupFrame >= 0) return;
            if (m_Primary == null)
            {
                foreach (Camera camera in cameras)
                    if (camera.cameraType == CameraType.Game && camera.name == m_Request.camera && camera.gameObject.scene.name == m_Request.scene)
                    {
                        if (m_Primary != null) { Fail("Ambiguous primary camera."); return; }
                        m_Primary = camera;
                    }
                if (m_Primary == null) return;
                var clone = new GameObject("InfinityFaultSecondary") { hideFlags = HideFlags.DontSave };
                SceneManager.MoveGameObjectToScene(clone, m_Primary.gameObject.scene);
                m_Secondary = clone.AddComponent<Camera>();
                m_Secondary.CopyFrom(m_Primary);
                clone.transform.SetPositionAndRotation(m_Primary.transform.position + m_Primary.transform.right * 0.35f, m_Primary.transform.rotation);
                m_Secondary.ResetWorldToCameraMatrix();
                m_Evidence.primaryPosition = m_Primary.transform.position;
                m_Evidence.secondaryPosition = clone.transform.position;
                m_Evidence.cameraRotation = clone.transform.rotation;
                m_Evidence.cameraSize = new Vector2Int(m_Primary.pixelWidth, m_Primary.pixelHeight);
                if (m_Primary.worldToCameraMatrix == m_Secondary.worldToCameraMatrix)
                { Fail("The fixed dual-camera fixture has identical view matrices."); return; }
                m_Secondary.depth = m_Primary.depth + 1;
                if (m_Primary.TryGetComponent(out CameraComponent component))
                {
                    var copy = clone.AddComponent<CameraComponent>();
                    copy.volumeLayerMask = component.volumeLayerMask; copy.volumeTrigger = component.volumeTrigger;
                }
                m_SecondaryId = m_Secondary.GetHashCode();
                m_Evidence.secondary = m_Secondary.name;
                AddCase("FirstCameraRecordingFailure", ERenderFaultPoint.BeforePassQueue, m_Primary.name, "CaptureNormalFrame");
                AddCase("SecondCameraRecordingFailure", ERenderFaultPoint.BeforePassQueue, m_Secondary.name, "CaptureNormalFrame");
                AddCase("AcceptedGraphicsQueueFailure", ERenderFaultPoint.AfterPassQueue, m_Secondary.name, "CaptureNormalFrame");
                AddCase("AcceptedAsyncQueueFailure", ERenderFaultPoint.AfterPassQueue, m_Secondary.name, "ComputeHiZ");
                AddCase("AfterNativeSubmitFailure", ERenderFaultPoint.AfterSubmit, m_Secondary.name, "");
                Save();
                return;
            }
            m_Ready = cameras.Contains(m_Primary) && cameras.Contains(m_Secondary);
            if (!m_Ready) return;
            if (m_Index >= m_Evidence.cases.Count) { BeginCleanup(); return; }
            CaseEvidence active = m_Evidence.cases[m_Index];
            if (string.IsNullOrEmpty(active.faultCapture)) StartCapture(active, false);
            else if (!m_Fired && RenderCaptureService.current.Finished)
            {
                Fail("Fault target did not execute before capture finished: " + active.name);
            }
            else if (m_Fired && RenderCaptureService.current.Finished)
            {
                if (!m_Recovery)
                {
                    if (RenderCaptureService.current.Status != "Failed") { Fail("Injected fault capture did not fail."); return; }
                    StartCapture(active, true);
                }
                else
                {
                    if (RenderCaptureService.current.Status != "Completed") { Fail("Recovery capture did not complete."); return; }
                    active.status = "Passed";
                    m_Index++; m_Fired = false; m_Recovery = false; m_Injected = null;
                    if (m_Index >= m_Evidence.cases.Count) BeginCleanup();
                    Save();
                }
            }
        }

        void AddCase(string name, ERenderFaultPoint point, string camera, string pass)
            => m_Evidence.cases.Add(new CaseEvidence { name = name, point = point.ToString(), camera = camera, pass = pass });

        void StartCapture(CaseEvidence active, bool recovery)
        {
            Camera camera = active.camera == m_Primary.name ? m_Primary : m_Secondary;
            string path = Path.Combine(m_Request.outputDirectory, active.name + (recovery ? "-recovery" : "-fault"));
            RenderCaptureService.Start(new RenderCaptureRequest { outputDirectory = path, fixture = active.name,
                scene = m_Request.scene, camera = camera.name, width = camera.pixelWidth, height = camera.pixelHeight,
                warmupFrames = 120, frameCount = 3, includeConfidence = true,
                roi = new RectInt(0, 0, camera.pixelWidth, camera.pixelHeight) });
            if (recovery) active.recoveryCapture = path; else active.faultCapture = path;
            m_Recovery = recovery; active.status = recovery ? "Recovering" : "Warming";
            m_Evidence.status = active.status;
            Save();
        }

        internal void BeforeCamera(Camera camera, CameraFrameState state)
        {
            m_CurrentCamera = camera;
            if (Finished || !m_Ready || m_CleanupFrame >= 0) return;
            if (camera != m_Primary && camera != m_Secondary) return;
            m_Cameras[state.cameraId] = new CameraObservation { camera = camera, state = state,
                generation = state.historyCache.TextureGeneration(AntiAliasingUtilityData.HistoryColorTextureID) };
            if (RenderCaptureService.current.CaptureThisFrame && camera.name == m_Evidence.cases[m_Index].camera)
                m_ArmedFrame = Time.frameCount;
        }

        internal void CameraResult(Camera camera, bool success, Exception error)
        {
            if (Finished || !m_Ready || m_CleanupFrame >= 0) return;
            if (m_Cameras.TryGetValue(camera.GetHashCode(), out CameraObservation observation)) observation.executed = success;
            if (error != null && !ReferenceEquals(error, m_Injected)) Fail("Unexpected camera exception: " + error);
        }

        internal void AtPoint(ERenderFaultPoint point, string pass)
        {
            if (Finished || !m_Ready || m_CleanupFrame >= 0 || m_Recovery || m_Fired || m_ArmedFrame != Time.frameCount) return;
            CaseEvidence active = m_Evidence.cases[m_Index];
            if (active.point != point.ToString() || active.pass != pass || (point != ERenderFaultPoint.AfterSubmit && m_CurrentCamera.name != active.camera)) return;
            m_Fired = true; m_FaultFrame = Time.frameCount; active.status = "Injected";
            m_Injected = new InjectedFault("InfinityRP controlled fault: " + active.name);
            Save();
            throw m_Injected;
        }

        internal void AfterSubmit()
        {
            m_NativeSubmitReached = true;
            AtPoint(ERenderFaultPoint.AfterSubmit, "");
        }

        internal void EndFrame(Dictionary<int, CameraFrameState> states, bool submitted, Exception error,
            int graphRetired, int lightRetired, bool asyncJoined)
        {
            if (Finished) return;
            if (m_CleanupFrame >= 0)
            {
                if (Time.frameCount - m_CleanupFrame > 10 && (RenderCaptureService.current == null || RenderCaptureService.current.Finished))
                {
                    m_Evidence.temporaryCameraStateRecycled = !states.ContainsKey(m_SecondaryId);
                    if (!m_Evidence.temporaryCameraStateRecycled) { m_Failed = true; m_Evidence.error = "Temporary camera state was not recycled."; }
                    m_Evidence.status = m_Failed ? "Failed" : "Completed";
                    Finished = true; Save();
                }
                return;
            }
            if (!m_Ready || m_Index >= m_Evidence.cases.Count) return;
            if (error != null && !ReferenceEquals(error, m_Injected)) { Fail("Unexpected frame exception: " + error); return; }
            if (!m_Fired) return;
            var frame = new FrameEvidence { frame = Time.frameCount, submitted = submitted, nativeSubmitReached = m_NativeSubmitReached,
                originalException = ReferenceEquals(error, m_Injected), error = error?.ToString(), graphRetired = graphRetired,
                lightRetired = lightRetired, asyncJoined = asyncJoined, captureOutstanding = RenderCaptureService.current.OutstandingCount };
            foreach (CameraObservation observation in m_Cameras.Values)
                frame.cameras.Add(new CameraEvidence { name = observation.camera.name, id = observation.state.cameraId,
                    generationBefore = observation.generation, generationAfter = observation.state.historyCache.TextureGeneration(AntiAliasingUtilityData.HistoryColorTextureID),
                    executed = observation.executed, reset = observation.state.requiresHistoryReset,
                    retired = observation.state.historyCache.RetiredQueuedCount, retiredAlive = observation.state.historyCache.RetiredResourcesAreAlive,
                    position = observation.camera.transform.position, cameraWorldToView = observation.camera.worldToCameraMatrix,
                    uniformWorldToView = observation.state.cameraUniform.matrix_WorldToView });
            CaseEvidence active = m_Evidence.cases[m_Index];
            if (Time.frameCount == m_FaultFrame)
            {
                active.faultFrame = frame;
                if (!frame.originalException || frame.cameras.Count != 2 || !asyncJoined) { Fail("Fault frame identity/camera/async gate failed."); return; }
                bool submitFault = active.point == ERenderFaultPoint.AfterSubmit.ToString();
                foreach (CameraEvidence camera in frame.cameras)
                {
                    bool shouldCommit = !submitFault && camera.name != active.camera;
                    if (camera.generationAfter - camera.generationBefore != (shouldCommit ? 1 : 0) || camera.reset == shouldCommit || !camera.retiredAlive)
                    { Fail("Camera commit/reset/retirement mismatch: " + camera.name); return; }
                }
                if (submitFault && (frame.submitted || !frame.nativeSubmitReached || frame.captureOutstanding == 0))
                { Fail("Submit fault did not preserve pending GPU ownership."); return; }
                Save();
            }
            else if (active.recoveryFrames.Count < 8)
            {
                foreach (CameraEvidence camera in frame.cameras)
                    if (!camera.executed || camera.reset || camera.generationAfter - camera.generationBefore != 1)
                    { Fail("Recovery did not resume committed history for " + camera.name); return; }
                if (!frame.submitted || !asyncJoined) { Fail("Recovery submission did not complete."); return; }
                active.recoveryFrames.Add(frame); Save();
            }
        }

        internal void Cancel(bool playStopped = false)
        {
            Fail("Fault suite cancelled.");
            if (!playStopped) return;
            if (m_Secondary != null) UnityEngine.Object.DestroyImmediate(m_Secondary.gameObject);
            RenderCaptureService.current?.Pump();
            if (RenderCaptureService.current == null || RenderCaptureService.current.Finished)
            {
                m_Evidence.status = "Cancelled";
                Finished = true; Save();
            }
        }

        void Fail(string error)
        {
            if (Finished || m_Failed) return;
            m_Failed = true; m_Evidence.status = "Failed"; m_Evidence.error = error;
            RenderCaptureService.current?.Cancel("FaultSuiteFailed");
            BeginCleanup(); Save();
        }
        void BeginCleanup()
        {
            if (m_CleanupFrame >= 0) return;
            m_CleanupFrame = Time.frameCount;
            if (m_Secondary != null)
            {
                m_Secondary.enabled = false;
                UnityEngine.Object.Destroy(m_Secondary.gameObject);
            }
            m_Evidence.temporaryCameraDestroyed = true;
        }
        void Save() => File.WriteAllText(Path.Combine(m_Request.outputDirectory, "fault-suite.json"), JsonUtility.ToJson(m_Evidence, true));
    }
}
