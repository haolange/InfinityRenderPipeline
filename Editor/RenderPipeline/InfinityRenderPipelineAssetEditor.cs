using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Rendering.Pipeline.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InfinityRenderPipelineAsset))]
    public class InfinityRenderPipelineAssetEditor : UnityEditor.Editor
    {
        const string FoldoutPrefix = "InfinityRP.Asset.Foldout.";

        SerializedProperty m_UpdateProxy;
        SerializedProperty m_RayTrace;
        SerializedProperty m_SuperResolution;
        SerializedProperty m_RenderScale;
        SerializedProperty m_SRPBatch;
        SerializedProperty m_GPUInstance;
        SerializedProperty m_DynamicBatch;
        SerializedProperty m_QualityVolumeProfile;
        SerializedProperty m_AtmosphericalProfile;
        SerializedProperty m_DiffusionProfiles;
        SerializedProperty m_OutputMode;
        SerializedProperty m_HDREncoding;
        SerializedProperty m_CascadeShadowMapResolution;
        SerializedProperty m_LocalShadowMapResolution;
        SerializedProperty m_ShadowDistance;
        SerializedProperty m_DefaultShader;
        SerializedProperty m_DefaultMaterial;
        SerializedProperty m_DefaultParticleMaterial;
        SerializedProperty m_DefaultLineMaterial;
        SerializedProperty m_DefaultTerrainMaterial;
        SerializedProperty m_Default2DMaterial;

        void OnEnable()
        {
            m_UpdateProxy = serializedObject.FindProperty("updateProxy");
            m_RayTrace = serializedObject.FindProperty("enableRayTrace");
            m_SuperResolution = serializedObject.FindProperty("enableSuperResolution");
            m_RenderScale = serializedObject.FindProperty("m_RenderScale");
            m_SRPBatch = serializedObject.FindProperty("enableSRPBatch");
            m_GPUInstance = serializedObject.FindProperty("enableInstanceBatch");
            m_DynamicBatch = serializedObject.FindProperty("enableDynamicBatch");
            m_QualityVolumeProfile = serializedObject.FindProperty("m_QualityVolumeProfile");
            m_AtmosphericalProfile = serializedObject.FindProperty("atmosphericalProfile");
            m_DiffusionProfiles = serializedObject.FindProperty("diffusionProfiles");
            m_OutputMode = serializedObject.FindProperty("outputMode");
            m_HDREncoding = serializedObject.FindProperty("hdrEncoding");
            m_CascadeShadowMapResolution = serializedObject.FindProperty("cascadeShadowMapResolution");
            m_LocalShadowMapResolution = serializedObject.FindProperty("localShadowMapResolution");
            m_ShadowDistance = serializedObject.FindProperty("shadowDistance");
            m_DefaultShader = serializedObject.FindProperty("m_DefaultShader");
            m_DefaultMaterial = serializedObject.FindProperty("m_DefaultMaterial");
            m_DefaultParticleMaterial = serializedObject.FindProperty("m_DefaultParticleMaterial");
            m_DefaultLineMaterial = serializedObject.FindProperty("m_DefaultLineMaterial");
            m_DefaultTerrainMaterial = serializedObject.FindProperty("m_DefaultTerrainMaterial");
            m_Default2DMaterial = serializedObject.FindProperty("m_Default2DMaterial");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Rendering", "Rendering"))
            {
                EditorGUILayout.PropertyField(m_SuperResolution, new GUIContent("Super Resolution"));
                EditorGUILayout.PropertyField(m_RenderScale, new GUIContent("Render Scale"));
                EditorGUILayout.PropertyField(m_SRPBatch, new GUIContent("SRP Batcher"));
                EditorGUILayout.PropertyField(m_GPUInstance, new GUIContent("GPU Instancing"));
                EditorGUILayout.PropertyField(m_DynamicBatch, new GUIContent("Dynamic Batcher"));
                EditorGUILayout.PropertyField(m_UpdateProxy, new GUIContent("Refresh Renderer Proxy"));
                EditorGUILayout.PropertyField(m_RayTrace, new GUIContent("Unified Ray Tracing"));
            }
            InfinityInspectorGUI.EndFoldout();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Lighting", "Lighting & Shadows"))
            {
                EditorGUILayout.PropertyField(m_AtmosphericalProfile, new GUIContent("Atmospherical Profile"));
                EditorGUILayout.PropertyField(m_DiffusionProfiles, new GUIContent("Diffusion Profiles"), true);
                InfinityRenderPipelineAsset asset = target as InfinityRenderPipelineAsset;
                if (asset != null && asset.diffusionProfiles != null)
                {
                    for (int i = 0; i < asset.diffusionProfiles.Length; ++i)
                    {
                        DiffusionProfile profile = asset.diffusionProfiles[i];
                        if (profile == null)
                            EditorGUILayout.HelpBox("Diffusion profile slot " + i + " is empty.", MessageType.Warning);
                    }
                }
                EditorGUILayout.PropertyField(m_CascadeShadowMapResolution, new GUIContent("Cascade Shadow Map Resolution"));
                EditorGUILayout.PropertyField(m_LocalShadowMapResolution, new GUIContent("Local Shadow Map Resolution"));
                EditorGUILayout.PropertyField(m_ShadowDistance, new GUIContent("Shadow Distance"));
            }
            InfinityInspectorGUI.EndFoldout();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Post", "Post-processing & Output"))
            {
                EditorGUILayout.PropertyField(m_QualityVolumeProfile, new GUIContent("Quality Volume Profile"));
                EditorGUILayout.HelpBox("The project default Volume profile lives on Infinity Global Settings. This optional profile is the Quality layer passed to VolumeManager.Initialize.", MessageType.Info);
                EditorGUILayout.PropertyField(m_OutputMode, new GUIContent("Output Mode"));
                EditorGUILayout.PropertyField(m_HDREncoding, new GUIContent("HDR Encoding"));
            }
            InfinityInspectorGUI.EndFoldout();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Defaults", "Default Materials"))
            {
                EditorGUILayout.PropertyField(m_DefaultShader, new GUIContent("Default Shader"));
                EditorGUILayout.PropertyField(m_DefaultMaterial, new GUIContent("Default Material"));
                EditorGUILayout.PropertyField(m_DefaultParticleMaterial, new GUIContent("Default Particle Material"));
                EditorGUILayout.PropertyField(m_DefaultLineMaterial, new GUIContent("Default Line Material"));
                EditorGUILayout.PropertyField(m_DefaultTerrainMaterial, new GUIContent("Default Terrain Material"));
                EditorGUILayout.PropertyField(m_Default2DMaterial, new GUIContent("Default 2D Material"));
            }
            InfinityInspectorGUI.EndFoldout();

            EditorGUILayout.Space();
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeShaders shaders) && shaders != null)
            {
                EditorGUILayout.HelpBox(shaders.taaShader != null
                    ? "Runtime shaders resolved from Global Settings."
                    : "Runtime shaders are registered but TAA is missing. Open Project Settings > Graphics > Infinity RP.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Infinity Global Settings / RuntimeShaders are not registered. Open Project Settings > Graphics.", MessageType.Error);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
