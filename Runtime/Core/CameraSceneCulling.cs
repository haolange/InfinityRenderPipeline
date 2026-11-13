using UnityEngine;

namespace InfinityTech.Core
{
    // The scene-culling mask Unity applies for a camera: its override, else the Editor stage mask of its scene, else every
    // scene in a player.
    internal static class CameraSceneCulling
    {
        internal static ulong Mask(Camera camera)
        {
#if UNITY_EDITOR
            if (camera.overrideSceneCullingMask != 0)
                return camera.overrideSceneCullingMask;
            if (camera.scene.IsValid())
                return UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(camera.scene);
            return camera.cameraType == CameraType.SceneView
                ? UnityEditor.SceneManagement.SceneCullingMasks.MainStageSceneViewObjects
                : UnityEditor.SceneManagement.SceneCullingMasks.GameViewObjects;
#else
            return ulong.MaxValue;
#endif
        }
    }
}
