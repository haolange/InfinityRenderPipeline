using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InfinityTech.Component;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable] internal sealed class CameraMotionRequest
    {
        public string outputDirectory, scene, camera, cameraEntity;
        public CameraType cameraType = CameraType.Game;
        public int samples = 16;
        public bool temporalStages = true;
    }
    internal static class CameraMotionValidation
    {
        internal static CameraMotionSession current;
        static bool s_ArgumentsRead;
        internal static void Start(CameraMotionRequest request)
        {
            if (request == null || (request.cameraType == CameraType.Game && !Application.isPlaying) ||
                (current != null && !current.Finished) || (RenderCaptureService.current != null && !RenderCaptureService.current.Finished) ||
                (VisualLightingValidation.current != null && !VisualLightingValidation.current.Finished))
                throw new InvalidOperationException("Motion validation requires the selected live view and no other active capture.");
            current = new CameraMotionSession(request);
        }
        internal static void Tick()
        {
            if (!s_ArgumentsRead)
            {
                s_ArgumentsRead = true;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-infinityCameraMotionRequest") Start(JsonUtility.FromJson<CameraMotionRequest>(File.ReadAllText(args[i + 1])));
            }
#if UNITY_EDITOR
            // Editor lifecycle transitions belong to the CLI update pump, never SceneView.OnGUI.
            current?.PrepareRender();
#else
            current?.Tick();
#endif
        }
    }
    internal sealed class CameraMotionSession
    {
        [Serializable] internal sealed class Evidence
        {
            public string status = "Ready", error, directory, cameraType, phaseName;
            public int phase;
            public bool restored;
            public List<string> captures = new List<string>();
        }
        static readonly string[] s_Phases = { "Still", "MotionSequence", "Disocclusion", "Resize", "CameraSwitch", "Return", "Sparse" };
        readonly CameraMotionRequest m_Request;
        readonly Camera m_Original;
        readonly Vector3 m_Position;
        readonly Quaternion m_Rotation;
        readonly Rect m_Rect;
        readonly bool m_Enabled;
        readonly Evidence m_Evidence;
        Camera m_Clone;
        RenderCaptureSession m_Capture;
        bool m_Stopping;
        double m_LastRepaint;
        Vector2Int m_PreviousSize;
        int m_LastPoseStep = -1;
        float m_Angle;
#if UNITY_EDITOR
        readonly SceneView m_View;
        SceneView m_CloneView;
        readonly Vector3 m_Pivot;
        readonly Quaternion m_ViewRotation;
        readonly float m_ViewSize;
        readonly bool m_Orthographic;
        readonly Rect m_ViewRect;
        readonly SceneViewDockSnapshot m_DockSnapshot;
#endif
        public bool Finished { get; private set; }
        internal object State => m_Evidence;
        internal string CurrentPhase => Finished ? "Finished" : m_Evidence.phase == 1
            ? new[] { "SlowRotate", "FastRotate", "Stop", "Reverse" }[Math.Min((m_Capture?.CapturedFrames ?? 0) / m_Request.samples, 3)]
            : s_Phases[Math.Min(m_Evidence.phase, s_Phases.Length - 1)];
        internal CameraMotionSession(CameraMotionRequest request)
        {
            if (!Path.IsPathRooted(request.outputDirectory) || Directory.Exists(request.outputDirectory) || request.samples < 2 || request.samples > 32)
                throw new ArgumentException("Use a new absolute evidence directory and 2..32 samples.");
            m_Request = request;
            var candidates = Camera.allCameras.AsEnumerable();
#if UNITY_EDITOR
            candidates = candidates.Concat(SceneView.GetAllSceneCameras()).Distinct();
#endif
            m_Original = candidates.Single(c => c.cameraType == request.cameraType &&
                (string.IsNullOrEmpty(request.cameraEntity) ? c.name == request.camera && (request.cameraType == CameraType.SceneView || c.gameObject.scene.name == request.scene) : c.GetEntityId().ToString() == request.cameraEntity));
            m_Position = m_Original.transform.position; m_Rotation = m_Original.transform.rotation; m_Rect = m_Original.rect; m_Enabled = m_Original.enabled;
#if UNITY_EDITOR
            if (request.cameraType == CameraType.SceneView)
            {
                m_View = SceneView.sceneViews.Cast<SceneView>().Single(v => v.camera == m_Original);
                m_DockSnapshot = new SceneViewDockSnapshot(m_View);
                m_Pivot = m_View.pivot; m_ViewRotation = m_View.rotation; m_ViewSize = m_View.size; m_Orthographic = m_View.orthographic; m_ViewRect = m_View.position;
            }
#endif
            m_Evidence = new Evidence { directory = request.outputDirectory, cameraType = request.cameraType.ToString() };
            Directory.CreateDirectory(request.outputDirectory); Save();
        }
        Camera ActiveCamera => m_Clone != null && m_Evidence.phase == 4 ? m_Clone : m_Original;
        internal void Tick()
        {
            if (Finished) return;
            try
            {
                if (m_Request.cameraType == CameraType.Game && !Application.isPlaying) Cancel("PlayModeEnded");
                m_Capture?.Pump();
                if (m_Stopping) { if (m_Capture == null || m_Capture.Finished) Restore(); return; }
                if (m_Original == null) throw new InvalidOperationException("Selected camera destroyed.");
                if (m_Capture != null && m_Capture.Finished)
                {
                    if (m_Capture.Status != "Completed") { Cancel("Capture failed: " + m_Capture.Status); return; }
                    m_Evidence.phase++; m_Capture = null; m_LastPoseStep = -1;
                }
                if (m_Evidence.phase == s_Phases.Length) { Restore(); return; }
                if (m_Capture == null)
                {
                    SetupPhase();
#if UNITY_EDITOR
                    if (m_CloneView != null && m_Evidence.phase == 4) m_Clone = m_CloneView.camera;
#endif
                    BeginCapture();
                }
                PrepareRender();
                if (m_Evidence.phaseName != CurrentPhase) { m_Evidence.phaseName = CurrentPhase; Save(); }
                Repaint(false);
            }
            catch (Exception error) { Cancel(error.ToString()); }
        }
        internal void PrepareRender()
        {
            if (Finished || m_Stopping || m_Capture == null) return;
            int step = m_Capture.CapturedFrames;
            if (step != m_LastPoseStep && m_Evidence.phase == 1 && step < m_Request.samples * 4)
            {
                int segment = step / m_Request.samples;
                float delta = segment == 0 ? 0.12f : segment == 1 ? 1f : segment == 3 ? -1f : 0;
                if (delta != 0) { m_Angle += delta; SetPose(m_Angle, Vector3.zero); }
                m_LastPoseStep = step;
            }
        }

        void SetPose(float angle, Vector3 offset)
        {
#if UNITY_EDITOR
            if (m_View != null) { m_View.LookAtDirect(m_Pivot + offset, m_ViewRotation * Quaternion.Euler(0, angle, 0), m_ViewSize); return; }
#endif
            m_Original.transform.SetPositionAndRotation(m_Position + offset, m_Rotation * Quaternion.Euler(0, angle, 0));
        }
        void SetupPhase()
        {
            int phase = m_Evidence.phase;
            m_PreviousSize = new Vector2Int(ActiveCamera.pixelWidth, ActiveCamera.pixelHeight);
            if (phase == 2) SetPose(m_Angle, m_Rotation * Vector3.right * 0.75f);
            if (phase == 3)
            {
#if UNITY_EDITOR
                if (m_View != null) SceneViewDockSnapshot.Resize(m_View, new Rect(m_ViewRect.x, m_ViewRect.y, m_ViewRect.width * .83f, m_ViewRect.height * .79f));
                else
#endif
                    m_Original.rect = new Rect(m_Rect.x, m_Rect.y, m_Rect.width * .83f, m_Rect.height * .79f);
            }
            if (phase == 4)
            {
#if UNITY_EDITOR
                if (m_View != null)
                {
                    m_CloneView = ScriptableObject.CreateInstance<SceneView>(); m_CloneView.titleContent = new GUIContent("Temporal Validation");
                    m_CloneView.Show(); m_CloneView.orthographic = m_Orthographic; m_CloneView.LookAtDirect(m_Pivot, m_ViewRotation, m_ViewSize);
                    return;
                }
#endif
                var go = new GameObject("MotionValidationCamera") { hideFlags = HideFlags.DontSave };
                SceneManager.MoveGameObjectToScene(go, m_Original.gameObject.scene);
                m_Clone = go.AddComponent<Camera>(); m_Clone.CopyFrom(m_Original); m_Clone.rect = m_Rect;
                m_Clone.transform.SetPositionAndRotation(m_Position, m_Rotation); go.AddComponent<InfinityAdditionalCameraData>();
                m_Original.enabled = false; m_Clone.enabled = true;
            }
            if (phase == 5)
            {
#if UNITY_EDITOR
                if (m_View != null)
                {
                    if (m_CloneView != null) { m_CloneView.Close(); m_CloneView = null; m_Clone = null; }
                    m_DockSnapshot.Restore(m_View); m_View.orthographic = m_Orthographic; m_View.LookAtDirect(m_Pivot, m_ViewRotation, m_ViewSize);
                    return;
                }
#endif
                if (m_Clone != null) m_Clone.enabled = false;
                m_Original.enabled = m_Enabled; m_Original.rect = m_Rect; SetPose(0, Vector3.zero);
            }
        }
        void Repaint(bool force)
        {
#if UNITY_EDITOR
            SceneView view = m_Evidence.phase == 4 ? m_CloneView : m_View;
            if (view == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && m_Evidence.phase == 6 && now - m_LastRepaint < 1) return;
            m_LastRepaint = now; view.Repaint();
#endif
        }
        void BeginCapture()
        {
            Camera camera = ActiveCamera;
            if (camera == null) throw new InvalidOperationException("The selected validation view has no camera.");
            string name = s_Phases[m_Evidence.phase], directory = Path.Combine(m_Evidence.directory, name);
            string[] buffers = m_Request.temporalStages
                ? new[] { "DisplayColor", "TAAInput", "TAAHistoryColor", "TAAHistoryDepth", "TAAAccumulation", "TAADepth", "TAAReprojection", "AntiAliasing", "Motion", "MotionMetadata", "Depth" }
                : new[] { "DisplayColor", "SceneColor", "AntiAliasing", "Motion", "Depth" };
            RenderCaptureService.Start(new RenderCaptureRequest { outputDirectory = directory, fixture = "CameraMotion-" + name,
                scene = m_Request.scene, camera = camera.name, cameraEntity = camera.GetEntityId().ToString(), cameraType = m_Request.cameraType,
                useFirstFrameDimensions = m_Evidence.phase == 3 || m_Evidence.phase == 4 || m_Evidence.phase == 5, waitForSizeChangeFrom = m_Evidence.phase == 3 || m_Evidence.phase == 5 ? m_PreviousSize : Vector2Int.zero,
                width = camera.pixelWidth, height = camera.pixelHeight, warmupFrames = m_Evidence.phase == 0 || m_Evidence.phase == 5 ? 96 : 0,
                frameCount = m_Evidence.phase == 1 ? m_Request.samples * 4 : m_Evidence.phase == 3 || m_Evidence.phase == 4 ? 3 : m_Request.samples, timeoutSeconds = 180,
                buffers = buffers, roi = new RectInt(0, 0, camera.pixelWidth, camera.pixelHeight), includeConfidence = true });
            m_Capture = RenderCaptureService.current; m_Evidence.captures.Add(directory); m_Evidence.status = "Capturing"; Save();
        }
        internal void Cancel(string reason = "Cancelled")
        {
            if (Finished || m_Stopping) return;
            m_Stopping = true; m_Evidence.error = reason; m_Evidence.status = "Cancelling";
            m_Capture?.Cancel(reason); Save();
        }
        void Restore()
        {
#if UNITY_EDITOR
            if (m_View != null)
            {
                if (m_CloneView != null) m_CloneView.Close();
                m_DockSnapshot.Restore(m_View); m_View.orthographic = m_Orthographic; m_View.LookAtDirect(m_Pivot, m_ViewRotation, m_ViewSize);
            }
            else
#endif
            {
                if (m_Original != null) { m_Original.enabled = m_Enabled; m_Original.rect = m_Rect; m_Original.transform.SetPositionAndRotation(m_Position, m_Rotation); }
                if (m_Clone != null) UnityEngine.Object.Destroy(m_Clone.gameObject);
            }
            m_Evidence.restored = m_Original != null;
#if UNITY_EDITOR
            if (m_View != null) m_Evidence.restored &= m_DockSnapshot.Matches(m_View);
#endif
            m_Evidence.status = string.IsNullOrEmpty(m_Evidence.error) ? "Completed" : "Failed";
            Finished = true; Save();
        }
        void Save() => File.WriteAllText(Path.Combine(m_Evidence.directory, "camera-motion.json"), JsonUtility.ToJson(m_Evidence, true));
    }
}
