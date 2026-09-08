using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    internal static class NormalFrameCaptureMenus
    {
        static bool s_ReloadLocked;
        [MenuItem("Infinity/Validation/Capture/Normal Beauty And Confidence")]
        static void Start() => StartCapture(false);
        [MenuItem("Infinity/Validation/Capture/Normal Beauty With ZBin Overflow")]
        static void StartWithOverflow() => StartCapture(true);

        static void StartCapture(bool includeOverflow)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("Use an already running Play-mode Game camera for a normal-frame capture.");
            Camera camera = Validation.ValidationSceneUtility.RequireActiveGameCamera();
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "normal-frame-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            RenderCaptureService.Start(new RenderCaptureRequest
            {
                outputDirectory = directory, fixture = "CurrentSceneBaseline", scene = camera.gameObject.scene.name,
                camera = camera.name, width = camera.pixelWidth, height = camera.pixelHeight,
                warmupFrames = 120, frameCount = 3, frameInterval = 1, includeConfidence = true, includeZBinOverflow = includeOverflow,
                buffers = new[] { "DisplayColor", "Lighting" },
                roi = new RectInt(0, 0, camera.pixelWidth, camera.pixelHeight)
            });
            EditorApplication.LockReloadAssemblies();
            s_ReloadLocked = true;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("[InfinityRP] Normal-frame capture requested: " + directory);
        }
        [MenuItem("Infinity/Validation/Capture/Cancel And Drain")]
        static void Cancel() => RenderCaptureService.Cancel();
        static void Pump()
        {
            if (!EditorApplication.isPlaying) RenderCaptureService.current?.Cancel("PlayModeEnded");
            RenderCaptureService.current?.Pump();
            if (RenderCaptureService.current == null || RenderCaptureService.current.Finished)
            {
                EditorApplication.update -= Pump;
                if (s_ReloadLocked) EditorApplication.UnlockReloadAssemblies();
                s_ReloadLocked = false;
            }
        }
    }
}
