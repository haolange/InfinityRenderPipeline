using UnityEngine;
using UnityEditor;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Editor.Component
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InfinityRenderPipelineAsset))]
    public class InfinityRenderPipelineAssetEditor : UnityEditor.Editor
    {
        public bool ResourceTab = true;
        public bool RenderingTab = true;

        public SerializedProperty SRPBatch;
        public SerializedProperty DynamicBatch;
        public SerializedProperty GPUInstance;
        public SerializedProperty RayTracing;

        public SerializedProperty DefaultShader;
        public SerializedProperty DefaultMaterial;

        void OnEnable()
        {
            SRPBatch = serializedObject.FindProperty("EnableSRPBatch");
            DynamicBatch = serializedObject.FindProperty("EnableDynamicBatch");
            GPUInstance = serializedObject.FindProperty("EnableInstanceBatch");
            RayTracing = serializedObject.FindProperty("EnableRayTracing");
            DefaultShader = serializedObject.FindProperty("DefaultShader");
            DefaultMaterial = serializedObject.FindProperty("DefaultMaterial");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            //////////////////////
            ResourceTab = EditorGUILayout.BeginFoldoutHeaderGroup(ResourceTab, "Resource");
            if (ResourceTab)
            {
                EditorGUILayout.PropertyField(DefaultShader, new GUIContent("Default Shader"), GUILayout.Height(18));
                EditorGUILayout.PropertyField(DefaultMaterial, new GUIContent("Default Material"), GUILayout.Height(18));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            RenderingTab = EditorGUILayout.BeginFoldoutHeaderGroup(RenderingTab, "Rendering");
            if (RenderingTab) {
                EditorGUILayout.PropertyField(RayTracing, new GUIContent("Ray Tracing"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(SRPBatch, new GUIContent("SRP Batcher"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(DynamicBatch, new GUIContent("Dynamic Batcher"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(GPUInstance, new GUIContent("GPU Instance"), GUILayout.Height(25));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            //////////////////////
            serializedObject.ApplyModifiedProperties();
        }
    }
}
