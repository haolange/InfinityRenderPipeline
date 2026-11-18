#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using System.Collections.Generic;

namespace InfinityTech.Rendering.Pipeline
{
    // Bounded diagnostics only. Observers read completed samples outside the measured call chain. One record describes
    // one Render call: Unity renders the Scene view and the Game view in separate calls, so a record carries the camera
    // types it rendered and `sequence` tells a reader that a new record completed.
    internal static class PipelineFrameMeasurement
    {
        internal static bool enabled;
        // GC.Alloc counts managed allocations on this thread inside the scope. No profiler counter reports bytes for
        // the same scope, so allocation size is not measured here.
        internal static long frameAllocations, proxyAllocations, frameTicks, proxyTicks, sequence;
        // CameraType flags of the cameras the last completed Render call rendered.
        internal static int frameCameraTypes;
        // GC.Alloc count inside ScriptableRenderContext.Submit of the last completed Render call (part of frameAllocations).
        internal static long nativeSubmitAllocations;
        static ProfilerRecorder s_FrameAllocations, s_ProxyAllocations, s_SubmitAllocations;
        static ProfilerRecorder Recorder(bool proxy)
        {
            ref var recorder = ref (proxy ? ref s_ProxyAllocations : ref s_FrameAllocations);
            if (!recorder.Valid)
                recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame);
            return recorder;
        }
        internal static void Dispose()
        {
            enabled = false;
            if (s_FrameAllocations.Valid)
            {
                s_FrameAllocations.Dispose();
            }
            if (s_ProxyAllocations.Valid)
            {
                s_ProxyAllocations.Dispose();
            }
            if (s_SubmitAllocations.Valid)
            {
                s_SubmitAllocations.Dispose();
            }
            s_FrameAllocations = default;
            s_ProxyAllocations = default;
            s_SubmitAllocations = default;
        }
        internal static long transformTicks, applyTicks;
        internal readonly struct NativeSubmitScope : IDisposable
        {
            readonly bool m_Enabled;
            internal NativeSubmitScope(bool enabledMeasurement)
            {
                m_Enabled = enabled && enabledMeasurement;
                if (!m_Enabled)
                {
                    return;
                }
                if (!s_SubmitAllocations.Valid)
                    s_SubmitAllocations = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                        ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame);
                s_SubmitAllocations.Reset();
                s_SubmitAllocations.Start();
            }
            public void Dispose()
            {
                if (!m_Enabled)
                {
                    return;
                }
                s_SubmitAllocations.Stop();
                if (s_SubmitAllocations.Count > 0)
                {
                    nativeSubmitAllocations += s_SubmitAllocations.GetSample(0).Count;
                }
            }
        }

        internal readonly struct Section : IDisposable
        {
            readonly long m_Start;
            readonly int m_Kind;
            internal Section(int kind)
            {
                m_Kind = kind;
                m_Start = enabled ? Stopwatch.GetTimestamp() : 0;
            }
            public void Dispose()
            {
                if (m_Start == 0)
                {
                    return;
                }
                long ticks = Stopwatch.GetTimestamp() - m_Start;
                if (m_Kind == 0)
                {
                    transformTicks = ticks;
                }
                else
                {
                    applyTicks = ticks;
                }
            }
        }

        internal static int CameraTypesOf(List<Camera> cameras)
        {
            int types = 0;
            for (int i = 0; i < cameras.Count; ++i)
            {
                types |= (int)cameras[i].cameraType;
            }
            return types;
        }

        internal readonly struct Scope : IDisposable
        {
            readonly bool m_Enabled, m_Proxy;
            readonly int m_CameraTypes;
            readonly long m_Ticks;
            readonly ProfilerRecorder m_Allocations;
            internal Scope(bool proxy, int cameraTypes)
            {
                m_Enabled = enabled;
                m_Proxy = proxy;
                m_CameraTypes = cameraTypes;
                if (m_Enabled && !proxy)
                {
                    nativeSubmitAllocations = 0;
                }
                m_Allocations = m_Enabled ? Recorder(proxy) : default;
                if (m_Enabled)
                {
                    m_Allocations.Reset();
                    m_Allocations.Start();
                }
                m_Ticks = m_Enabled ? Stopwatch.GetTimestamp() : 0;
            }
            public void Dispose()
            {
                if (!m_Enabled)
                {
                    return;
                }
                m_Allocations.Stop();
                long count = m_Allocations.Count == 0 ? 0 : m_Allocations.GetSample(0).Count;
                long ticks = Stopwatch.GetTimestamp() - m_Ticks;
                if (m_Proxy)
                {
                    proxyAllocations = count;
                    proxyTicks = ticks;
                }
                else
                {
                    frameAllocations = count;
                    frameTicks = ticks;
                    frameCameraTypes = m_CameraTypes;
                    ++sequence;
                }
            }
        }
    }
}
#endif
