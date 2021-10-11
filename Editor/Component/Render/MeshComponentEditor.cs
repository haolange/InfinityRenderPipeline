using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MeshComponent))]
    public class MeshComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
        }

        [MenuItem("GameObject/3D Object/Infinity/MeshEntity", false, -1000)]
        public static void CreatePrimitiveEntity(MenuCommand menuCommand)
        {
            GameObject MeshEntity = new GameObject("MeshEntity");
            MeshEntity.AddComponent<MeshComponent>();
            GameObjectUtility.SetParentAndAlign(MeshEntity, menuCommand.context as GameObject);
            StageUtility.PlaceGameObjectInCurrentStage(MeshEntity);
            GameObjectUtility.EnsureUniqueNameForSibling(MeshEntity);
            Undo.RegisterCreatedObjectUndo(MeshEntity, "Create " + MeshEntity.name);
            Selection.activeObject = MeshEntity;
        }
    }
}

