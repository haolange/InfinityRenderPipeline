using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    internal sealed class RenderCaptureRequest
    {
        public string outputDirectory, fixture, scene, camera;
        public int width, height, warmupFrames = 120, frameCount = 3, frameInterval = 1;
        public float timeoutSeconds = 120;
        public bool includeConfidence = true;
        public bool includeZBinOverflow;
        public string[] buffers = { "DisplayColor", "Lighting" };
        public RectInt roi;
    }

    internal sealed class CaptureRetirement
    {
        public bool recorded, submitted, terminal, retired;
        public bool queueUncertain, hasDrainFence;
        public GraphicsFence drainFence;
        internal void QueueFailed() { recorded = true; queueUncertain = true; }
        internal bool ResolveUncertainQueue()
        {
            if (!queueUncertain || terminal || !submitted || !hasDrainFence || !drainFence.passed) return false;
            terminal = true;
            return true;
        }
        public bool CanRetire => !recorded || (submitted && terminal);
        public void Retire()
        {
            if (retired || !CanRetire) throw new InvalidOperationException("Capture staging is still owned by queued GPU work.");
            retired = true;
        }
    }

    internal sealed class RenderCaptureSession
    {
        [Serializable] internal sealed class BufferEvidence
        {
            public string semantic, producer, sourceQueue, queue = "Graphics", format, file, error;
            public int width, height, layers, samples, producerFrame, completionFrame, submissionFrame;
            public long bytes;
            public string resourceKind = "Texture", bufferTarget;
            public int bufferCount, bufferStride;
            public uint counter;
            public bool historyReset, submitted, readbackTerminal, retired;
            public TextureDescriptor sourceDescriptor;
            public RectInt sourceRoi;
            public RawCaptureStatistics statistics;
        }
        [Serializable] sealed class SessionEvidence
        {
            public string status = "Warming", error, persistenceError, unity, api, gpu, platform, scene, camera;
            public int successfulFrames, capturedFrames, outstanding, retired, nativeRetired;
            public string[] volumeSnapshots;
            public List<BufferEvidence> buffers = new List<BufferEvidence>();
            public List<NativeDisplayProbe.Evidence> nativeDisplays = new List<NativeDisplayProbe.Evidence>();
        }
        internal sealed class Staging : RenderGraph.IRGPassQueueObserver
        {
            public RenderTexture texture;
            public GraphicsBuffer buffer;
            public RTHandle handle;
            public readonly CaptureRetirement lifetime = new CaptureRetirement();
            public BufferEvidence evidence;
            public RenderCaptureSession owner;
            public GraphicsFormat readbackFormat;
            public void OnQueued() => lifetime.recorded = true;
            public void OnQueueFailed() => lifetime.QueueFailed();
            public void Complete(AsyncGPUReadbackRequest request) => owner.OnReadback(this, request);
        }

        internal readonly RenderCaptureRequest request;
        readonly SessionEvidence m_Evidence;
        readonly List<Staging> m_Staging = new List<Staging>();
        readonly List<NativeDisplayProbe> m_NativeProbes = new List<NativeDisplayProbe>();
        readonly double m_Started;
        bool m_StopRecording, m_CameraSucceeded, m_CaptureThisFrame, m_DidCapture, m_PersistenceFailed;
        Camera m_Camera;
        bool m_CameraAssigned;
        int m_LastGraphFrame = -1;
        public bool Finished => m_StopRecording && m_Staging.Count == 0 && m_NativeProbes.Count == 0;
        public bool CaptureThisFrame => m_CaptureThisFrame;
        internal string Status => m_Evidence.status;
        internal int OutstandingCount => m_Staging.Count + m_NativeProbes.Count;

        internal RenderCaptureSession(RenderCaptureRequest captureRequest)
        {
            Validate(captureRequest);
            request = JsonUtility.FromJson<RenderCaptureRequest>(JsonUtility.ToJson(captureRequest));
            Directory.CreateDirectory(request.outputDirectory);
            File.WriteAllText(Path.Combine(request.outputDirectory, "request.json"), JsonUtility.ToJson(request, true));
            m_Started = Time.realtimeSinceStartupAsDouble;
            m_Evidence = new SessionEvidence { unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(),
                gpu = SystemInfo.graphicsDeviceName, platform = Application.platform.ToString(), scene = request.scene, camera = request.camera };
            Save(true);
        }

        internal static void Validate(RenderCaptureRequest request)
        {
            if (request == null || !Path.IsPathRooted(request.outputDirectory ?? "") || Directory.Exists(request.outputDirectory))
                throw new ArgumentException("Capture requires a new absolute output directory.");
            if (string.IsNullOrWhiteSpace(request.fixture) || string.IsNullOrWhiteSpace(request.scene) || string.IsNullOrWhiteSpace(request.camera))
                throw new ArgumentException("Capture requires fixed fixture, scene and camera identifiers.");
            if (request.width < 1 || request.height < 1 || request.warmupFrames < 0 || request.frameCount < 1 || request.frameCount > 32 || request.frameInterval < 1 || request.timeoutSeconds <= 0)
                throw new ArgumentException("Invalid capture dimensions, frame range or timeout.");
            if (request.roi.width < 1 || request.roi.height < 1 || request.roi.xMin < 0 || request.roi.yMin < 0 || request.roi.xMax > request.width || request.roi.yMax > request.height)
                throw new ArgumentException("Declare a nonempty in-bounds ROI before capture.");
            if (request.buffers == null || request.buffers.Length == 0) throw new ArgumentException("Capture requires explicit source buffers.");
            var names = new HashSet<string>();
            foreach (string semantic in request.buffers)
            {
                if (!names.Add(semantic) || !RenderCaptureService.IsSupportedBuffer(semantic))
                    throw new ArgumentException("Unknown or duplicate capture source: " + semantic);
            }
        }

        internal void PrepareCamera(Camera camera, CameraFrameState state, InfinityRenderPipelineAsset asset)
        {
            m_CaptureThisFrame = false;
            if (m_StopRecording || camera.cameraType != CameraType.Game || camera.name != request.camera || camera.gameObject.scene.name != request.scene) return;
            if (m_CameraAssigned && m_Camera != camera) { Fail("The fixed capture camera was replaced."); return; }
            m_Camera = camera;
            m_CameraAssigned = true;
            if (camera.pixelWidth != request.width || camera.pixelHeight != request.height || asset.debugView != EDebugView.None)
            { Fail("Camera dimensions or normal-beauty DebugView contract changed."); return; }
            if (state.cameraUniform.historyReset) m_Evidence.successfulFrames = 0;
            m_CaptureThisFrame = m_Evidence.successfulFrames >= request.warmupFrames &&
                (m_Evidence.successfulFrames - request.warmupFrames) % request.frameInterval == 0;
            if (m_CaptureThisFrame)
            {
                m_Evidence.status = "Capturing";
                var snapshots = new List<string>();
                foreach (VolumeComponent component in asset.volumeProfile.components)
                {
                    VolumeComponent blended = state.volumeStack.GetComponent(component.GetType());
                    if (blended == null) { Fail("Required Volume registry type missing: " + component.GetType().FullName); return; }
                    snapshots.Add(JsonUtility.ToJson(blended));
                }
                m_Evidence.volumeSnapshots = snapshots.ToArray();
            }
        }

        internal Staging Reserve(string semantic, string producer, TextureDescriptor descriptor, bool historyReset)
        {
            if (!m_CaptureThisFrame || m_StopRecording) throw new InvalidOperationException("No capture frame was prepared.");
            if (descriptor.width < 1 || descriptor.height < 1 || descriptor.slices != 1 || descriptor.dimension != TextureDimension.Tex2D || descriptor.enableMSAA || descriptor.useMipMap)
                throw new InvalidOperationException("Capture source requires an explicit single-mip, non-MSAA 2D descriptor.");
            RTHandle handle = RTHandles.Alloc(descriptor.width, descriptor.height, descriptor.slices,
                (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, FilterMode.Point, TextureWrapMode.Clamp,
                descriptor.dimension, false, false, false, descriptor.isShadowMap, 1, 0, MSAASamples.None,
                false, false, false, RenderTextureMemoryless.None, VRTextureUsage.None, "CaptureStaging_" + semantic);
            RenderTexture texture = handle.rt;
            if (texture == null || !texture.IsCreated()) { RTHandles.Release(handle); throw new InvalidOperationException("Capture staging allocation failed."); }
            GraphicsFormat readbackFormat = descriptor.depthBufferBits == EDepthBits.None ? texture.graphicsFormat : texture.depthStencilFormat;
            var evidence = new BufferEvidence { semantic = semantic, producer = producer, format = readbackFormat.ToString(),
                width = texture.width, height = texture.height, layers = texture.volumeDepth, samples = texture.antiAliasing,
                producerFrame = Time.frameCount, historyReset = historyReset, sourceDescriptor = descriptor,
                sourceRoi = ScaleRoi(descriptor.width, descriptor.height) };
            var staging = new Staging { texture = texture, handle = handle, evidence = evidence, owner = this, readbackFormat = readbackFormat };
            m_Staging.Add(staging); m_Evidence.buffers.Add(evidence);
            m_DidCapture = true;
            Save();
            return staging;
        }

        RectInt ScaleRoi(int width, int height)
        {
            int left = (int)((long)request.roi.xMin * width / request.width);
            int bottom = (int)((long)request.roi.yMin * height / request.height);
            int right = (int)(((long)request.roi.xMax * width + request.width - 1) / request.width);
            int top = (int)(((long)request.roi.yMax * height + request.height - 1) / request.height);
            return new RectInt(left, bottom, right - left, top - bottom);
        }

        internal Staging ReserveCounter(string semantic, GraphicsBuffer source, string producer, string queue)
        {
            if (!m_CaptureThisFrame || m_StopRecording) throw new InvalidOperationException("No capture frame was prepared.");
            if (source.count != 1 || source.stride != sizeof(uint) || (source.target & GraphicsBuffer.Target.CopySource) == 0)
                throw new InvalidOperationException("Counter capture requires one copyable uint.");
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination, 1, sizeof(uint));
            var evidence = new BufferEvidence { semantic = semantic, producer = producer, sourceQueue = queue,
                resourceKind = "GraphicsBuffer", format = "UInt32", bufferTarget = source.target.ToString(),
                bufferCount = source.count, bufferStride = source.stride, producerFrame = Time.frameCount };
            var staging = new Staging { buffer = buffer, evidence = evidence, owner = this };
            m_Staging.Add(staging); m_Evidence.buffers.Add(evidence); m_DidCapture = true;
            Save();
            return staging;
        }

        internal void CameraExecuted(Camera camera, bool success, Exception exception = null)
        {
            if (Finished) return;
            if (camera != m_Camera) return;
            m_CameraSucceeded = success;
            if (!success) Fail(exception != null ? exception.ToString() : "Captured camera graph failed; frame cannot count as successful.");
        }

        internal NativeDisplayProbe ReserveNativeProbe()
        {
            if (!m_CaptureThisFrame || m_StopRecording) return null;
            NativeDisplayProbe probe = NativeDisplayProbe.Create();
            if (probe != null)
            {
                m_NativeProbes.Add(probe);
                m_Evidence.nativeDisplays.Add(probe.evidence);
            }
            return probe;
        }

        internal void RecordGraph(RenderGraph.RGBuilder graph, int frame)
        {
            if (m_LastGraphFrame == frame) return;
            File.WriteAllText(Path.Combine(request.outputDirectory, "compiled-graph-" + frame + ".txt"), graph.DescribeCompiledGraph());
            m_LastGraphFrame = frame;
        }

        internal void AfterSubmit()
        {
            if (Finished) return;
            foreach (NativeDisplayProbe probe in m_NativeProbes)
                if (probe.lifetime.recorded) probe.lifetime.submitted = true;
            foreach (Staging staging in m_Staging)
            {
                if (!staging.lifetime.recorded || staging.lifetime.submitted) continue;
                staging.lifetime.submitted = staging.evidence.submitted = true;
                staging.evidence.submissionFrame = Time.frameCount;
            }
            bool needsDrainFence = false;
            foreach (Staging staging in m_Staging)
                needsDrainFence |= staging.lifetime.queueUncertain && !staging.lifetime.hasDrainFence;
            foreach (NativeDisplayProbe probe in m_NativeProbes)
                needsDrainFence |= probe.lifetime.queueUncertain && !probe.lifetime.hasDrainFence;
            if (needsDrainFence)
            {
                // This exceptional path synchronously drains native readback requests after Submit.
                // No normal-frame capture waits here. Requests that never existed carry no data.
                AsyncGPUReadback.WaitAllRequests();
                foreach (Staging staging in m_Staging)
                {
                    if (!staging.lifetime.queueUncertain || staging.lifetime.terminal) continue;
                    staging.lifetime.terminal = true;
                    staging.evidence.error = "Uncertain queue drained without a native readback callback; no data available.";
                    Fail(staging.evidence.error);
                }
                var commands = CommandBufferPool.Get();
                try
                {
                    GraphicsFence fence = commands.CreateAsyncGraphicsFence();
                    Graphics.ExecuteCommandBuffer(commands);
                    foreach (Staging staging in m_Staging)
                    {
                        if (!staging.lifetime.queueUncertain || staging.lifetime.hasDrainFence) continue;
                        staging.lifetime.drainFence = fence; staging.lifetime.hasDrainFence = true;
                    }
                    foreach (NativeDisplayProbe probe in m_NativeProbes)
                    {
                        if (!probe.lifetime.queueUncertain || probe.lifetime.hasDrainFence) continue;
                        probe.lifetime.drainFence = fence; probe.lifetime.hasDrainFence = true;
                    }
                }
                finally { commands.Clear(); CommandBufferPool.Release(commands); }
            }
            if (m_CameraSucceeded && !m_StopRecording)
            {
                m_Evidence.successfulFrames++;
                if (m_DidCapture && ++m_Evidence.capturedFrames >= request.frameCount)
                { m_StopRecording = true; m_Evidence.status = "Draining"; }
            }
            m_CameraSucceeded = false;
            m_CaptureThisFrame = false;
            m_DidCapture = false;
            Pump();
        }

        void OnReadback(Staging staging, AsyncGPUReadbackRequest readback)
        {
            try
            {
                staging.evidence.completionFrame = Time.frameCount;
                if (readback.hasError) throw new InvalidOperationException("GPU readback failed without data substitution.");
                byte[] bytes = readback.GetData<byte>().ToArray();
                staging.evidence.bytes = bytes.LongLength;
                staging.evidence.file = staging.evidence.semantic + "-" + staging.evidence.producerFrame + ".bin";
                File.WriteAllBytes(Path.Combine(request.outputDirectory, staging.evidence.file), bytes);
                if (staging.buffer != null)
                {
                    if (bytes.Length != sizeof(uint)) throw new InvalidOperationException("Counter readback byte count mismatch.");
                    staging.evidence.counter = BitConverter.ToUInt32(bytes, 0);
                    if (staging.evidence.counter != 0) Fail("ZBin overflow detected; raw counter preserved.");
                }
                else
                {
                    staging.evidence.statistics = RawCaptureStatistics.Compute(bytes, staging.readbackFormat,
                        staging.evidence.width, staging.evidence.height, staging.evidence.sourceRoi);
                    if (staging.evidence.statistics.nonFinite != 0) Fail("Raw GPU data contains NaN/Inf; preserved without sanitization.");
                }
            }
            catch (Exception error) { staging.evidence.error = error.ToString(); Fail(error.ToString()); }
            finally
            {
                staging.lifetime.terminal = staging.evidence.readbackTerminal = true;
                Pump();
            }
        }

        internal void Cancel(string reason = "Cancelled")
        {
            if (Finished) return;
            m_StopRecording = true; m_CaptureThisFrame = false;
            if (m_Evidence.status != "Failed") m_Evidence.status = reason;
            Save();
            Pump();
        }
        internal void Fail(string reason)
        {
            m_StopRecording = true; m_CaptureThisFrame = false;
            m_Evidence.status = "Failed";
            if (string.IsNullOrEmpty(m_Evidence.error)) m_Evidence.error = reason;
            Save();
        }
        internal void Pump()
        {
            if (Finished) return;
            if (!m_StopRecording && Time.realtimeSinceStartupAsDouble - m_Started > request.timeoutSeconds) Cancel("TimedOut");
            for (int i = m_Staging.Count - 1; i >= 0; --i)
            {
                Staging staging = m_Staging[i];
                if (!staging.lifetime.CanRetire || (!staging.lifetime.recorded && !m_StopRecording)) continue;
                staging.lifetime.Retire();
                if (staging.handle != null) RTHandles.Release(staging.handle);
                staging.buffer?.Release();
                staging.evidence.retired = true;
                m_Staging.RemoveAt(i); m_Evidence.retired++;
            }
            for (int i = m_NativeProbes.Count - 1; i >= 0; --i)
            {
                NativeDisplayProbe probe = m_NativeProbes[i];
                if (!probe.lifetime.recorded && !m_StopRecording) continue;
                if (!probe.Poll()) continue;
                if (!string.IsNullOrEmpty(probe.evidence.error)) Fail(probe.evidence.error);
                m_NativeProbes.RemoveAt(i); m_Evidence.nativeRetired++;
            }
            if (Finished && m_Evidence.status == "Draining") m_Evidence.status = "Completed";
            Save();
        }
        void Save(bool requirePersistence = false)
        {
            m_Evidence.outstanding = m_Staging.Count + m_NativeProbes.Count;
            try
            {
                File.WriteAllText(Path.Combine(request.outputDirectory, "capture.json"), JsonUtility.ToJson(m_Evidence, true));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // Evidence failure stops new recording, but cannot interrupt terminal GPU retirement.
                m_StopRecording = true;
                m_CaptureThisFrame = false;
                m_Evidence.status = "Failed";
                if (string.IsNullOrEmpty(m_Evidence.persistenceError)) m_Evidence.persistenceError = error.ToString();
                if (!m_PersistenceFailed)
                {
                    m_PersistenceFailed = true;
                    Debug.LogException(error);
                }
                if (requirePersistence) throw;
            }
        }
    }
}
