using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Editor
{
    [Serializable]
    internal sealed class CpuMarkerProbeRequest
    {
        public string outputDirectory;
        public string camera;
        public string meshBackend = "";
        public int warmupFrames = 300;
        public int sampleFrames = 300;
        public bool moveCamera = true;
        public float cameraPathRadius = 4f;
        public bool captureAllocations = true;
        // false: a Scene view camera rendering during the probe fails it. true: Scene view Render calls are measured too,
        // and allocations are reported per camera type of each Render call.
        public bool includeSceneView;
    }

    // Reads the CPU ProfilerMarker tables outside the pipeline. It does not allocate during the sampled frames.
    internal static class CpuMarkerProbeCommands
    {
        static CpuMarkerProbeSession s_Session;

        [CliCommand("infinity_cpu_markers_start", "Record CPU marker means, call counts and optional GC.Alloc stacks in Play mode. Does not save assets.")]
        public static object Start(string requestPath)
        {
            if (s_Session != null && !s_Session.Finished)
            {
                throw new InvalidOperationException("A CPU marker probe is already active.");
            }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException("The CPU marker probe requires Play mode and a noncompiling Editor.");
            }
            if (CompilationPipeline.codeOptimization != CodeOptimization.Release)
            {
                throw new InvalidOperationException("Editor Code Optimization must be Release.");
            }
            CpuMarkerProbeRequest request = JsonUtility.FromJson<CpuMarkerProbeRequest>(File.ReadAllText(Path.GetFullPath(requestPath)));
            s_Session = new CpuMarkerProbeSession(request);
            return Status();
        }

        [CliCommand("infinity_cpu_markers_status", "Read CPU marker probe progress and its evidence directory.")]
        public static object Status() => s_Session == null
            ? (object)new { status = "Idle" }
            : new { status = s_Session.Status, finished = s_Session.Finished, frames = s_Session.SampledFrames, error = s_Session.Error, evidence = s_Session.OutputDirectory };

        [CliCommand("infinity_cpu_markers_cancel", "Cancel the CPU marker probe and restore the camera, backend and profiler flags.")]
        public static object Cancel()
        {
            s_Session?.Stop("Cancelled");
            return Status();
        }
    }

    internal sealed class CpuMarkerProbeSession
    {
        const string ReloadHolder = "infinity_cpu_markers";
        const int WindowCount = 3;
        // Allocation buckets by the camera types of one Render call: Game, SceneView, and any other combination.
        static readonly string[] s_CameraTypeBuckets = { "Game", "SceneView", "Other" };
        const int SampleCapacity = 512;
        static readonly ProfilerRecorderOptions s_Options = ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.WrapAroundWhenCapacityReached;
        static readonly FieldInfo s_InlineMarker = typeof(ProfilingSampler).GetField("m_InlineMarker", BindingFlags.Instance | BindingFlags.NonPublic);

        readonly CpuMarkerProbeRequest m_Request;
        readonly Camera m_Camera;
        readonly Vector3 m_CameraPosition;
        readonly Quaternion m_CameraRotation;
        readonly bool m_PreviousRunInBackground;
        readonly bool m_PreviousMeasurement;
        readonly EMeshDrawBackendPolicy? m_PreviousBackend;
        readonly bool m_PreviousProfiler;
        readonly bool m_PreviousAllocationCallstacks;
        readonly string[] m_Names;
        readonly ProfilerRecorder[] m_Recorders;
        readonly long[] m_TimeNs;
        readonly long[] m_Calls;
        readonly long[] m_CoverageTimeNs;
        readonly int m_UpdateIndex;
        readonly long[] m_ParentTimeNs;
        readonly long[] m_ParentCalls;
        readonly ProfilerRecorder[] m_Parents;
        readonly long[] m_WindowAllocations;
        readonly long[] m_WindowSubmitAllocations;
        readonly int[] m_WindowFrames;
        long m_ReadSequence;
        int m_Frame;
        int m_Sampled;
        int m_AllocFrames;
        bool m_Sampling;
        bool m_HasPending;
        bool m_Allocating;
        string m_Status = "Warmup";
        string m_Error;

        internal bool Finished { get; private set; }
        internal int SampledFrames => m_Sampled;
        internal string Status => m_Status;
        internal string Error => m_Error;
        internal string OutputDirectory => m_Request.outputDirectory;

        internal CpuMarkerProbeSession(CpuMarkerProbeRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.outputDirectory) || !Path.IsPathRooted(request.outputDirectory) || Directory.Exists(request.outputDirectory))
            {
                throw new ArgumentException("A new absolute outputDirectory is required.");
            }
            if (request.warmupFrames < 0 || request.sampleFrames < WindowCount || request.sampleFrames % WindowCount != 0)
            {
                throw new ArgumentException("sampleFrames must be a positive multiple of 3.");
            }
            if (request.moveCamera && request.cameraPathRadius <= 0f)
            {
                throw new ArgumentException("cameraPathRadius must be positive when the camera moves.");
            }
            Camera[] matches = SelectCameras(request.camera);
            if (matches.Length != 1)
            {
                throw new InvalidOperationException("Select exactly one active Game camera by exact name.");
            }
            m_Request = request;
            m_Camera = matches[0];
            m_CameraPosition = m_Camera.transform.position;
            m_CameraRotation = m_Camera.transform.rotation;
            List<string> names = new List<string>();
            List<ProfilerMarker> markers = new List<ProfilerMarker>();
            Add(names, markers, PipelineCpuMarkers.Names, PipelineCpuMarkers.Markers);
            Add(names, markers, LightCpuMarkers.Names, LightCpuMarkers.Markers);
            Add(names, markers, MeshCpuMarkers.Names, MeshCpuMarkers.Markers);
            Add(names, markers, RGCpuMarkers.Names, RGCpuMarkers.Markers);
            m_Names = names.ToArray();
            m_Recorders = new ProfilerRecorder[markers.Count];
            m_TimeNs = new long[markers.Count];
            m_Calls = new long[markers.Count];
            m_CoverageTimeNs = new long[4];
            m_ParentTimeNs = new long[4];
            m_ParentCalls = new long[4];
            m_WindowAllocations = new long[WindowCount * s_CameraTypeBuckets.Length];
            m_WindowSubmitAllocations = new long[WindowCount * s_CameraTypeBuckets.Length];
            m_WindowFrames = new int[WindowCount * s_CameraTypeBuckets.Length];
            m_UpdateIndex = names.IndexOf("UpdateMeshWorldGPU");
            for (int i = 0; i < markers.Count; ++i)
            {
                m_Recorders[i] = new ProfilerRecorder(markers[i], SampleCapacity, s_Options);
            }
            m_Parents = new[] { Inline("RecordRG"), Inline("ExecuteRG"), Inline("ProcessLight") };
            Directory.CreateDirectory(request.outputDirectory);
            m_PreviousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            m_PreviousMeasurement = PipelineFrameMeasurement.enabled;
            m_PreviousBackend = InfinityDebugDisplaySettings.current.mesh.backendOverride;
            m_PreviousProfiler = ProfilerDriver.enabled;
            m_PreviousAllocationCallstacks = Profiler.enableAllocationCallstacks;
            try
            {
                if (!string.IsNullOrEmpty(request.meshBackend))
                {
                    InfinityDebugDisplaySettings.current.mesh.backendOverride = (EMeshDrawBackendPolicy)Enum.Parse(typeof(EMeshDrawBackendPolicy), request.meshBackend);
                }
                PipelineFrameMeasurement.enabled = true;
                for (int i = 0; i < m_Recorders.Length; ++i)
                {
                    m_Recorders[i].Start();
                }
                for (int i = 0; i < m_Parents.Length; ++i)
                {
                    m_Parents[i].Start();
                }
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                EditorApplication.update += OnUpdate;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
                ReloadLock.Acquire(ReloadHolder);
            }
            catch (Exception error)
            {
                Stop("Failed", error);
                throw new InvalidOperationException("The CPU marker probe failed to start; evidence: " + Path.Combine(request.outputDirectory, "markers.json"), error);
            }
        }

        static Camera[] SelectCameras(string name)
        {
            var found = new List<Camera>();
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; ++i)
            {
                Camera camera = cameras[i];
                if (camera.cameraType == CameraType.Game && camera.isActiveAndEnabled && (string.IsNullOrEmpty(name) || camera.name == name))
                {
                    found.Add(camera);
                }
            }
            return found.ToArray();
        }

        static void Add(List<string> names, List<ProfilerMarker> markers, IReadOnlyList<string> sourceNames, IReadOnlyList<ProfilerMarker> sourceMarkers)
        {
            for (int i = 0; i < sourceNames.Count; ++i)
            {
                names.Add(sourceNames[i]);
                markers.Add(sourceMarkers[i]);
            }
        }

        static ProfilerRecorder Inline(string name)
        {
            if (s_InlineMarker == null)
            {
                throw new InvalidOperationException("ProfilingSampler has no inline CPU marker.");
            }
            ProfilingSampler sampler = ProfilingSampler.Get((EPipelineProfileId)Enum.Parse(typeof(EPipelineProfileId), name));
            ProfilerMarker marker = (ProfilerMarker)s_InlineMarker.GetValue(sampler);
            return new ProfilerRecorder(marker, SampleCapacity, s_Options);
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                Stop("Cancelled", new InvalidOperationException("Play mode ended before the probe finished."));
            }
        }

        void OnBeforeReload() => Stop("Cancelled", new InvalidOperationException("A forced assembly reload started before the probe finished."));

        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (Finished)
            {
                return;
            }
            if (!m_Request.includeSceneView)
            {
                for (int i = 0; i < cameras.Count; ++i)
                {
                    if (cameras[i].cameraType == CameraType.SceneView)
                    {
                        Stop("Failed", new InvalidOperationException("A Scene view camera rendered during the probe. Close Scene views, or set includeSceneView."));
                        return;
                    }
                }
            }
            // The previous Render call completed its record before this call began; read it once, by its camera types.
            if (m_Sampling && !m_Allocating && m_Sampled < m_Request.sampleFrames && PipelineFrameMeasurement.sequence != m_ReadSequence)
            {
                AccumulateRenderRecord();
            }
            m_ReadSequence = PipelineFrameMeasurement.sequence;
            if (!m_Sampling || !cameras.Contains(m_Camera))
            {
                return;
            }
            if (m_HasPending && !m_Allocating && m_Sampled < m_Request.sampleFrames)
            {
                Accumulate();
                m_Sampled++;
            }
            m_HasPending = true;
        }

        int CurrentWindow()
        {
            int window = m_Sampled / (m_Request.sampleFrames / WindowCount);
            return window >= WindowCount ? WindowCount - 1 : window;
        }

        void AccumulateRenderRecord()
        {
            int types = PipelineFrameMeasurement.frameCameraTypes;
            int bucket = types == (int)CameraType.Game ? 0 : types == (int)CameraType.SceneView ? 1 : 2;
            int index = bucket * WindowCount + CurrentWindow();
            m_WindowAllocations[index] += PipelineFrameMeasurement.frameAllocations;
            m_WindowSubmitAllocations[index] += PipelineFrameMeasurement.nativeSubmitAllocations;
            m_WindowFrames[index]++;
        }

        void Accumulate()
        {
            long updateTime = 0;
            int updateCalls = 0;
            for (int i = 0; i < m_Recorders.Length; ++i)
            {
                Read(ref m_Recorders[i], out long time, out int calls);
                m_TimeNs[i] += time;
                m_Calls[i] += calls;
                if (i == m_UpdateIndex)
                {
                    updateTime = time;
                    updateCalls = calls;
                }
                int group = CoverageGroup(m_Names[i]);
                if (group >= 0)
                {
                    m_CoverageTimeNs[group] += time;
                }
            }
            for (int i = 0; i < m_Parents.Length; ++i)
            {
                Read(ref m_Parents[i], out long time, out int calls);
                m_ParentTimeNs[i] += time;
                m_ParentCalls[i] += calls;
            }
            m_ParentTimeNs[3] += updateTime;
            m_ParentCalls[3] += updateCalls;
        }

        // 0 RecordRG, 1 ExecuteRG, 2 ProcessLight, 3 UpdateMeshWorldGPU. Nested markers return -1.
        void ResetRecorders()
        {
            for (int i = 0; i < m_Recorders.Length; ++i)
            {
                m_Recorders[i].Reset();
                m_Recorders[i].Start();
            }
            for (int i = 0; i < m_Parents.Length; ++i)
            {
                m_Parents[i].Reset();
                m_Parents[i].Start();
            }
        }

        static void Read(ref ProfilerRecorder recorder, out long time, out int calls)
        {
            calls = recorder.Count;
            time = 0;
            for (int i = 0; i < calls; ++i)
            {
                time += recorder.GetSample(i).Value;
            }
            recorder.Reset();
            recorder.Start();
        }

        static int CoverageGroup(string name)
        {
            if (name.StartsWith("Record.", StringComparison.Ordinal))
            {
                return 0;
            }
            if (name.StartsWith("RG.Compile.", StringComparison.Ordinal) || name == "RG.Execute.Prepare" || name == "RG.Execute.RendererLists"
                || name == "RG.Execute.Submit" || name == "RG.Execute.Clear")
            {
                return 1;
            }
            if (name.StartsWith("ProcessLight.", StringComparison.Ordinal) || name.StartsWith("Shadow.", StringComparison.Ordinal)
                || (name.StartsWith("Light.", StringComparison.Ordinal) && name != "Light.TileRect"))
            {
                return 2;
            }
            if (name == "Mesh.ResidencyGate")
            {
                return 3;
            }
            return -1;
        }

        void OnUpdate()
        {
            if (Finished)
            {
                return;
            }
            Pump();
            try
            {
                Move();
                m_Frame++;
                if (!m_Sampling)
                {
                    if (m_Frame <= m_Request.warmupFrames)
                    {
                        return;
                    }
                    m_Sampling = true;
                    m_Status = "Sampling";
                    ResetRecorders();
                    return;
                }
                if (!m_Allocating && m_Sampled >= m_Request.sampleFrames)
                {
                    if (!m_Request.captureAllocations)
                    {
                        WriteMarkers();
                        Stop("Completed");
                        return;
                    }
                    m_Allocating = true;
                    m_Status = "Allocations";
                    ProfilerDriver.enabled = true;
                    Profiler.enabled = true;
                    Profiler.enableAllocationCallstacks = true;
                    m_AllocFrames = 0;
                    return;
                }
                if (m_Allocating)
                {
                    m_AllocFrames++;
                    if (m_AllocFrames >= 3)
                    {
                        WriteMarkers();
                        WriteAllocations();
                        Stop("Completed");
                    }
                }
            }
            catch (Exception error)
            {
                Stop("Failed", error);
            }
        }

        void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (m_Request.includeSceneView)
            {
                SceneView.RepaintAll();
            }
            Type gameView = Type.GetType("UnityEditor.GameView, UnityEditor");
            if (gameView == null)
            {
                return;
            }
            UnityEngine.Object[] views = Resources.FindObjectsOfTypeAll(gameView);
            for (int i = 0; i < views.Length; ++i)
            {
                if (views[i] is EditorWindow window)
                {
                    window.Repaint();
                }
            }
        }

        void Move()
        {
            if (!m_Request.moveCamera || m_Camera == null)
            {
                return;
            }
            float angle = Time.realtimeSinceStartup * 0.35f;
            Vector3 right = m_CameraRotation * Vector3.right;
            Vector3 forward = m_CameraRotation * Vector3.forward;
            m_Camera.transform.SetPositionAndRotation(
                m_CameraPosition + (right * Mathf.Sin(angle) + forward * Mathf.Cos(angle)) * m_Request.cameraPathRadius,
                m_CameraRotation);
        }

        void WriteMarkers()
        {
            var text = new StringBuilder();
            text.Append("{\n");
            Field("scene", m_Camera.gameObject.scene.path, text, true);
            Field("camera", m_Camera.name, text, true);
            Field("backend", string.IsNullOrEmpty(m_Request.meshBackend) ? "Asset" : m_Request.meshBackend, text, true);
            text.Append("  \"sampleFrames\": ").Append(m_Sampled.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("  \"parents\": [\n");
            string[] parents = { "Inl_RecordRG", "Inl_ExecuteRG", "Inl_ProcessLight", "UpdateMeshWorldGPU" };
            for (int i = 0; i < parents.Length; ++i)
            {
                double meanMs = m_Sampled == 0 ? 0 : m_ParentTimeNs[i] / 1e6 / m_Sampled;
                double childMs = m_Sampled == 0 ? 0 : m_CoverageTimeNs[i] / 1e6 / m_Sampled;
                double calls = m_Sampled == 0 ? 0 : (double)m_ParentCalls[i] / m_Sampled;
                text.Append("    { \"name\": \"").Append(parents[i]).Append("\", \"meanMs\": ").Append(meanMs.ToString("R", CultureInfo.InvariantCulture));
                text.Append(", \"callsPerFrame\": ").Append(calls.ToString("R", CultureInfo.InvariantCulture));
                text.Append(", \"directChildMs\": ").Append(childMs.ToString("R", CultureInfo.InvariantCulture));
                double gap = meanMs <= 0 ? 0 : Math.Abs(childMs - meanMs) / meanMs;
                text.Append(", \"gap\": ").Append(gap.ToString("R", CultureInfo.InvariantCulture)).Append(" }");
                text.Append(i + 1 == parents.Length ? "\n" : ",\n");
            }
            text.Append("  ],\n  \"includeSceneView\": ").Append(m_Request.includeSceneView ? "true" : "false");
            text.Append(",\n  \"windowsByCameraType\": [\n");
            for (int bucket = 0; bucket < s_CameraTypeBuckets.Length; ++bucket)
            {
                text.Append("    { \"cameraTypes\": \"").Append(s_CameraTypeBuckets[bucket]).Append("\", \"windows\": [");
                for (int window = 0; window < WindowCount; ++window)
                {
                    int i = bucket * WindowCount + window;
                    double mean = m_WindowFrames[i] == 0 ? 0 : (double)m_WindowAllocations[i] / m_WindowFrames[i];
                    double submit = m_WindowFrames[i] == 0 ? 0 : (double)m_WindowSubmitAllocations[i] / m_WindowFrames[i];
                    text.Append(" { \"renderCalls\": ").Append(m_WindowFrames[i].ToString(CultureInfo.InvariantCulture));
                    text.Append(", \"frameAllocationsMean\": ").Append(mean.ToString("R", CultureInfo.InvariantCulture));
                    text.Append(", \"nativeSubmitAllocationsMean\": ").Append(submit.ToString("R", CultureInfo.InvariantCulture)).Append(" }");
                    text.Append(window + 1 == WindowCount ? " " : ",");
                }
                text.Append(bucket + 1 == s_CameraTypeBuckets.Length ? "] }\n" : "] },\n");
            }
            text.Append("  ],\n  \"markers\": [\n");
            for (int i = 0; i < m_Names.Length; ++i)
            {
                double meanMs = m_Sampled == 0 ? 0 : m_TimeNs[i] / 1e6 / m_Sampled;
                double calls = m_Sampled == 0 ? 0 : (double)m_Calls[i] / m_Sampled;
                text.Append("    { \"name\": \"").Append(m_Names[i]).Append("\", \"meanMs\": ").Append(meanMs.ToString("R", CultureInfo.InvariantCulture));
                text.Append(", \"callsPerFrame\": ").Append(calls.ToString("R", CultureInfo.InvariantCulture));
                text.Append(", \"valid\": ").Append(m_Recorders[i].Valid ? "true" : "false").Append(" }");
                text.Append(i + 1 == m_Names.Length ? "\n" : ",\n");
            }
            text.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(m_Request.outputDirectory, "markers.json"), text.ToString());
        }

        static void Field(string name, string value, StringBuilder text, bool comma)
        {
            text.Append("  \"").Append(name).Append("\": \"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append(comma ? "\",\n" : "\"\n");
        }

        void WriteAllocations()
        {
            var text = new StringBuilder();
            int frame = ProfilerDriver.lastFrameIndex;
            text.Append("frame ").Append(frame.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (frame < 0)
            {
                File.WriteAllText(Path.Combine(m_Request.outputDirectory, "alloc-stacks.txt"), text.ToString());
                return;
            }
            using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, (HierarchyFrameDataView.ViewModes)0, HierarchyFrameDataView.columnGcMemory, false))
            {
                var children = new List<int>(64);
                var path = new List<string>(32);
                var totals = new Dictionary<string, double>();
                // Each camera records under its own sampler, named after the camera, so a path names its camera.
                var sceneCameras = new HashSet<string>();
                foreach (object sceneView in SceneView.sceneViews)
                {
                    if (sceneView is SceneView open && open.camera != null)
                    {
                        sceneCameras.Add(open.camera.name);
                    }
                }
                Walk(view, view.GetRootItemID(), children, path, totals, sceneCameras);
                var rows = new List<KeyValuePair<string, double>>(totals);
                rows.Sort((a, b) => b.Value.CompareTo(a.Value));
                for (int i = 0; i < rows.Count; ++i)
                {
                    text.Append(rows[i].Value.ToString("R", CultureInfo.InvariantCulture)).Append('\t').Append(rows[i].Key).Append('\n');
                }
            }
            File.WriteAllText(Path.Combine(m_Request.outputDirectory, "alloc-stacks.txt"), text.ToString());
        }

        static void Walk(HierarchyFrameDataView view, int id, List<int> children, List<string> path, Dictionary<string, double> totals, HashSet<string> sceneCameras)
        {
            string name = view.GetItemName(id);
            path.Add(name);
            if (name == "GC.Alloc")
            {
                double bytes = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnGcMemory);
                string stack = view.ResolveItemCallstack(id);
                string key = (Interests(path) ? "record-or-execute" : "other") + " " + CameraTypeOf(path, sceneCameras) + "\n" + string.Join(" > ", path) + "\n" + stack;
                totals.TryGetValue(key, out double sum);
                totals[key] = sum + bytes;
            }
            children.Clear();
            view.GetItemChildren(id, children);
            var ids = new int[children.Count];
            for (int i = 0; i < children.Count; ++i)
            {
                ids[i] = children[i];
            }
            for (int i = 0; i < ids.Length; ++i)
            {
                Walk(view, ids[i], children, path, totals, sceneCameras);
            }
            path.RemoveAt(path.Count - 1);
        }

        static string CameraTypeOf(List<string> path, HashSet<string> sceneCameras)
        {
            for (int i = 0; i < path.Count; ++i)
            {
                if (sceneCameras.Contains(path[i]))
                {
                    return "SceneView";
                }
            }
            return "Game-or-shared";
        }

        static bool Interests(List<string> path)
        {
            for (int i = 0; i < path.Count; ++i)
            {
                string name = path[i];
                if (name == "Inl_RecordRG" || name == "Inl_ExecuteRG" || name.StartsWith("Record.", StringComparison.Ordinal) || name.StartsWith("RG.", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        internal void Stop(string status, Exception error = null)
        {
            if (Finished)
            {
                return;
            }
            Finished = true;
            m_Status = status;
            if (error != null)
            {
                m_Error = error.ToString();
                File.WriteAllText(Path.Combine(m_Request.outputDirectory, "error.txt"), m_Error);
            }
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            EditorApplication.update -= OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            if (m_Recorders != null)
            {
                for (int i = 0; i < m_Recorders.Length; ++i)
                {
                    if (m_Recorders[i].Valid)
                    {
                        m_Recorders[i].Dispose();
                    }
                }
            }
            if (m_Parents != null)
            {
                for (int i = 0; i < m_Parents.Length; ++i)
                {
                    if (m_Parents[i].Valid)
                    {
                        m_Parents[i].Dispose();
                    }
                }
            }
            if (m_Camera != null)
            {
                m_Camera.transform.SetPositionAndRotation(m_CameraPosition, m_CameraRotation);
            }
            if (InfinityDebugDisplaySettings.current != null)
            {
                InfinityDebugDisplaySettings.current.mesh.backendOverride = m_PreviousBackend;
            }
            PipelineFrameMeasurement.enabled = m_PreviousMeasurement;
            Profiler.enableAllocationCallstacks = m_PreviousAllocationCallstacks;
            ProfilerDriver.enabled = m_PreviousProfiler;
            Application.runInBackground = m_PreviousRunInBackground;
            ReloadLock.Release(ReloadHolder);
        }
    }
}
