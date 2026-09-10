using System;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    internal static class RenderingValidationCommands
    {
        static bool s_ReloadLocked;
        static EditorWindow s_GameCaptureView;
        static RenderCaptureSession s_CaptureSession;

        [CliCommand("infinity_render_state", "Read active cameras and baked lighting state without saving assets.")]
        public static object State() => new
        {
            project = Path.GetDirectoryName(Application.dataPath),
            pid = System.Diagnostics.Process.GetCurrentProcess().Id,
            unity = Application.unityVersion,
            playing = EditorApplication.isPlaying,
            lightmaps = LightmapSettings.lightmaps.Length,
            lightmapsMode = LightmapSettings.lightmapsMode.ToString(),
            shadowmaskMode = QualitySettings.shadowmaskMode.ToString(),
            cameras = Camera.allCameras.Concat(SceneView.GetAllSceneCameras()).Distinct().Select(c => new { c.name, entity = c.GetEntityId().ToString(),
                scene = c.gameObject.scene.path, sceneName = c.gameObject.scene.name, dirty = c.gameObject.scene.isDirty,
                c.pixelWidth, c.pixelHeight, type = c.cameraType.ToString() }).ToArray()
        };

        [CliCommand("infinity_capture_start", "Start a normal RenderGraph frame capture from an explicit request JSON file; returns acceptance, not completion.")]
        public static object CaptureStart(string requestPath)
        {
            if (EditorApplication.isCompiling || UnityEngine.FrameDebugger.enabled)
                throw new InvalidOperationException("Normal capture requires a running, noncompiling Editor.");
            var request = JsonUtility.FromJson<RenderCaptureRequest>(File.ReadAllText(Path.GetFullPath(requestPath)));
            if ((RenderCaptureService.current != null && !RenderCaptureService.current.Finished)
                || (CameraMotionValidation.current != null && !CameraMotionValidation.current.Finished)
                || (VisualLightingValidation.current != null && !VisualLightingValidation.current.Finished))
                throw new InvalidOperationException("An active validation session must finish draining before capture starts.");
            if (EditorApplication.isPaused && !request.requestRepaint)
                throw new InvalidOperationException("Paused capture requires explicit bounded normal-view repaint.");
            EditorWindow gameView = null;
            if (request.requestRepaint && request.cameraType == CameraType.Game)
                gameView = Resources.FindObjectsOfTypeAll<EditorWindow>().Single(w => w.GetType().FullName == "UnityEditor.GameView");
            if (request.cameraType == CameraType.Game && !EditorApplication.isPlaying)
                throw new InvalidOperationException("Game capture requires Play mode; SceneView capture uses normal Editor repaint.");
            var matches = Camera.allCameras.Concat(SceneView.GetAllSceneCameras()).Distinct().Where(c => request.Matches(c)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Select exactly one camera by requested type, scene and exact name or entity.");
            request.cameraEntity = matches[0].GetEntityId().ToString();
            // A completed session may still have its final update callback queued.
            StopCapturePump();
            RenderCaptureService.Start(request);
            s_GameCaptureView = gameView;
            s_CaptureSession = RenderCaptureService.current;
            EditorApplication.LockReloadAssemblies();
            s_ReloadLocked = true;
            EditorApplication.update += Pump;
            return CaptureStatus();
        }

        [CliCommand("infinity_capture_status", "Read capture status and outstanding retirement count.")]
        public static object CaptureStatus() => new { status = RenderCaptureService.current?.Status ?? "Idle",
            finished = RenderCaptureService.current?.Finished ?? true,
            outstanding = RenderCaptureService.current?.OutstandingCount ?? 0,
            evidence = RenderCaptureService.current?.request.outputDirectory };

        [CliCommand("infinity_capture_cancel", "Cancel capture and continue draining owned staging resources.")]
        public static object CaptureCancel() { RenderCaptureService.Cancel(); return CaptureStatus(); }

        static System.Reflection.MethodInfo TestMethod(string name)
        {
            var type = Type.GetType("InfinityTech.Rendering.Tests.ValidationTestRunner, Unity.RenderPipelines.Infinity.Tests", true);
            return type.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new MissingMethodException(type.FullName, name);
        }

        [CliCommand("infinity_tests_start", "Run the shared Infinity EditMode test engine with real XML and a new evidence directory.")]
        public static object TestsStart(string outputDirectory, string filter = null)
            => TestMethod("RunValidation").Invoke(null, new object[] { outputDirectory, filter });

        [CliCommand("infinity_tests_status", "Read the active XML test run identity and evidence directory.")]
        public static object TestsStatus() => TestMethod("Status").Invoke(null, null);

        [CliCommand("infinity_tests_cancel", "Cancel the active shared test run.")]
        public static void TestsCancel() => TestMethod("Cancel").Invoke(null, null);

        [CliCommand("infinity_visual_fixture_start", "Run transient native/Mesh CPU/GPU baked-lighting and mixed-shadow fixtures, restoring original cameras/lights/maps after drain.")]
        public static object VisualStart(string outputDirectory, bool pauseForVisual = false, string scenario = null)
        {
            VisualLightingValidation.Start(outputDirectory, pauseForVisual, scenario);
            EditorApplication.LockReloadAssemblies();
            EditorApplication.update += PumpVisual;
            return VisualStatus();
        }
        [CliCommand("infinity_visual_fixture_status", "Read fixture progress and restoration state.")]
        public static object VisualStatus() => VisualLightingValidation.current?.State;
        [CliCommand("infinity_visual_fixture_cancel", "Cancel fixtures, drain pending capture, and restore owned temporary state.")]
        public static void VisualCancel() => VisualLightingValidation.current?.Cancel();
        [CliCommand("infinity_visual_fixture_advance", "Continue a fixture paused for actual window inspection.")]
        public static void VisualAdvance() => VisualLightingValidation.current?.Advance();

        static void PumpVisual()
        {
            VisualLightingValidation.current?.Tick();
            if (VisualLightingValidation.current != null && !VisualLightingValidation.current.Finished) return;
            EditorApplication.update -= PumpVisual;
            EditorApplication.UnlockReloadAssemblies();
        }

        [CliCommand("infinity_mesh_state", "Read current MeshScene instance/draw data for the selected Editor without altering it.")]
        public static object MeshState()
        {
            var pipeline = RenderPipelineManager.currentPipeline as InfinityRenderPipeline;
            if (pipeline == null) throw new InvalidOperationException("Infinity is not the active pipeline.");
            var scene = pipeline.renderContext.GetMeshScene();
            var instances = scene.GetInstances();
            var data = new System.Collections.Generic.List<object>();
            for (int i = 0; i < scene.InstanceHighWater; i++)
            {
                var instance = instances[i];
                data.Add(new { slot = i, transform = instance.transform.Index, bounds = instance.worldBounds.ToString(),
                    layer = instance.layerMask, flags = instance.flags.ToString(), baked = instance.bakedLighting.TextureSet, draws = instance.drawCount });
            }
            return new { count = scene.LogicalInstanceCount, draws = scene.DrawCount, instances = data };
        }

        [CliCommand("infinity_scene_view_dock", "Restore an explicitly selected floating SceneView beside the sole Game tab; does not save assets.")]
        public static object DockSceneView(string cameraEntity)
        {
            var view = SceneView.sceneViews.Cast<SceneView>().Single(v => v.camera.GetEntityId().ToString() == cameraEntity);
            SceneViewDockSnapshot.DockBeforeGame(view);
            return new { cameraEntity, docked = view.docked };
        }

        [CliCommand("infinity_camera_motion_start", "Run normal-frame camera motion, disocclusion, resize and camera-switch captures; restore the original camera after drain.")]
        public static object MotionStart(string requestPath)
        {
            CameraMotionValidation.Start(JsonUtility.FromJson<CameraMotionRequest>(File.ReadAllText(requestPath)));
            EditorApplication.LockReloadAssemblies(); EditorApplication.update += PumpMotion;
            return MotionStatus();
        }
        [CliCommand("infinity_camera_motion_status", "Read camera-motion validation status.")]
        public static object MotionStatus() => CameraMotionValidation.current?.State;
        [CliCommand("infinity_camera_motion_cancel", "Cancel motion capture, drain and restore the selected camera.")]
        public static void MotionCancel() => CameraMotionValidation.current?.Cancel();
        static void PumpMotion()
        {
            CameraMotionValidation.current?.Tick();
            if (CameraMotionValidation.current == null || CameraMotionValidation.current.Finished)
            { EditorApplication.update -= PumpMotion; EditorApplication.UnlockReloadAssemblies(); }
        }

        [CliCommand("infinity_frame_tree_start", "Export the explicitly selected Game camera frame tree through the existing engine, restoring native state afterwards.")]
        public static object FrameTreeStart(string outputDirectory, string camera, string scene) => FrameDebuggerCapture.Start(outputDirectory, camera, scene);
        [CliCommand("infinity_frame_tree_status", "Read frame tree status and evidence.")]
        public static object FrameTreeStatus() => FrameDebuggerCapture.Status();
        [CliCommand("infinity_frame_tree_cancel", "Cancel frame tree capture and restore Frame Debugger and pause state.")]
        public static void FrameTreeCancel() => FrameDebuggerCapture.Cancel();

        static void Pump()
        {
            var session = s_CaptureSession;
            if (session != null && session.request.cameraType == CameraType.Game && !EditorApplication.isPlaying) session.Cancel("PlayModeEnded");
            if (session != null && !session.Finished && session.request.cameraType == CameraType.SceneView && session.request.requestRepaint)
                foreach (SceneView view in SceneView.sceneViews)
                    if (session.request.Matches(view.camera)) view.Repaint();
            if (session != null && !session.Finished && session.request.cameraType == CameraType.Game && session.request.requestRepaint)
            {
                if (s_GameCaptureView == null) session.Cancel("Selected GameView was closed.");
                else { EditorApplication.QueuePlayerLoopUpdate(); s_GameCaptureView.Repaint(); }
            }
            session?.Pump();
            if (session != null && !session.Finished) return;
            StopCapturePump();
        }
        static void StopCapturePump()
        {
            EditorApplication.update -= Pump;
            if (s_ReloadLocked) EditorApplication.UnlockReloadAssemblies();
            s_ReloadLocked = false;
            s_GameCaptureView = null;
            s_CaptureSession = null;
        }
    }
}
