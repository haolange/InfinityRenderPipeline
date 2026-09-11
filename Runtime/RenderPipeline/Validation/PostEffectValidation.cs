using System;
using System.Collections.Generic;
using System.IO;
using InfinityTech.Component;
using InfinityTech.Rendering.PostProcess;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    internal sealed class PostEffectRequest
    {
        public string outputDirectory, scene, camera;
        public bool pauseForVisual = true, stageDiagnostics = true;
        public int advance;
        public float timeoutSeconds = 1200;
    }

    internal static class PostEffectValidation
    {
        internal static PostEffectSession current;
        static bool s_ArgumentsRead;
        static string s_RequestFile;
        static DateTime s_RequestWrite;
        static int s_Advance;

        internal static void Start(PostEffectRequest request)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Post-effect validation requires Play or Player.");
            if ((current != null && !current.Finished) ||
                (RenderCaptureService.current != null && !RenderCaptureService.current.Finished) ||
                (RenderFaultValidation.current != null && !RenderFaultValidation.current.Finished))
                throw new InvalidOperationException("Existing validation must finish draining first.");
            current = new PostEffectSession(request);
            s_Advance = request.advance;
        }

        internal static void Tick(List<Camera> cameras)
        {
            if (!s_ArgumentsRead)
            {
                s_ArgumentsRead = true;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-infinityPostEffectRequest") s_RequestFile = Path.GetFullPath(args[i + 1]);
            }
            if (!string.IsNullOrEmpty(s_RequestFile) && File.Exists(s_RequestFile))
            {
                DateTime write = File.GetLastWriteTimeUtc(s_RequestFile);
                if (write != s_RequestWrite)
                {
                    s_RequestWrite = write;
                    var request = JsonUtility.FromJson<PostEffectRequest>(File.ReadAllText(s_RequestFile));
                    if (current == null || current.Finished) Start(request);
                    else if (request.outputDirectory == current.request.outputDirectory && request.advance > s_Advance)
                    {
                        current.Advance();
                        s_Advance = request.advance;
                    }
                }
            }
            current?.Pump(cameras);
        }
    }

    internal sealed class PostEffectSession
    {
        [Serializable] sealed class Phase
        {
            public string name, directory, status, settings;
            public int startedFrame, completedFrame;
        }
        [Serializable] sealed class Evidence
        {
            public string status = "WaitingForCamera", error, scene, camera, measurementSpace = "linear Rec709";
            public RectInt roi;
            public bool restored;
            public List<Phase> phases = new List<Phase>();
        }
        static readonly string[] s_Phases = { "Baseline", "Grain", "Vignette", "BloomTight", "BloomWide", "Restored" };
        internal readonly PostEffectRequest request;
        readonly Evidence m_Evidence = new Evidence();
        readonly double m_Started = Time.realtimeSinceStartupAsDouble;
        Camera m_Camera;
        GameObject m_Object;
        Volume m_Volume;
        VolumeProfile m_Profile;
        Bloom m_Bloom;
        Vignette m_Vignette;
        FilmGrain m_Grain;
        RenderCaptureSession m_Capture;
        int m_Phase = -1;
        bool m_Stopping, m_Initialized;
        public bool Finished { get; private set; }

        internal PostEffectSession(PostEffectRequest captureRequest)
        {
            request = captureRequest ?? throw new ArgumentNullException(nameof(captureRequest));
            if (string.IsNullOrEmpty(request.outputDirectory) || !Path.IsPathRooted(request.outputDirectory) ||
                Directory.Exists(request.outputDirectory) || string.IsNullOrEmpty(request.scene) || string.IsNullOrEmpty(request.camera))
                throw new ArgumentException("A new absolute evidence directory and explicit scene/camera are required.");
            Directory.CreateDirectory(request.outputDirectory);
            m_Evidence.scene = request.scene; m_Evidence.camera = request.camera;
            File.WriteAllText(Path.Combine(request.outputDirectory, "request.json"), JsonUtility.ToJson(request, true));
            Save();
        }

        internal void Pump(List<Camera> cameras = null)
        {
            if (Finished) return;
            try
            {
                if (!Application.isPlaying) Cancel("PlayModeEnded");
                if (!m_Stopping && Time.realtimeSinceStartupAsDouble - m_Started > request.timeoutSeconds) Cancel("Session timed out.");
                if (m_Stopping)
                {
                    m_Capture?.Pump();
                    if (m_Capture == null || m_Capture.Finished) Cleanup();
                    return;
                }
                if (m_Camera == null)
                {
                    if (m_Initialized) throw new InvalidOperationException("The fixed target camera was destroyed.");
                    if (cameras == null) return;
                    foreach (Camera camera in cameras)
                        if (camera.cameraType == CameraType.Game && camera.name == request.camera && camera.gameObject.scene.name == request.scene)
                        {
                            if (m_Camera != null) throw new InvalidOperationException("Camera identity is ambiguous.");
                            m_Camera = camera;
                        }
                    if (m_Camera == null) return;
                    Initialize();
                    m_Initialized = true;
                    NextPhase();
                }
                if (m_Evidence.status == "Capturing" && m_Capture.Finished)
                {
                    Phase phase = m_Evidence.phases[m_Phase];
                    phase.status = m_Capture.Status; phase.completedFrame = Time.frameCount;
                    if (phase.status != "Completed") { Cancel("Capture failed: " + phase.name); return; }
                    m_Evidence.status = "AwaitingVisual";
                    Save();
                    if (!request.pauseForVisual || m_Phase == s_Phases.Length - 1) Advance();
                }
            }
            catch (Exception error)
            {
                Cancel(error.ToString());
                Debug.LogException(error);
            }
        }

        void Initialize()
        {
            int mask = m_Camera.TryGetComponent(out InfinityAdditionalCameraData component) ? component.volumeLayerMask.value : -1;
            if (mask == 0) throw new InvalidOperationException("Target camera excludes all Volume layers.");
            int layer = 0;
            while ((mask & (1 << layer)) == 0) layer++;
            m_Object = new GameObject("Infinity Post-Effect Validation") { hideFlags = HideFlags.DontSave, layer = layer };
            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.hideFlags = HideFlags.DontSave;
            m_Bloom = m_Profile.Add<Bloom>(true);
            m_Vignette = m_Profile.Add<Vignette>(true);
            m_Grain = m_Profile.Add<FilmGrain>(true);
            m_Volume = m_Object.AddComponent<Volume>();
            m_Volume.isGlobal = true; m_Volume.priority = float.MaxValue; m_Volume.weight = 1;
            m_Volume.sharedProfile = m_Profile;
            // Fixed before capture: architectural region clear of the central animated figure.
            m_Evidence.roi = new RectInt((int)(m_Camera.pixelWidth * 0.05f), (int)(m_Camera.pixelHeight * 0.35f),
                Math.Max(1, (int)(m_Camera.pixelWidth * 0.2f)), Math.Max(1, (int)(m_Camera.pixelHeight * 0.25f)));
        }

        void NextPhase()
        {
            m_Phase++;
            if (m_Phase == s_Phases.Length) { m_Evidence.status = "Completed"; m_Stopping = true; Cleanup(); return; }
            m_Grain.intensity.value = m_Phase == 1 ? 0.6f : 0;
            m_Vignette.intensity.value = m_Phase == 2 ? 0.8f : 0;
            m_Bloom.intensity.value = m_Phase == 3 || m_Phase == 4 ? 2 : 0;
            m_Bloom.threshold.value = 0.4f;
            m_Bloom.scatter.value = m_Phase == 4 ? 0.9f : 0.1f;
            m_Volume.enabled = m_Phase != 5;
            string directory = Path.Combine(request.outputDirectory, m_Phase + "-" + s_Phases[m_Phase]);
            bool diagnose = request.stageDiagnostics && (m_Phase == 0 || m_Phase == 5);
            RenderCaptureService.Start(new RenderCaptureRequest
            {
                outputDirectory = directory, fixture = "PostEffect-" + s_Phases[m_Phase], scene = request.scene, camera = request.camera,
                width = m_Camera.pixelWidth, height = m_Camera.pixelHeight, warmupFrames = 120, frameCount = 3,
                timeoutSeconds = 180, includeConfidence = true, roi = m_Evidence.roi,
                buffers = diagnose ? new[] { "Lighting", "GBufferA", "GBufferB", "GBufferC", "Occlusion", "SSR", "SSGI", "SceneColor", "AntiAliasing", "PostProcess", "DisplayColor" } :
                    new[] { "Lighting", "PostProcess", "DisplayColor" }
            });
            m_Capture = RenderCaptureService.current;
            m_Evidence.phases.Add(new Phase { name = s_Phases[m_Phase], directory = directory, status = "Capturing", startedFrame = Time.frameCount,
                settings = m_Volume.enabled ? JsonUtility.ToJson(m_Bloom) + "\n" + JsonUtility.ToJson(m_Vignette) + "\n" + JsonUtility.ToJson(m_Grain) : "Original scene Volumes; temporary override disabled." });
            m_Evidence.status = "Capturing";
            Save();
        }

        internal void Advance()
        {
            if (m_Evidence.status != "AwaitingVisual") throw new InvalidOperationException("A completed phase is required before advancing.");
            NextPhase();
        }
        internal void Cancel(string reason = "Cancelled")
        {
            if (Finished || m_Stopping) return;
            m_Evidence.status = "Failed"; m_Evidence.error = reason; m_Stopping = true;
            m_Capture?.Cancel(reason);
            if (m_Volume != null) m_Volume.enabled = false;
            Save();
        }
        void Cleanup()
        {
            if (m_Volume != null) m_Volume.enabled = false;
            if (m_Object != null) DestroyOwned(m_Object);
            if (m_Profile != null)
            {
                foreach (VolumeComponent component in m_Profile.components) DestroyOwned(component);
                DestroyOwned(m_Profile);
            }
            m_Evidence.restored = true; Finished = true; Save();
        }
        static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        void Save() => File.WriteAllText(Path.Combine(request.outputDirectory, "session.json"), JsonUtility.ToJson(m_Evidence, true));
    }
}
