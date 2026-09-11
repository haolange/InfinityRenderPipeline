using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    internal static class RenderFaultValidationMenus
    {
        static bool s_Locked;
        [MenuItem("Window/Infinity/Faults/Run Dual Camera Suite")]
        static void Start()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("Start the fixed Game scene in Play mode before running faults.");
            Camera camera = ValidationSceneUtility.RequireActiveGameCamera();
            int gameCameras = 0;
            foreach (Camera active in Camera.allCameras)
                if (active.cameraType == CameraType.Game && active.isActiveAndEnabled) gameCameras++;
            if (gameCameras != 1) throw new InvalidOperationException("This fixture requires one original Game camera before creating its temporary second camera.");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "fault-suite-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            RenderFaultValidation.Start(new RenderFaultRequest { outputDirectory = path, scene = camera.gameObject.scene.name, camera = camera.name });
            EditorApplication.LockReloadAssemblies(); s_Locked = true;
            EditorApplication.update -= Pump; EditorApplication.update += Pump;
            Debug.Log("[InfinityRP] Fault suite started: " + path);
        }
        [MenuItem("Window/Infinity/Faults/Cancel And Drain")]
        static void Cancel() => RenderFaultValidation.current?.Cancel();
        static void Pump()
        {
            if (!EditorApplication.isPlaying) RenderFaultValidation.current?.Cancel(true);
            RenderCaptureService.current?.Pump();
            if (RenderFaultValidation.current == null || RenderFaultValidation.current.Finished)
            {
                EditorApplication.update -= Pump;
                if (s_Locked) EditorApplication.UnlockReloadAssemblies();
                s_Locked = false;
            }
        }
    }
}
