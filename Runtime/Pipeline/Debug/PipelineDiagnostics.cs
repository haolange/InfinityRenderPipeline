#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // What one frame produced, handed to diagnostics after the measured scope.
    internal readonly struct PipelineFrameResult
    {
        internal readonly Dictionary<int, CameraFrameState> cameraStates;
        internal readonly bool frameValid;
        internal readonly bool submitted;
        internal readonly Exception error;
        internal readonly int retiredResourceCount;
        internal readonly int retiredLightBufferCount;
        internal readonly bool canRetireSubmittedResources;

        internal PipelineFrameResult(Dictionary<int, CameraFrameState> cameraStates, bool frameValid, bool submitted, Exception error,
            int retiredResourceCount, int retiredLightBufferCount, bool canRetireSubmittedResources)
        {
            this.cameraStates = cameraStates;
            this.frameValid = frameValid;
            this.submitted = submitted;
            this.error = error;
            this.retiredResourceCount = retiredResourceCount;
            this.retiredLightBufferCount = retiredLightBufferCount;
            this.canRetireSubmittedResources = canRetireSubmittedResources;
        }
    }

    // A diagnostics or validation session that observes frames. Exists only in Editor and Development builds.
    internal abstract class PipelineFrameObserver
    {
        internal virtual void BeginFrame(List<Camera> cameras) { }
        internal virtual void BeforeCamera(in CameraSetup setup) { }
        internal virtual void AfterCamera(Camera camera, bool executed, Exception error) { }
        internal virtual bool LocksHistoryReset(Camera camera) => false;

        internal virtual bool TryOverrideBackend(Camera camera, out EMeshDrawBackendPolicy backend)
        {
            backend = default;
            return false;
        }

        internal virtual void AfterNativeSubmit() { }
        internal virtual void EndFrame(in PipelineFrameResult result) { }
        internal virtual void PipelineDisposed() { }
    }

    // The pipeline's only entry to diagnostics. Registered observers run first; normal-frame capture runs at its fixed place.
    internal static class PipelineDiagnostics
    {
        static readonly List<PipelineFrameObserver> s_Observers = new List<PipelineFrameObserver>(8);

        // The camera-motion fixture writes its phase here so capture evidence can record it.
        internal static string motionPhase;

        internal static void Register(PipelineFrameObserver observer)
        {
            if (!s_Observers.Contains(observer))
            {
                s_Observers.Add(observer);
            }
        }

        internal static void BeginFrame(List<Camera> cameras)
        {
            RenderCaptureService.Tick();
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                s_Observers[i].BeginFrame(cameras);
            }
        }

        internal static void BeforeCamera(in CameraSetup setup)
        {
            RenderCaptureService.current?.PrepareCamera(setup);
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                s_Observers[i].BeforeCamera(setup);
            }
        }

        internal static void AfterCamera(Camera camera, bool executed, Exception error)
        {
            RenderCaptureService.current?.CameraExecuted(camera, executed, error);
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                s_Observers[i].AfterCamera(camera, executed, error);
            }
        }

        internal static bool LocksHistoryReset(Camera camera)
        {
            if (RenderCaptureService.LockHistoryReset(camera))
            {
                return true;
            }
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                if (s_Observers[i].LocksHistoryReset(camera))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool TryOverrideBackend(Camera camera, out EMeshDrawBackendPolicy backend)
        {
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                if (s_Observers[i].TryOverrideBackend(camera, out backend))
                {
                    return true;
                }
            }
            return RenderCaptureService.TryOverrideBackend(camera, out backend);
        }

        internal static void AfterNativeSubmit()
        {
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                s_Observers[i].AfterNativeSubmit();
            }
        }

        internal static void EndFrame(in PipelineFrameResult result)
        {
            if (!result.frameValid)
            {
                RenderCaptureService.current?.Fail(result.error?.ToString() ?? "Frame submission failed.");
            }
            if (result.submitted)
            {
                RenderCaptureService.current?.AfterSubmit();
            }
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                s_Observers[i].EndFrame(result);
            }
        }

        internal static void PipelineDisposed()
        {
            RenderCaptureService.current?.Cancel("PipelineDisposed");
            for (int i = 0; i < s_Observers.Count; ++i)
            {
                s_Observers[i].PipelineDisposed();
            }
        }
    }
}
#endif
