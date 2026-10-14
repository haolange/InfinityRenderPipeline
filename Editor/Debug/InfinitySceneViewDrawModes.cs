using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    /// <summary>
    /// Scene view Draw Modes: one section per Rendering Debugger panel ("Infinity Material", "Infinity Lighting",
    /// ...) listing its debug views. A Scene view in one of these modes renders that view through a per-camera override
    /// (InfinityDebugDisplaySettings.SetCameraDebugView); in any built-in mode it follows the Rendering Debugger.
    /// Built-in modes the pipeline does not render (the same list URP rejects) are disabled while Infinity is active.
    /// </summary>
    [InitializeOnLoad]
    static class InfinitySceneViewDrawModes
    {
        static readonly HashSet<SceneView> s_ValidatedViews = new HashSet<SceneView>();

        static InfinitySceneViewDrawModes()
        {
            foreach (EDebugViewPanel panel in DebugViewCatalog.Panels)
            {
                string section = DebugViewCatalog.SceneViewSection(panel);
                foreach (EDebugView view in DebugViewCatalog.ViewsOf(panel))
                {
                    if (view == EDebugView.None) continue;
                    try
                    {
                        SceneView.AddCameraMode(DebugViewCatalog.DisplayName(view), section);
                    }
                    catch (InvalidOperationException)
                    {
                        // Already registered in this domain.
                    }
                }
            }

            SceneView.beforeSceneGui -= SyncSceneView;
            SceneView.beforeSceneGui += SyncSceneView;
        }

        static bool InfinityActive => GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset;

        // Runs before the Scene view renders, so a mode change takes effect on the same repaint.
        static void SyncSceneView(SceneView sceneView)
        {
            if (sceneView == null || sceneView.camera == null) return;
            if (s_ValidatedViews.Add(sceneView))
            {
                sceneView.onValidateCameraMode += ValidateCameraMode;
            }

            SceneView.CameraMode mode = sceneView.cameraMode;
            if (mode.drawMode == DrawCameraMode.UserDefined && DebugViewCatalog.TryParseSceneViewMode(mode.section, mode.name, out EDebugView view))
            {
                InfinityDebugDisplaySettings.current.SetCameraDebugView(sceneView.camera, view);
            }
            else
            {
                InfinityDebugDisplaySettings.current.ClearCameraDebugView(sceneView.camera);
            }
        }

        static bool ValidateCameraMode(SceneView.CameraMode mode)
        {
            if (!InfinityActive) return true;
            switch (mode.drawMode)
            {
                case DrawCameraMode.ShadowCascades:
                case DrawCameraMode.RenderPaths:
                case DrawCameraMode.AlphaChannel:
                case DrawCameraMode.Overdraw:
                case DrawCameraMode.Mipmaps:
                case DrawCameraMode.SpriteMask:
                case DrawCameraMode.DeferredDiffuse:
                case DrawCameraMode.DeferredSpecular:
                case DrawCameraMode.DeferredSmoothness:
                case DrawCameraMode.DeferredNormal:
                case DrawCameraMode.ValidateAlbedo:
                case DrawCameraMode.ValidateMetalSpecular:
                case DrawCameraMode.TextureStreaming:
                    return false;
                default:
                    return true;
            }
        }
    }
}
