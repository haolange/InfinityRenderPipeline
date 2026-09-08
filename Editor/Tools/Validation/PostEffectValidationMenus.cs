using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    internal static class PostEffectValidationMenus
    {
        static bool s_ReloadLocked;
        [MenuItem("Infinity/Validation/Post Effects/Start Controlled Suite")]
        static void Start() => Start(true);
        [MenuItem("Infinity/Validation/Post Effects/Run Automatic Capture Suite")]
        static void StartAutomatic() => Start(false);
        static void Start(bool pauseForVisual)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("Use an already running Play-mode Game camera.");
            Camera camera = Validation.ValidationSceneUtility.RequireActiveGameCamera();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "post-effects-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            PostEffectValidation.Start(new PostEffectRequest { outputDirectory = output,
                camera = camera.name, scene = camera.gameObject.scene.name, pauseForVisual = pauseForVisual, stageDiagnostics = true });
            EditorApplication.LockReloadAssemblies(); s_ReloadLocked = true;
            EditorApplication.update -= Pump; EditorApplication.update += Pump;
            Debug.Log("[InfinityRP] Post-effect suite: " + output);
        }
        [MenuItem("Infinity/Validation/Post Effects/Advance To Next Phase")]
        static void Advance()
        {
            if (PostEffectValidation.current == null) throw new InvalidOperationException("No active post-effect suite.");
            PostEffectValidation.current.Advance();
        }
        [MenuItem("Infinity/Validation/Post Effects/Cancel And Restore")]
        static void Cancel() => PostEffectValidation.current?.Cancel();
        static void Pump()
        {
            PostEffectValidation.current?.Pump();
            if (PostEffectValidation.current == null || PostEffectValidation.current.Finished)
            {
                EditorApplication.update -= Pump;
                if (s_ReloadLocked) EditorApplication.UnlockReloadAssemblies();
                s_ReloadLocked = false;
            }
        }
    }
}
