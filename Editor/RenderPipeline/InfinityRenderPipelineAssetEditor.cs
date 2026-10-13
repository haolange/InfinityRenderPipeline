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
        SerializedProperty m_CascadeSplitRatios;
        SerializedProperty m_MaxShadowedLocalLights;
        SerializedProperty m_CascadeCount;
        SerializedProperty m_CachedShadowAtlasWidth;
        SerializedProperty m_CookieAtlasResolution;
        SerializedProperty m_LightProbeSystem;
        SerializedProperty m_ProbeVolumeMemoryBudget;
        SerializedProperty m_ProbeVolumeBlendingMemoryBudget;
        SerializedProperty m_ProbeVolumeSHBands;
        SerializedProperty m_ProbeVolumeGPUStreaming;
        SerializedProperty m_ProbeVolumeDiskStreaming;
        SerializedProperty m_ProbeVolumeScenarios;
        SerializedProperty m_ProbeVolumeScenarioBlending;
        SerializedProperty m_CookieMaxSize;
        SerializedProperty m_CascadeBlendWidth;
        SerializedProperty m_ShadowFilterQuality;
        SerializedProperty m_CustomShadowFilter;
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
            m_CascadeSplitRatios = serializedObject.FindProperty("cascadeSplitRatios");
            m_MaxShadowedLocalLights = serializedObject.FindProperty("maxShadowedLocalLights");
            m_CascadeCount = serializedObject.FindProperty("cascadeCount");
            m_CachedShadowAtlasWidth = serializedObject.FindProperty("cachedShadowAtlasWidth");
            m_CookieAtlasResolution = serializedObject.FindProperty("cookieAtlasResolution");
            m_LightProbeSystem = serializedObject.FindProperty("lightProbeSystem");
            m_ProbeVolumeMemoryBudget = serializedObject.FindProperty("probeVolumeMemoryBudget");
            m_ProbeVolumeBlendingMemoryBudget = serializedObject.FindProperty("probeVolumeBlendingMemoryBudget");
            m_ProbeVolumeSHBands = serializedObject.FindProperty("probeVolumeSHBands");
            m_ProbeVolumeGPUStreaming = serializedObject.FindProperty("supportProbeVolumeGPUStreaming");
            m_ProbeVolumeDiskStreaming = serializedObject.FindProperty("supportProbeVolumeDiskStreaming");
            m_ProbeVolumeScenarios = serializedObject.FindProperty("supportProbeVolumeScenarios");
            m_ProbeVolumeScenarioBlending = serializedObject.FindProperty("supportProbeVolumeScenarioBlending");
            m_CookieMaxSize = serializedObject.FindProperty("cookieMaxSize");
            m_CascadeBlendWidth = serializedObject.FindProperty("cascadeBlendWidth");
            m_ShadowFilterQuality = serializedObject.FindProperty("shadowFilterQuality");
            m_CustomShadowFilter = serializedObject.FindProperty("customShadowFilter");
            m_DefaultShader = serializedObject.FindProperty("m_DefaultShader");
            m_DefaultMaterial = serializedObject.FindProperty("m_DefaultMaterial");
            m_DefaultParticleMaterial = serializedObject.FindProperty("m_DefaultParticleMaterial");
            m_DefaultLineMaterial = serializedObject.FindProperty("m_DefaultLineMaterial");
            m_DefaultTerrainMaterial = serializedObject.FindProperty("m_DefaultTerrainMaterial");
            m_Default2DMaterial = serializedObject.FindProperty("m_Default2DMaterial");
        }

        // Filter quality follows the Quality preset pattern: Low..Ultra show the hardcoded preset read-only, Custom edits
        // the asset's own copy.
        void DrawShadowFilter()
        {
            EditorGUILayout.PropertyField(m_ShadowFilterQuality, new GUIContent("Shadow Filter Quality", "Low..Ultra select QualityPresets.ShadowFilter; Custom uses the values below."));
            EQualityLevel level = (EQualityLevel)m_ShadowFilterQuality.enumValueIndex;
            ++EditorGUI.indentLevel;
            if (level == EQualityLevel.Custom || m_ShadowFilterQuality.hasMultipleDifferentValues)
            {
                foreach (SerializedProperty child in ChildProperties(m_CustomShadowFilter))
                    EditorGUILayout.PropertyField(child);
            }
            else
            {
                FShadowFilterSettings preset = QualityPresets.Select(QualityPresets.ShadowFilter, level);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField("Pcf Radius", preset.pcfRadius);
                    EditorGUILayout.IntField("Pcss Blocker Samples", preset.pcssBlockerSamples);
                    EditorGUILayout.IntField("Pcss Filter Samples", preset.pcssFilterSamples);
                    EditorGUILayout.FloatField("Pcss Max Penumbra", preset.pcssMaxPenumbra);
                    EditorGUILayout.FloatField("Evsm Exponent", preset.evsmExponent);
                    EditorGUILayout.FloatField("Light Bleeding Reduction", preset.lightBleedingReduction);
                    EditorGUILayout.IntField("Moment Blur Radius", preset.momentBlurRadius);
                }
                if (GUILayout.Button("Copy Preset To Custom"))
                {
                    foreach (Object asset in targets)
                    {
                        if (asset is InfinityRenderPipelineAsset pipelineAsset)
                        {
                            Undo.RecordObject(pipelineAsset, "Copy Shadow Filter Preset");
                            pipelineAsset.customShadowFilter = preset;
                            pipelineAsset.shadowFilterQuality = EQualityLevel.Custom;
                            EditorUtility.SetDirty(pipelineAsset);
                        }
                    }
                    serializedObject.Update();
                }
            }
            --EditorGUI.indentLevel;
        }

        static System.Collections.Generic.IEnumerable<SerializedProperty> ChildProperties(SerializedProperty parent)
        {
            SerializedProperty iterator = parent.Copy();
            SerializedProperty end = parent.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                yield return iterator.Copy();
            }
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
                EditorGUILayout.PropertyField(m_CascadeCount, new GUIContent("Cascade Count", "Directional shadow cascades (1-4)."));
                EditorGUILayout.PropertyField(m_CascadeSplitRatios, new GUIContent("Cascade Splits", "Split positions as fractions of Shadow Distance; strictly increasing inside (0, 1). Only the first Cascade Count - 1 splits are used."));
                Vector3 ratios = m_CascadeSplitRatios.vector3Value;
                if (!(ratios.x > 0.0f && ratios.y > ratios.x && ratios.z > ratios.y && ratios.z < 1.0f))
                    EditorGUILayout.HelpBox("Cascade splits must be strictly increasing inside (0, 1). Rendering fails until this is fixed.", MessageType.Error);
                EditorGUILayout.PropertyField(m_CascadeBlendWidth, new GUIContent("Cascade Blend Width", "Fraction of each cascade's depth range blended into the next cascade."));
                EditorGUILayout.PropertyField(m_MaxShadowedLocalLights, new GUIContent("Max Shadowed Local Lights"));
                EditorGUILayout.PropertyField(m_CachedShadowAtlasWidth, new GUIContent("Cached Shadow Atlas Width", "Atlas for local lights whose Shadow Update Mode is OnEnable or OnDemand; its height is the Local Shadow Map Resolution. 0 disables caching."));
                DrawShadowFilter();
                EditorGUILayout.PropertyField(m_CookieAtlasResolution, new GUIContent("Cookie Atlas Resolution", "Power-of-two atlas for light cookies and IES profiles (RGBA8 with mips). 0 disables cookies."));
                EditorGUILayout.PropertyField(m_CookieMaxSize, new GUIContent("Cookie Max Size", "Largest atlas block of one cookie; larger textures are minified."));
                EditorGUILayout.PropertyField(m_LightProbeSystem, new GUIContent("Light Probe System", "Light Probe Groups (with Light Probe Proxy Volumes) or Adaptive Probe Volumes. Changing it recreates the pipeline."));
                if (m_LightProbeSystem.enumValueIndex == (int)ELightProbeSystem.ProbeVolumes || m_LightProbeSystem.hasMultipleDifferentValues)
                {
                    ++EditorGUI.indentLevel;
                    EditorGUILayout.PropertyField(m_ProbeVolumeSHBands, new GUIContent("SH Bands"));
                    EditorGUILayout.PropertyField(m_ProbeVolumeMemoryBudget, new GUIContent("Memory Budget"));
                    EditorGUILayout.PropertyField(m_ProbeVolumeGPUStreaming, new GUIContent("GPU Streaming"));
                    EditorGUILayout.PropertyField(m_ProbeVolumeDiskStreaming, new GUIContent("Disk Streaming"));
                    EditorGUILayout.PropertyField(m_ProbeVolumeScenarios, new GUIContent("Lighting Scenarios"));
                    using (new EditorGUI.DisabledScope(!m_ProbeVolumeScenarios.boolValue))
                    {
                        EditorGUILayout.PropertyField(m_ProbeVolumeScenarioBlending, new GUIContent("Scenario Blending"));
                        EditorGUILayout.PropertyField(m_ProbeVolumeBlendingMemoryBudget, new GUIContent("Blending Memory Budget"));
                    }
                    --EditorGUI.indentLevel;
                }
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
