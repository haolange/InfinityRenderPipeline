using UnityEngine;
using UnityEditor;
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
        SerializedProperty m_SRPBatch;
        SerializedProperty m_GPUInstance;
        SerializedProperty m_DynamicBatch;

        SerializedProperty m_VolumeProfile;
        SerializedProperty m_AtmosphericalProfile;
        SerializedProperty m_DiffusionProfiles;
        SerializedProperty m_SubsurfaceQuality;

        SerializedProperty m_OutputMode;
        SerializedProperty m_HDREncoding;

        SerializedProperty m_MeshDrawPipelineCS;
        SerializedProperty m_TAAShader;
        SerializedProperty m_SSRShader;
        SerializedProperty m_SSAOShader;
        SerializedProperty m_SSGIShader;
        SerializedProperty m_CombineLUTShader;
        SerializedProperty m_HiZShader;
        SerializedProperty m_HalfResDownsampleShader;
        SerializedProperty m_ZBinningShader;
        SerializedProperty m_ContactShadowShader;
        SerializedProperty m_DeferredShadingShader;
        SerializedProperty m_SubsurfaceShader;
        SerializedProperty m_AtmosphericLUTShader;
        SerializedProperty m_VolumetricFogShader;
        SerializedProperty m_VolumetricCloudShader;
        SerializedProperty m_FogCompositeShader;
        SerializedProperty m_ColorPyramidShader;
        SerializedProperty m_ScreenSpaceCompositeShader;
        SerializedProperty m_SuperResolutionShader;
        SerializedProperty m_PostProcessingShader;
        SerializedProperty m_OutputTransformShader;

        SerializedProperty m_DefaultShader;
        SerializedProperty m_BlitMaterial;
        SerializedProperty m_DefaultMaterial;
        SerializedProperty m_BestFitNormalTexture;

        SerializedProperty m_CascadeShadowMapResolution;
        SerializedProperty m_LocalShadowMapResolution;
        SerializedProperty m_ShadowDistance;

        void OnEnable()
        {
            m_UpdateProxy = serializedObject.FindProperty("updateProxy");
            m_RayTrace = serializedObject.FindProperty("enableRayTrace");
            m_SuperResolution = serializedObject.FindProperty("enableSuperResolution");
            m_SRPBatch = serializedObject.FindProperty("enableSRPBatch");
            m_GPUInstance = serializedObject.FindProperty("enableInstanceBatch");
            m_DynamicBatch = serializedObject.FindProperty("enableDynamicBatch");

            m_VolumeProfile = serializedObject.FindProperty("m_VolumeProfile");
            m_AtmosphericalProfile = serializedObject.FindProperty("atmosphericalProfile");
            m_DiffusionProfiles = serializedObject.FindProperty("diffusionProfiles");
            m_SubsurfaceQuality = serializedObject.FindProperty("subsurfaceQuality");

            m_OutputMode = serializedObject.FindProperty("outputMode");
            m_HDREncoding = serializedObject.FindProperty("hdrEncoding");

            m_MeshDrawPipelineCS = serializedObject.FindProperty("meshDrawPipelineCS");
            m_TAAShader = serializedObject.FindProperty("taaShader");
            m_SSRShader = serializedObject.FindProperty("ssrShader");
            m_SSAOShader = serializedObject.FindProperty("ssaoShader");
            m_SSGIShader = serializedObject.FindProperty("ssgiShader");
            m_CombineLUTShader = serializedObject.FindProperty("combineLUTShader");
            m_HiZShader = serializedObject.FindProperty("hiZShader");
            m_HalfResDownsampleShader = serializedObject.FindProperty("halfResDownsampleShader");
            m_ZBinningShader = serializedObject.FindProperty("zBinningShader");
            m_ContactShadowShader = serializedObject.FindProperty("contactShadowShader");
            m_DeferredShadingShader = serializedObject.FindProperty("deferredShadingShader");
            m_SubsurfaceShader = serializedObject.FindProperty("subsurfaceShader");
            m_AtmosphericLUTShader = serializedObject.FindProperty("atmosphericLUTShader");
            m_VolumetricFogShader = serializedObject.FindProperty("volumetricFogShader");
            m_VolumetricCloudShader = serializedObject.FindProperty("volumetricCloudShader");
            m_FogCompositeShader = serializedObject.FindProperty("fogCompositeShader");
            m_ColorPyramidShader = serializedObject.FindProperty("colorPyramidShader");
            m_ScreenSpaceCompositeShader = serializedObject.FindProperty("screenSpaceCompositeShader");
            m_SuperResolutionShader = serializedObject.FindProperty("superResolutionShader");
            m_PostProcessingShader = serializedObject.FindProperty("postProcessingShader");
            m_OutputTransformShader = serializedObject.FindProperty("outputTransformShader");

            m_DefaultShader = serializedObject.FindProperty("defaultShaderProxy");
            m_BlitMaterial = serializedObject.FindProperty("blitMaterial");
            m_DefaultMaterial = serializedObject.FindProperty("defaultMaterialProxy");
            m_BestFitNormalTexture = serializedObject.FindProperty("bestFitNormalTexture");

            m_CascadeShadowMapResolution = serializedObject.FindProperty("cascadeShadowMapResolution");
            m_LocalShadowMapResolution = serializedObject.FindProperty("localShadowMapResolution");
            m_ShadowDistance = serializedObject.FindProperty("shadowDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            #region Profiles
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Profiles", "Profiles"))
            {
                EditorGUILayout.PropertyField(m_VolumeProfile, new GUIContent("Default Volume Profile"));
                EditorGUILayout.PropertyField(m_AtmosphericalProfile, new GUIContent("Atmospherical Profile"));
                EditorGUILayout.PropertyField(m_DiffusionProfiles, new GUIContent("Diffusion Profiles"), true);
                EditorGUILayout.PropertyField(m_SubsurfaceQuality, new GUIContent("SSS Quality"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Output
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Output", "Output"))
            {
                EditorGUILayout.PropertyField(m_OutputMode, new GUIContent("Output Mode"));
                EditorGUILayout.PropertyField(m_HDREncoding, new GUIContent("HDR Encoding"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Shadows
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Shadows", "Shadows"))
            {
                EditorGUILayout.PropertyField(m_CascadeShadowMapResolution, new GUIContent("Cascade Shadow Map Resolution"));
                EditorGUILayout.PropertyField(m_LocalShadowMapResolution, new GUIContent("Local Shadow Map Resolution"));
                EditorGUILayout.PropertyField(m_ShadowDistance, new GUIContent("Shadow Distance"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Compute Shaders
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Compute", "Compute Shaders", false))
            {
                EditorGUILayout.PropertyField(m_MeshDrawPipelineCS, new GUIContent("Mesh Draw Pipeline"));
                EditorGUILayout.PropertyField(m_TAAShader, new GUIContent("TAA"));
                EditorGUILayout.PropertyField(m_SSRShader, new GUIContent("SSR"));
                EditorGUILayout.PropertyField(m_SSGIShader, new GUIContent("SSGI"));
                EditorGUILayout.PropertyField(m_SSAOShader, new GUIContent("GTAO"));
                EditorGUILayout.PropertyField(m_HiZShader, new GUIContent("HiZ"));
                EditorGUILayout.PropertyField(m_HalfResDownsampleShader, new GUIContent("Half-Res Downsample"));
                EditorGUILayout.PropertyField(m_ZBinningShader, new GUIContent("Z-Binning"));
                EditorGUILayout.PropertyField(m_ContactShadowShader, new GUIContent("Contact Shadow"));
                EditorGUILayout.PropertyField(m_DeferredShadingShader, new GUIContent("Deferred Shading"));
                EditorGUILayout.PropertyField(m_SubsurfaceShader, new GUIContent("Subsurface"));
                EditorGUILayout.PropertyField(m_AtmosphericLUTShader, new GUIContent("Atmospheric LUT"));
                EditorGUILayout.PropertyField(m_VolumetricFogShader, new GUIContent("Volumetric Fog"));
                EditorGUILayout.PropertyField(m_VolumetricCloudShader, new GUIContent("Volumetric Cloud"));
                EditorGUILayout.PropertyField(m_FogCompositeShader, new GUIContent("Fog Composite"));
                EditorGUILayout.PropertyField(m_ColorPyramidShader, new GUIContent("Color Pyramid"));
                EditorGUILayout.PropertyField(m_ScreenSpaceCompositeShader, new GUIContent("Screen-Space Composite"));
                EditorGUILayout.PropertyField(m_CombineLUTShader, new GUIContent("Combine LUT"));
                EditorGUILayout.PropertyField(m_SuperResolutionShader, new GUIContent("Super Resolution"));
                EditorGUILayout.PropertyField(m_PostProcessingShader, new GUIContent("Post Processing"));
                EditorGUILayout.PropertyField(m_OutputTransformShader, new GUIContent("Output Transform"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Shaders
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Shaders", "Shaders", false))
            {
                EditorGUILayout.PropertyField(m_DefaultShader, new GUIContent("Default Shader"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Textures
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Textures", "Textures", false))
            {
                EditorGUILayout.PropertyField(m_BestFitNormalTexture, new GUIContent("Best Fit Normal LUT"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Materials
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Materials", "Materials", false))
            {
                EditorGUILayout.PropertyField(m_BlitMaterial, new GUIContent("Blit Material"));
                EditorGUILayout.PropertyField(m_DefaultMaterial, new GUIContent("Default Material"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Advanced
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Advanced", "Advanced"))
            {
                EditorGUILayout.PropertyField(m_RayTrace, new GUIContent("Ray Trace"));
                EditorGUILayout.PropertyField(m_SuperResolution, new GUIContent("Super Resolution"));
                EditorGUILayout.PropertyField(m_SRPBatch, new GUIContent("SRP Batch"));
                EditorGUILayout.PropertyField(m_GPUInstance, new GUIContent("GPU Instance"));
                EditorGUILayout.PropertyField(m_DynamicBatch, new GUIContent("Dynamic Batcher"));
                EditorGUILayout.PropertyField(m_UpdateProxy, new GUIContent("Refresh Renderer Proxy"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            serializedObject.ApplyModifiedProperties();
        }
    }
}
