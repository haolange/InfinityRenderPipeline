using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal sealed class NativeDisplayProbe : RenderGraph.IRGPassQueueObserver
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Result
        {
            public ulong pixelFormat, width, height, samples;
            public int status;
        }
        [Serializable] internal sealed class Evidence
        {
            public string source = "Metal.CurrentRenderPassDescriptor.colorAttachments[0]", format, error;
            public ulong pixelFormat, width, height, samples;
            public int frame;
            public bool retired;
        }
        internal readonly Evidence evidence = new Evidence();
        internal readonly CaptureRetirement lifetime = new CaptureRetirement();
        public void OnQueued() => lifetime.recorded = true;
        public void OnQueueFailed() => lifetime.QueueFailed();
        internal IntPtr payload;
        internal IntPtr callback;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        const string Library = "InfinityCaptureMetal";
        [DllImport(Library)] static extern IntPtr InfinityCaptureMetalEvent();
        [DllImport(Library)] static extern int InfinityCaptureMetalReady(IntPtr payload);
        [DllImport(Library)] static extern IntPtr InfinityCaptureMetalFormatName(ulong value);
#endif
        internal static NativeDisplayProbe Create()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal) return null;
            var probe = new NativeDisplayProbe();
            probe.callback = InfinityCaptureMetalEvent();
            if (probe.callback == IntPtr.Zero) throw new InvalidOperationException("Metal display-probe callback is unavailable.");
            probe.payload = Marshal.AllocHGlobal(Marshal.SizeOf<Result>());
            Marshal.StructureToPtr(default(Result), probe.payload, false);
            probe.evidence.frame = Time.frameCount;
            return probe;
#else
            return null;
#endif
        }
        internal bool Poll()
        {
            if (lifetime.ResolveUncertainQueue())
                evidence.error = "Uncertain Present queue completed without a native probe callback.";
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (lifetime.recorded && !lifetime.terminal)
            {
                int ready = InfinityCaptureMetalReady(payload);
                if (ready != 0)
                {
                    Result result = Marshal.PtrToStructure<Result>(payload);
                    evidence.pixelFormat = result.pixelFormat;
                    evidence.width = result.width; evidence.height = result.height; evidence.samples = result.samples;
                    evidence.format = Marshal.PtrToStringAnsi(InfinityCaptureMetalFormatName(result.pixelFormat));
                    if (ready < 0) evidence.error = "Present had no native Metal color attachment at the plugin event.";
                    lifetime.terminal = true;
                }
            }
#endif
            if (!lifetime.CanRetire) return false;
            lifetime.Retire();
            Marshal.FreeHGlobal(payload); payload = IntPtr.Zero;
            evidence.retired = true;
            return true;
        }
    }
}
