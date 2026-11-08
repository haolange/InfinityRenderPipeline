using System;
using System.Diagnostics;
using Unity.Profiling;

namespace InfinityTech.Rendering.Pipeline
{
    // Bounded diagnostics only. Observers read completed samples outside the measured call chain.
    internal static class PipelineFrameMeasurement
    {
        internal static bool enabled;
        // GC.Alloc reports an allocation count. Byte size comes from the separate "GC Allocated In Frame" counter.
        internal static long frameBytes, proxyBytes, frameAllocations, proxyAllocations, frameTicks, proxyTicks, sequence;
        static ProfilerRecorder s_FrameAllocations, s_ProxyAllocations, s_SubmitAllocations, s_FrameAllocBytes, s_ProxyAllocBytes;
        internal static long nativeSubmitAllocations;
        static ProfilerRecorder Recorder(bool proxy)
        {
            ref var recorder = ref (proxy ? ref s_ProxyAllocations : ref s_FrameAllocations);
            if (!recorder.Valid)
                recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame);
            return recorder;
        }
        static ProfilerRecorder ByteRecorder(bool proxy)
        {
            ref var recorder = ref (proxy ? ref s_ProxyAllocBytes : ref s_FrameAllocBytes);
            if (!recorder.Valid)
                recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            return recorder;
        }
        internal static void ThrowIfAllocationByteCounterMissing()
        {
            using (var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
                if (!recorder.Valid) throw new InvalidOperationException("GC Allocated In Frame is not available, so allocation bytes cannot be recorded.");
        }
        internal static void Dispose()
        {
            enabled = false;
            if (s_FrameAllocations.Valid) s_FrameAllocations.Dispose();
            if (s_ProxyAllocations.Valid) s_ProxyAllocations.Dispose();
            if (s_SubmitAllocations.Valid) s_SubmitAllocations.Dispose();
            if (s_FrameAllocBytes.Valid) s_FrameAllocBytes.Dispose();
            if (s_ProxyAllocBytes.Valid) s_ProxyAllocBytes.Dispose();
            s_FrameAllocations = default; s_ProxyAllocations = default; s_SubmitAllocations = default;
            s_FrameAllocBytes = default; s_ProxyAllocBytes = default;
        }
        internal static long transformTicks, applyTicks;
        internal readonly struct NativeSubmitScope : IDisposable
        {
            readonly bool m_Enabled;
            internal NativeSubmitScope(bool enabledMeasurement)
            {
                m_Enabled = enabled && enabledMeasurement;
                if (!m_Enabled) return;
                if (!s_SubmitAllocations.Valid)
                    s_SubmitAllocations = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                        ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame);
                s_SubmitAllocations.Reset(); s_SubmitAllocations.Start();
            }
            public void Dispose()
            {
                if (!m_Enabled) return;
                s_SubmitAllocations.Stop();
                if (s_SubmitAllocations.Count > 0) nativeSubmitAllocations += s_SubmitAllocations.GetSample(0).Count;
            }
        }

        internal readonly struct Section : IDisposable
        {
            readonly long m_Start;
            readonly int m_Kind;
            internal Section(int kind) { m_Kind = kind; m_Start = enabled ? Stopwatch.GetTimestamp() : 0; }
            public void Dispose()
            {
                if (m_Start == 0) return;
                long ticks = Stopwatch.GetTimestamp() - m_Start;
                if (m_Kind == 0) transformTicks = ticks;
                else applyTicks = ticks;
            }
        }

        internal readonly struct Scope : IDisposable
        {
            readonly bool m_Enabled, m_Proxy;
            readonly long m_Ticks, m_BytesAtStart;
            readonly ProfilerRecorder m_Allocations, m_Bytes;
            internal Scope(bool proxy)
            {
                m_Enabled = enabled; m_Proxy = proxy;
                if (m_Enabled && !proxy) nativeSubmitAllocations = 0;
                m_Allocations = m_Enabled ? Recorder(proxy) : default;
                m_Bytes = m_Enabled ? ByteRecorder(proxy) : default;
                if (m_Enabled)
                {
                    m_Allocations.Reset(); m_Allocations.Start();
                    m_Bytes.Reset(); m_Bytes.Start();
                    m_BytesAtStart = m_Bytes.LastValue;
                }
                else m_BytesAtStart = 0;
                m_Ticks = m_Enabled ? Stopwatch.GetTimestamp() : 0;
            }
            public void Dispose()
            {
                if (!m_Enabled) return;
                m_Allocations.Stop();
                m_Bytes.Stop();
                long count = m_Allocations.Count == 0 ? 0 : m_Allocations.GetSample(0).Count;
                long end = m_Bytes.LastValue;
                long bytes = end >= m_BytesAtStart ? end - m_BytesAtStart : end;
                long ticks = Stopwatch.GetTimestamp() - m_Ticks;
                if (m_Proxy) { proxyBytes = bytes; proxyAllocations = count; proxyTicks = ticks; }
                else { frameBytes = bytes; frameAllocations = count; frameTicks = ticks; ++sequence; }
            }
        }
    }
}
