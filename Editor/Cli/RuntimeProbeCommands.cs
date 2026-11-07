using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    // Request file for infinity_runtime_probe_start. Paths are absolute; the output directory must not exist yet.
    [Serializable]
    internal sealed class RuntimeProbeRequest
    {
        public string outputDirectory;
        public string camera;
        public string meshBackend = "";
        public int count = 1000;
        public float movingRatio = 1.0f;
        public float translucentRatio;
        public bool castShadow = true;
        public float spacing = 1.5f;
        public int warmupFrames = 32;
        public float[] windowsSeconds = { 5.0f, 20.0f, 60.0f };
    }

    [Serializable]
    internal sealed class RuntimeProbeStat
    {
        public double p50, p95, min, max, mean;
    }

    [Serializable]
    internal sealed class RuntimeProbeWindow
    {
        public float endSeconds;
        public int frames;
        public RuntimeProbeStat frameMs, proxyMs, transformMs, applyMs, frameAllocations, proxyAllocations;
        public long monoUsedBytes, monoHeapBytes, totalAllocatedBytes, totalReservedBytes, graphicsDriverBytes;
        public long poolTextureAllocatedBytes, poolTextureIdleBytes, poolTextureRetiredBytes, poolBufferAllocatedBytes, poolBufferIdleBytes, poolBufferRetiredBytes;
        public int gcGen0, gcGen1, gcGen2;
        public int meshEntities, meshElements;
        public string actualBackend;
    }

    [Serializable]
    internal sealed class RuntimeProbeEvidence
    {
        public string status = "Preparing", error, unity, api, scene, camera, requestedBackend;
        public RuntimeProbeRequest request;
        public int spawned, moving, translucent;
        public bool restored;
        public List<RuntimeProbeWindow> windows = new List<RuntimeProbeWindow>();
    }

    // Measurement and lifecycle. The MeshComponent authoring calls live in RuntimeProbeContent so this file stays identical
    // when the authoring API changes, which keeps baseline and candidate numbers comparable.
    internal static class RuntimeProbeCommands
    {
        static RuntimeProbeSession s_Session;

        [CliCommand("infinity_runtime_probe_start", "Spawn a bounded MeshComponent stress set in Play mode and record frame time, GC allocation, memory and pipeline-state windows; never saves assets.")]
        public static object Start(string requestPath)
        {
            if (s_Session != null && !s_Session.Finished) throw new InvalidOperationException("A runtime probe is already active.");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("The runtime probe requires Play mode and a noncompiling Editor.");
            if ((RenderCaptureService.current != null && !RenderCaptureService.current.Finished)
                || (VisualLightingValidation.current != null && !VisualLightingValidation.current.Finished)
                || (CameraMotionValidation.current != null && !CameraMotionValidation.current.Finished)
                || (PostEffectValidation.current != null && !PostEffectValidation.current.Finished)
                || (RenderFaultValidation.current != null && !RenderFaultValidation.current.Finished))
                throw new InvalidOperationException("Another validation session must finish before the probe starts.");
            var request = JsonUtility.FromJson<RuntimeProbeRequest>(File.ReadAllText(Path.GetFullPath(requestPath)));
            s_Session = new RuntimeProbeSession(request);
            return Status();
        }

        [CliCommand("infinity_runtime_probe_status", "Read runtime probe progress and its evidence directory.")]
        public static object Status() => s_Session == null
            ? (object)new { status = "Idle" }
            : new { status = s_Session.evidence.status, finished = s_Session.Finished, frames = s_Session.SampledFrames, windows = s_Session.evidence.windows.Count, error = s_Session.evidence.error, evidence = s_Session.evidence.request.outputDirectory };

        [CliCommand("infinity_runtime_probe_cancel", "Cancel the runtime probe, destroy spawned content and restore the measurement and backend overrides.")]
        public static object Cancel()
        {
            s_Session?.Stop("Cancelled");
            return Status();
        }
    }

    // Probe work runs on the Editor update, outside the pipeline's Render scope. The render callback only copies the
    // pipeline's counters, so the probe does not inflate the frames it measures.
    internal sealed class RuntimeProbeSession
    {
        internal readonly RuntimeProbeEvidence evidence;
        readonly RuntimeProbeRequest m_Request;
        readonly Camera m_Camera;
        readonly List<UnityEngine.Object> m_Owned = new List<UnityEngine.Object>();
        readonly List<Transform> m_Moving = new List<Transform>();
        readonly List<Vector3> m_BasePositions = new List<Vector3>();
        readonly bool m_PreviousMeasurement;
        readonly MeshPipeline.EMeshBackendPolicy? m_PreviousBackend;
        readonly double[] m_FrameMs, m_ProxyMs, m_TransformMs, m_ApplyMs, m_FrameAllocations, m_ProxyAllocations;
        readonly double[] m_Scratch;
        int m_Frame, m_WindowSamples, m_WindowIndex;
        double m_WindowsStarted;
        bool m_Sampling, m_HasPending, m_ReloadLocked;
        // Proxy sections of frame N are final at frame N's render callback, but the complete Render scope of frame N is final
        // only at frame N+1's callback. Hold frame N's proxy values until then so each sample describes one frame.
        double m_PendingProxyMs, m_PendingTransformMs, m_PendingApplyMs, m_PendingProxyAllocations;
        long m_PendingSequence;

        internal bool Finished { get; private set; }
        internal int SampledFrames { get; private set; }

        internal RuntimeProbeSession(RuntimeProbeRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.outputDirectory) || !Path.IsPathRooted(request.outputDirectory) || Directory.Exists(request.outputDirectory))
                throw new ArgumentException("A new absolute outputDirectory is required.");
            if (request.count < 0 || request.movingRatio < 0 || request.movingRatio > 1 || request.translucentRatio < 0 || request.translucentRatio > 1)
                throw new ArgumentException("count must be non-negative and the ratios must lie in [0, 1].");
            if (request.windowsSeconds == null || request.windowsSeconds.Length == 0 || request.windowsSeconds[0] <= 0 || request.windowsSeconds.Last() > 60.0f)
                throw new ArgumentException("windowsSeconds must be positive, ascending and end at or before 60 seconds.");
            float longest = request.windowsSeconds[0];
            for (int i = 1; i < request.windowsSeconds.Length; ++i)
            {
                if (request.windowsSeconds[i] <= request.windowsSeconds[i - 1]) throw new ArgumentException("windowsSeconds must be ascending.");
                longest = Mathf.Max(longest, request.windowsSeconds[i] - request.windowsSeconds[i - 1]);
            }
            Camera[] matches = Camera.allCameras.Where(c => c.cameraType == CameraType.Game && c.isActiveAndEnabled && (string.IsNullOrEmpty(request.camera) || c.name == request.camera)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Select exactly one active Game camera by exact name.");
            m_Request = request;
            m_Camera = matches[0];
            evidence = new RuntimeProbeEvidence
            {
                request = request, unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(),
                scene = m_Camera.gameObject.scene.path, camera = m_Camera.name, requestedBackend = string.IsNullOrEmpty(request.meshBackend) ? "Asset" : request.meshBackend,
            };

            // Preallocate for 1000 frames per second over the longest window interval so sampling never allocates.
            // A faster frame rate fails the probe instead of dropping samples.
            int capacity = Mathf.CeilToInt(longest * 1000.0f) + 16;
            m_FrameMs = new double[capacity]; m_ProxyMs = new double[capacity]; m_TransformMs = new double[capacity];
            m_ApplyMs = new double[capacity]; m_FrameAllocations = new double[capacity]; m_ProxyAllocations = new double[capacity];
            m_Scratch = new double[capacity];

            Directory.CreateDirectory(request.outputDirectory);
            m_PreviousMeasurement = PipelineFrameMeasurement.enabled;
            m_PreviousBackend = InfinityDebugDisplaySettings.current.mesh.backendOverride;
            try
            {
                if (!string.IsNullOrEmpty(request.meshBackend))
                    InfinityDebugDisplaySettings.current.mesh.backendOverride = (MeshPipeline.EMeshBackendPolicy)Enum.Parse(typeof(MeshPipeline.EMeshBackendPolicy), request.meshBackend);
                PipelineFrameMeasurement.enabled = true;
                RuntimeProbeContent.Spawn(request, m_Camera, m_Owned, m_Moving, m_BasePositions, out evidence.translucent);
                evidence.spawned = request.count;
                evidence.moving = m_Moving.Count;
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                EditorApplication.update += OnUpdate;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
                EditorApplication.LockReloadAssemblies();
                m_ReloadLocked = true;
                evidence.status = "Warmup";
            }
            catch (Exception error)
            {
                Stop("Failed", error);
                throw new InvalidOperationException("The runtime probe failed to start; evidence: " + Path.Combine(request.outputDirectory, "probe.json"), error);
            }
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) Stop("Cancelled", new InvalidOperationException("Play mode ended before the probe finished."));
        }

        void OnBeforeReload() => Stop("Cancelled", new InvalidOperationException("A forced assembly reload started before the probe finished."));

        // Runs inside the pipeline frame after the proxy update. It only copies counters and never throws into Render.
        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (Finished || !m_Sampling || !cameras.Contains(m_Camera)) return;
            double ticksToMs = 1000.0 / Stopwatch.Frequency;
            if (m_HasPending && PipelineFrameMeasurement.sequence == m_PendingSequence + 1)
            {
                if (m_WindowSamples < m_FrameMs.Length)
                {
                    int i = m_WindowSamples++;
                    m_FrameMs[i] = PipelineFrameMeasurement.frameTicks * ticksToMs;
                    m_FrameAllocations[i] = PipelineFrameMeasurement.frameAllocations;
                    m_ProxyMs[i] = m_PendingProxyMs;
                    m_TransformMs[i] = m_PendingTransformMs;
                    m_ApplyMs[i] = m_PendingApplyMs;
                    m_ProxyAllocations[i] = m_PendingProxyAllocations;
                    ++SampledFrames;
                }
                else m_WindowSamples = m_FrameMs.Length + 1;
            }
            m_PendingProxyMs = PipelineFrameMeasurement.proxyTicks * ticksToMs;
            m_PendingTransformMs = PipelineFrameMeasurement.transformTicks * ticksToMs;
            m_PendingApplyMs = PipelineFrameMeasurement.applyTicks * ticksToMs;
            m_PendingProxyAllocations = PipelineFrameMeasurement.proxyAllocations;
            m_PendingSequence = PipelineFrameMeasurement.sequence;
            m_HasPending = true;
        }

        void OnUpdate()
        {
            if (Finished) return;
            try
            {
                Move();
                ++m_Frame;
                if (!m_Sampling)
                {
                    if (m_Frame <= m_Request.warmupFrames) return;
                    m_Sampling = true;
                    evidence.status = "Sampling";
                    m_WindowsStarted = Time.realtimeSinceStartupAsDouble;
                    return;
                }
                if (m_WindowSamples > m_FrameMs.Length)
                    throw new InvalidOperationException("The frame rate exceeded the probe's preallocated 1000 Hz sample capacity.");
                if (Time.realtimeSinceStartupAsDouble - m_WindowsStarted >= m_Request.windowsSeconds[m_WindowIndex]) CloseWindow();
            }
            catch (Exception error)
            {
                Stop("Failed", error);
            }
        }

        void Move()
        {
            float phase = Time.realtimeSinceStartup;
            for (int i = 0; i < m_Moving.Count; ++i)
            {
                float offset = Mathf.Sin(phase * 2.0f + i * 0.37f) * 0.5f;
                m_Moving[i].position = m_BasePositions[i] + new Vector3(0.0f, offset, 0.0f);
            }
        }

        void CloseWindow()
        {
            var pipeline = RenderPipelineManager.currentPipeline as InfinityRenderPipeline
                ?? throw new InvalidOperationException("Infinity is not the active pipeline.");
            var window = new RuntimeProbeWindow
            {
                endSeconds = m_Request.windowsSeconds[m_WindowIndex],
                frames = m_WindowSamples,
                frameMs = Stat(m_FrameMs), proxyMs = Stat(m_ProxyMs), transformMs = Stat(m_TransformMs), applyMs = Stat(m_ApplyMs),
                frameAllocations = Stat(m_FrameAllocations), proxyAllocations = Stat(m_ProxyAllocations),
                monoUsedBytes = Profiler.GetMonoUsedSizeLong(), monoHeapBytes = Profiler.GetMonoHeapSizeLong(),
                totalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), totalReservedBytes = Profiler.GetTotalReservedMemoryLong(),
                graphicsDriverBytes = Profiler.GetAllocatedMemoryForGraphicsDriver(),
                poolTextureAllocatedBytes = pipeline.resourcePool.textures.estimatedAllocatedBytes,
                poolTextureIdleBytes = pipeline.resourcePool.textures.estimatedIdleBytes,
                poolTextureRetiredBytes = pipeline.resourcePool.textures.estimatedRetiredBytes,
                poolBufferAllocatedBytes = pipeline.resourcePool.buffers.estimatedAllocatedBytes,
                poolBufferIdleBytes = pipeline.resourcePool.buffers.estimatedIdleBytes,
                poolBufferRetiredBytes = pipeline.resourcePool.buffers.estimatedRetiredBytes,
                gcGen0 = GC.CollectionCount(0), gcGen1 = GC.CollectionCount(1), gcGen2 = GC.CollectionCount(2),
                actualBackend = InfinityDebugDisplaySettings.current.mesh.actualBackend.ToString(),
            };
            RuntimeProbeContent.ReadMeshState(pipeline, out window.meshEntities, out window.meshElements);
            evidence.windows.Add(window);
            m_WindowSamples = 0;
            if (++m_WindowIndex == m_Request.windowsSeconds.Length) Stop("Completed");
        }

        RuntimeProbeStat Stat(double[] values)
        {
            int n = m_WindowSamples;
            if (n == 0) return new RuntimeProbeStat();
            Array.Copy(values, m_Scratch, n);
            Array.Sort(m_Scratch, 0, n);
            double sum = 0;
            for (int i = 0; i < n; ++i) sum += m_Scratch[i];
            return new RuntimeProbeStat
            {
                p50 = m_Scratch[(n - 1) / 2], p95 = m_Scratch[Mathf.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1)],
                min = m_Scratch[0], max = m_Scratch[n - 1], mean = sum / n,
            };
        }

        internal void Stop(string status, Exception error = null)
        {
            if (Finished) return;
            Finished = true;
            evidence.status = status;
            if (error != null) evidence.error = error.ToString();
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            EditorApplication.update -= OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            try
            {
                for (int i = m_Owned.Count - 1; i >= 0; --i)
                    if (m_Owned[i] != null) UnityEngine.Object.Destroy(m_Owned[i]);
            }
            catch (Exception cleanup) { evidence.error += "\nCleanup: " + cleanup; }
            m_Owned.Clear();
            m_Moving.Clear();
            InfinityDebugDisplaySettings.current.mesh.backendOverride = m_PreviousBackend;
            PipelineFrameMeasurement.enabled = m_PreviousMeasurement;
            if (m_ReloadLocked) { EditorApplication.UnlockReloadAssemblies(); m_ReloadLocked = false; }
            evidence.restored = true;
            try { File.WriteAllText(Path.Combine(m_Request.outputDirectory, "probe.json"), JsonUtility.ToJson(evidence, true)); }
            catch (Exception write) { evidence.error += "\nEvidence write: " + write; UnityEngine.Debug.LogException(write); }
        }
    }
}
