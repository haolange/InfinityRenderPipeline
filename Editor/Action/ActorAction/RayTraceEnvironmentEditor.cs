using UnityEngine;
using UnityEditor;
using InfinityTech.Component;

namespace InfinityTech.ActorAction.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(RayTraceEnvironment))]
    public class RayTraceEnvironmentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {

        }

        [MenuItem("GameObject/Light/RayTraceMannager", false)]
        public static void CreateRayTraceMannagerEntity(MenuCommand menuCommand)
        {
            RayTraceEnvironment RayTraceMannagerObject = FindAnyObjectByType<RayTraceEnvironment>();

            if(RayTraceMannagerObject == null) {
                GameObject RayTraceMannagerEntity = new GameObject("RayTraceMannager");
                GameObjectUtility.SetParentAndAlign(RayTraceMannagerEntity, menuCommand.context as GameObject);
                RayTraceEnvironment RayTraceMannagerComponent = RayTraceMannagerEntity.AddComponent<RayTraceEnvironment>();
                Undo.RegisterCreatedObjectUndo(RayTraceMannagerEntity, "Create " + RayTraceMannagerEntity.name);
                Selection.activeObject = RayTraceMannagerEntity;
            } else {
                Debug.LogWarning("Scene allready have RayTraceMannager");
            }
        }
    }
}
