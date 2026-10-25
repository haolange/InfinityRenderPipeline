using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MeshComponent))]
    public class MeshComponentEditor : UnityEditor.Editor
    {
        const string FoldoutPrefix = "InfinityRP.Mesh.Foldout.";

        SerializedProperty m_Movebility;
        SerializedProperty m_MeshAsset;
        SerializedProperty m_Materials;
        SerializedProperty m_CastShadow;
        SerializedProperty m_ReceiveShadow;
        SerializedProperty m_AffectIndirectLighting;
        SerializedProperty m_Visible;
        SerializedProperty m_RenderingLayer;
        SerializedProperty m_RenderPriority;
        SerializedProperty m_MotionVector;

        void OnEnable()
        {
            m_Movebility = serializedObject.FindProperty("movebility");
            m_MeshAsset = serializedObject.FindProperty("meshAsset");
            m_Materials = serializedObject.FindProperty("materials");
            m_CastShadow = serializedObject.FindProperty("castShadow");
            m_ReceiveShadow = serializedObject.FindProperty("receiveShadow");
            m_AffectIndirectLighting = serializedObject.FindProperty("affectIndirectLighting");
            m_Visible = serializedObject.FindProperty("visible");
            m_RenderingLayer = serializedObject.FindProperty("renderingLayer");
            m_RenderPriority = serializedObject.FindProperty("renderPriority");
            m_MotionVector = serializedObject.FindProperty("motionVector");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            #region State
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "State", "State"))
            {
                EditorGUILayout.PropertyField(m_Movebility, new GUIContent("Mobility"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Mesh
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Mesh", "Mesh"))
            {
                EditorGUILayout.PropertyField(m_MeshAsset, new GUIContent("Mesh"));
                EditorGUILayout.PropertyField(m_Materials, new GUIContent("Materials"), true);
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Lighting
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Lighting", "Lighting"))
            {
                EditorGUILayout.PropertyField(m_CastShadow, new GUIContent("Cast Shadow"));
                EditorGUILayout.PropertyField(m_ReceiveShadow, new GUIContent("Receive Shadow"));
                EditorGUILayout.PropertyField(m_AffectIndirectLighting, new GUIContent("Affect Indirect"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Rendering
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Rendering", "Rendering"))
            {
                EditorGUILayout.PropertyField(m_Visible, new GUIContent("Visible"));
                EditorGUILayout.PropertyField(m_RenderingLayer, new GUIContent("Rendering Layer"));
                EditorGUILayout.PropertyField(m_RenderPriority, new GUIContent("Render Priority"));
                EditorGUILayout.PropertyField(m_MotionVector, new GUIContent("Motion Vector"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            serializedObject.ApplyModifiedProperties();
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
