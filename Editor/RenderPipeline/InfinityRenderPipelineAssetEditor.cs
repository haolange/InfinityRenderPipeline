using UnityEngine;
using UnityEditor;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Editor.Component
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InfinityRenderPipelineAsset))]
    public class InfinityRenderPipelineAssetEditor : UnityEditor.Editor
    {
        public bool showShader = false;
        public bool showMaterial = false;
        public bool showAdvanced = true;

        private SerializedProperty m_RayTracing;
        private SerializedProperty m_SRPBatch;
        private SerializedProperty m_GPUInstance;
        private SerializedProperty m_DynamicBatch;
        private SerializedProperty m_DefaultShader;
        private SerializedProperty m_DefaultMaterial;

        void OnEnable()
        {
            m_RayTracing = serializedObject.FindProperty("EnableRayTracing");

            m_SRPBatch = serializedObject.FindProperty("EnableSRPBatch");
            m_GPUInstance = serializedObject.FindProperty("EnableInstanceBatch");
            m_DynamicBatch = serializedObject.FindProperty("EnableDynamicBatch");

            m_DefaultShader = serializedObject.FindProperty("DefaultShader");
            m_DefaultMaterial = serializedObject.FindProperty("DefaultMaterial");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            //////////////////////
            showShader = EditorGUILayout.BeginFoldoutHeaderGroup(showShader, "Shaders");
            if (showShader)
            {
                EditorGUILayout.PropertyField(m_DefaultShader, new GUIContent("Default Shader"), GUILayout.Height(18));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showMaterial = EditorGUILayout.BeginFoldoutHeaderGroup(showMaterial, "Materials");
            if (showMaterial)
            {
                EditorGUILayout.PropertyField(m_DefaultMaterial, new GUIContent("Default Material"), GUILayout.Height(18));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showAdvanced = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvanced, "Advanced");
            if (showAdvanced) 
            {
                EditorGUILayout.PropertyField(m_RayTracing, new GUIContent("Ray Trace"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(m_SRPBatch, new GUIContent("SRP Batch"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(m_GPUInstance, new GUIContent("GPU Instance"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(m_DynamicBatch, new GUIContent("Dynamic Batcher"), GUILayout.Height(25));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            //////////////////////
            serializedObject.ApplyModifiedProperties();
        }
    }
}
