using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using InfinityTech.Component;

namespace InfinityTech.Rendering.Editor.Validation
{
    internal static class ValidationSceneUtility
    {
        internal static Camera RequireActiveGameCamera()
        {
            Camera selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Camera>() : null;
            if (selected != null)
            {
                if (!selected.isActiveAndEnabled || selected.cameraType != CameraType.Game)
                    throw new System.InvalidOperationException("The selected camera must be an active Game camera.");
                return selected;
            }
            Camera result = null;
            foreach (Camera camera in Camera.allCameras)
            {
                if (!camera.isActiveAndEnabled || camera.cameraType != CameraType.Game) continue;
                if (result != null)
                    throw new System.InvalidOperationException("Multiple Game cameras are active. Select the intended camera before capture.");
                result = camera;
            }
            return result != null ? result : throw new System.InvalidOperationException("An active Game camera is required.");
        }

        [MenuItem("Window/Infinity/Add Liveness Marker", false, 62)]
        static void AddLivenessMarker()
        {
            Camera camera = RequireActiveGameCamera();
            LivenessMarker marker = EnsureLitLivenessMarker(camera);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
        }
        internal static LivenessMarker EnsureLitLivenessMarker(Camera camera)
        {
            LivenessMarker marker = LivenessMarkerUtility.EnsureInScene(camera);
            Material material = LivenessMarkerUtility.ApplyLitMaterial(marker.gameObject, new Color(0.85f, 0.15f, 0.05f, 1));
            InfinityTech.Rendering.Pipeline.MaterialRouteUtility.ApplyPassState(material);
            return marker;
        }
    }
}
