using UnityEngine;
using UnityEditor;

namespace InfinityTech.Rendering.Pipeline.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InfinityRenderPipelineAsset))]
    public class InfinityRenderPipelineAssetEditor : UnityEditor.Editor
    {
        public InfinityRenderPipelineAsset renderipelineAsset { get { return target as InfinityRenderPipelineAsset; } }
        public bool showShader { get { return renderipelineAsset.showShader; } set { renderipelineAsset.showShader = value; } }
        public bool showTexture { get { return renderipelineAsset.showTexture; } set { renderipelineAsset.showTexture = value; } }
        public bool showMaterial { get { return renderipelineAsset.showMaterial; } set { renderipelineAsset.showMaterial = value; } }
        public bool showAdvanced { get { return renderipelineAsset.showAdvanced; } set { renderipelineAsset.showAdvanced = value; } }

        private SerializedProperty m_UpdateProxy;
        private SerializedProperty m_RayTrace;
        private SerializedProperty m_SRPBatch;
        private SerializedProperty m_GPUInstance;
        private SerializedProperty m_DynamicBatch;

        private SerializedProperty m_SSRShader;
        private SerializedProperty m_SSAOShader;
        private SerializedProperty m_SSGIShader;
        private SerializedProperty m_TAAShader;

        private SerializedProperty m_DefaultShader;

        private SerializedProperty m_BlitMaterial;
        private SerializedProperty m_DefaultMaterial;

        private SerializedProperty m_BestFitNormalTexture;

        void OnEnable()
        {
            m_UpdateProxy = serializedObject.FindProperty("updateProxy");
            m_RayTrace = serializedObject.FindProperty("enableRayTrace");
            m_SRPBatch = serializedObject.FindProperty("enableSRPBatch");
            m_GPUInstance = serializedObject.FindProperty("enableInstanceBatch");
            m_DynamicBatch = serializedObject.FindProperty("enableDynamicBatch");

            m_DefaultShader = serializedObject.FindProperty("defaultShaderProxy");

            m_TAAShader = serializedObject.FindProperty("taaShader");
            m_SSRShader = serializedObject.FindProperty("ssrShader");
            m_SSAOShader = serializedObject.FindProperty("ssaoShader");
            m_SSGIShader = serializedObject.FindProperty("ssgiShader");

            m_BlitMaterial = serializedObject.FindProperty("blitMaterial");
            m_DefaultMaterial = serializedObject.FindProperty("defaultMaterialProxy");

            m_BestFitNormalTexture = serializedObject.FindProperty("bestFitNormalTexture");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            showShader = EditorGUILayout.BeginFoldoutHeaderGroup(showShader, "Shaders");
            if (showShader)
            {
                EditorGUILayout.PropertyField(m_DefaultShader, new GUIContent("Default Shader"), GUILayout.Height(18));
                EditorGUILayout.PropertyField(m_SSRShader, new GUIContent("SSR Shader"), GUILayout.Height(18));
                EditorGUILayout.PropertyField(m_TAAShader, new GUIContent("TAA Shader"), GUILayout.Height(18));
                EditorGUILayout.PropertyField(m_SSGIShader, new GUIContent("SSGI Shader"), GUILayout.Height(18));
                EditorGUILayout.PropertyField(m_SSAOShader, new GUIContent("SSAO Shader"), GUILayout.Height(18));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showTexture = EditorGUILayout.BeginFoldoutHeaderGroup(showTexture, "Textures");
            if (showTexture)
            {
                EditorGUILayout.PropertyField(m_BestFitNormalTexture, new GUIContent("Best Fit Normal LUT"), GUILayout.Height(18));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showMaterial = EditorGUILayout.BeginFoldoutHeaderGroup(showMaterial, "Materials");
            if (showMaterial)
            {
                EditorGUILayout.PropertyField(m_BlitMaterial, new GUIContent("Blit Material"), GUILayout.Height(18));
                EditorGUILayout.PropertyField(m_DefaultMaterial, new GUIContent("Default Material"), GUILayout.Height(18));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showAdvanced = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvanced, "Advanced");
            if (showAdvanced) 
            {
                EditorGUILayout.PropertyField(m_UpdateProxy, new GUIContent("RefreshProxy"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(m_RayTrace, new GUIContent("Ray Trace"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(m_SRPBatch, new GUIContent("SRP Batch"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(m_GPUInstance, new GUIContent("GPU Instance"), GUILayout.Height(25));
                EditorGUILayout.PropertyField(m_DynamicBatch, new GUIContent("Dynamic Batcher"), GUILayout.Height(25));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
