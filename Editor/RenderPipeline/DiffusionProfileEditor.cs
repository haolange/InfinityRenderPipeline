using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.Editor
{
    [CustomEditor(typeof(DiffusionProfile))]
    public sealed class DiffusionProfileEditor : UnityEditor.Editor
    {
        const string FoldoutKey = "InfinityRP.Diffusion.Foldout.Scatter";

        SerializedProperty m_ScatterColor;
        SerializedProperty m_ScatterDistance;
        SerializedProperty m_SurfaceAlbedo;
        SerializedProperty m_MaxRadius;

        void OnEnable()
        {
            m_ScatterColor = serializedObject.FindProperty("scatterColor");
            m_ScatterDistance = serializedObject.FindProperty("scatterDistance");
            m_SurfaceAlbedo = serializedObject.FindProperty("surfaceAlbedo");
            m_MaxRadius = serializedObject.FindProperty("maxRadius");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutKey, "Scatter"))
            {
                EditorGUILayout.PropertyField(m_ScatterColor, new GUIContent("Scatter Color"));
                EditorGUILayout.PropertyField(m_ScatterDistance, new GUIContent("Scatter Distance"));
                EditorGUILayout.PropertyField(m_SurfaceAlbedo, new GUIContent("Surface Albedo"));
                EditorGUILayout.PropertyField(m_MaxRadius, new GUIContent("Max Radius"));
            }
            InfinityInspectorGUI.EndFoldout();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
