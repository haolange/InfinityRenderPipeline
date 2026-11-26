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
        SerializedProperty m_MeshBackendPolicy;
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
        SerializedProperty m_ReflectionProbeResolution;
        SerializedProperty m_MaxReflectionProbes;
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
        SerializedProperty m_GBufferLayout;
        SerializedProperty m_SceneColorFormat;
        SerializedProperty m_DepthStencilFormat;
        SerializedProperty m_MotionVectorFormat;
        SerializedProperty m_IndirectLightingFormat;
        SerializedProperty m_SubsurfaceLightingMode;
        SerializedProperty m_EnableRenderingLayers;
        SerializedProperty m_EnableAnalyticShadow;
        SerializedProperty m_EnableAnalyticIndirectOcclusion;
        SerializedProperty m_EnableAnalyticDirectShadow;
        SerializedProperty m_TemporalBackend;
        SerializedProperty m_TemporalMode;
        SerializedProperty m_TemporalQuality;
        SerializedProperty m_RenderResolutionPolicy;
        SerializedProperty m_TemporalSharpness;
        bool m_ShowEffectiveConfiguration;

        void OnEnable()
        {
            m_UpdateProxy = serializedObject.FindProperty("updateProxy");
            m_RayTrace = serializedObject.FindProperty("enableRayTrace");
            m_SuperResolution = serializedObject.FindProperty("enableSuperResolution");
            m_RenderScale = serializedObject.FindProperty("m_RenderScale");
            m_SRPBatch = serializedObject.FindProperty("enableSRPBatch");
            m_GPUInstance = serializedObject.FindProperty("enableInstanceBatch");
            m_MeshBackendPolicy = serializedObject.FindProperty("meshBackendPolicy");
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
            m_ReflectionProbeResolution = serializedObject.FindProperty("reflectionProbeResolution");
            m_MaxReflectionProbes = serializedObject.FindProperty("maxReflectionProbes");
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
            m_GBufferLayout = serializedObject.FindProperty("m_GBufferLayout");
            m_SceneColorFormat = serializedObject.FindProperty("m_SceneColorFormat");
            m_DepthStencilFormat = serializedObject.FindProperty("m_DepthStencilFormat");
            m_MotionVectorFormat = serializedObject.FindProperty("m_MotionVectorFormat");
            m_IndirectLightingFormat = serializedObject.FindProperty("m_IndirectLightingFormat");
            m_SubsurfaceLightingMode = serializedObject.FindProperty("m_SubsurfaceLightingMode");
            m_EnableRenderingLayers = serializedObject.FindProperty("m_EnableRenderingLayers");
            m_EnableAnalyticShadow = serializedObject.FindProperty("m_EnableAnalyticShadow");
            m_EnableAnalyticIndirectOcclusion = serializedObject.FindProperty("m_EnableAnalyticIndirectOcclusion");
            m_EnableAnalyticDirectShadow = serializedObject.FindProperty("m_EnableAnalyticDirectShadow");
            m_TemporalBackend = serializedObject.FindProperty("m_TemporalBackend");
            m_TemporalMode = serializedObject.FindProperty("m_TemporalMode");
            m_TemporalQuality = serializedObject.FindProperty("m_TemporalQuality");
            m_RenderResolutionPolicy = serializedObject.FindProperty("m_RenderResolutionPolicy");
            m_TemporalSharpness = serializedObject.FindProperty("m_TemporalSharpness");
        }

        static void DrawResolvedField(SerializedProperty property, GUIContent label, in ConfigFieldState state, bool allowEditWhenUnsupported = true)
        {
            string tooltip = string.IsNullOrEmpty(state.reason) ? label.tooltip : $"{label.tooltip}\n[{state.status}] {state.reason}";
            var resolvedLabel = new GUIContent(label.text, tooltip);
            bool disabled = state.status == EConfigStatus.Inactive
                || state.status == EConfigStatus.Required
                || (!allowEditWhenUnsupported && state.status == EConfigStatus.Unsupported);

            using (new EditorGUI.DisabledScope(disabled))
            {
                EditorGUILayout.PropertyField(property, resolvedLabel);
            }

            switch (state.status)
            {
                case EConfigStatus.Conflict:
                    EditorGUILayout.HelpBox(state.reason, MessageType.Error);
                    break;
                case EConfigStatus.Unsupported:
                case EConfigStatus.Required:
                case EConfigStatus.PlatformOverride:
                    EditorGUILayout.HelpBox(state.reason, MessageType.Warning);
                    break;
                case EConfigStatus.Inactive:
                    EditorGUILayout.HelpBox(state.reason, MessageType.Info);
                    break;
            }
        }

        static void DrawStatusLine(string label, in ConfigFieldState state)
        {
            MessageType type = state.status == EConfigStatus.Conflict ? MessageType.Error
                : (state.status == EConfigStatus.Unsupported || state.status == EConfigStatus.Required || state.status == EConfigStatus.PlatformOverride)
                    ? MessageType.Warning
                    : MessageType.Info;
            string msg = string.IsNullOrEmpty(state.reason) ? $"{label}: {state.status}" : $"{label} [{state.status}]: {state.reason}";
            EditorGUILayout.HelpBox(msg, type);
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
                ShadowFilterSettings preset = QualityPresets.Select(QualityPresets.ShadowFilter, level);
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
                    m_CustomShadowFilter.boxedValue = preset;
                    m_ShadowFilterQuality.intValue = (int)EQualityLevel.Custom;
                    serializedObject.ApplyModifiedProperties();
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
            var pipelineAsset = (InfinityRenderPipelineAsset)target;
            EffectivePipelineConfiguration resolvedConfig = pipelineAsset.ResolveSettings().config;

            // 1. Rendering
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Rendering", "Rendering"))
            {
                EditorGUILayout.PropertyField(m_SRPBatch, new GUIContent("SRP Batcher"));
                EditorGUILayout.PropertyField(m_MeshBackendPolicy);
                EditorGUILayout.PropertyField(m_GPUInstance, new GUIContent("GPU Instancing"));
                EditorGUILayout.PropertyField(m_UpdateProxy, new GUIContent("Refresh Renderer Proxy"));
                EditorGUILayout.PropertyField(m_RayTrace, new GUIContent("Unified Ray Tracing"));
                EditorGUILayout.PropertyField(m_QualityVolumeProfile, new GUIContent("Quality Volume Profile"));
                EditorGUILayout.PropertyField(m_OutputMode, new GUIContent("Output Mode"));
                EditorGUILayout.PropertyField(m_HDREncoding, new GUIContent("HDR Encoding"));
            }
            InfinityInspectorGUI.EndFoldout();

            // 2. Surface & Attachments
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "SurfaceAttachments", "Surface & Attachments"))
            {
                DrawResolvedField(m_GBufferLayout, new GUIContent("GBuffer Layout", "Compact64Bit: full RGB albedo + Oct8x2 normal (default). HighPrecision96Bit: RGB + Oct16x2. BestFitYCoCg64Bit: YCoCg checkerboard + BestFit normal."), resolvedConfig.gBufferLayoutState);
                DrawResolvedField(m_SceneColorFormat, new GUIContent("Scene Color Format", "R11G11B10F (32 bit, default) or RGBA16F (64 bit, required by SceneColorAlpha SSS)."), resolvedConfig.sceneColorState);
                DrawResolvedField(m_DepthStencilFormat, new GUIContent("Depth Stencil Format", "Main camera depth buffer format. MotionPass requires a stencil channel."), resolvedConfig.depthStencilState);
                DrawResolvedField(m_MotionVectorFormat, new GUIContent("Motion Vector Format"), resolvedConfig.motionVectorState);
                DrawResolvedField(m_IndirectLightingFormat, new GUIContent("Indirect Lighting Format"), resolvedConfig.indirectLightingState);
                DrawResolvedField(m_SubsurfaceLightingMode, new GUIContent("Subsurface Lighting Mode", "SeparateBuffer uses on-demand R11G11B10F ScatterDiffuse; SceneColorAlpha stores diffuse luminance in RGBA16F SceneColor alpha."), resolvedConfig.subsurfaceModeState);
                DrawResolvedField(m_EnableRenderingLayers, new GUIContent("Rendering Layers", "Stores object rendering layers in SurfaceMetadata.g (R8G8_UNorm vs R8_UNorm)."), resolvedConfig.renderingLayersState);

                m_ShowEffectiveConfiguration = EditorGUILayout.Foldout(m_ShowEffectiveConfiguration, "Effective Configuration", true);
                if (m_ShowEffectiveConfiguration)
                {
                    ++EditorGUI.indentLevel;
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("GBuffer A / B", $"{resolvedConfig.gBufferAFormat} / {resolvedConfig.gBufferBFormat}");
                        EditorGUILayout.TextField("Surface Metadata", resolvedConfig.surfaceMetadataFormat.ToString());
                        EditorGUILayout.TextField("Motion / Metadata", $"{resolvedConfig.motionVectorFormat} / {resolvedConfig.motionMetadataFormat} ({resolvedConfig.motionMetadataBitsPerPixel} bit)");
                        EditorGUILayout.TextField("Scene Color", resolvedConfig.sceneColorFormat.ToString());
                        EditorGUILayout.TextField("Depth Stencil", resolvedConfig.depthStencilFormat.ToString());
                        EditorGUILayout.TextField("Indirect Lighting", resolvedConfig.indirectLightingFormat.ToString());
                        EditorGUILayout.TextField("Baked / SSS Format", $"{resolvedConfig.bakedDiffuseFormat} / {resolvedConfig.scatterDiffuseFormat}");
                        EditorGUILayout.TextField("CustomData SSS", resolvedConfig.customDataSSSFormat.ToString());
                        EditorGUILayout.TextField("Common BPP", $"Core {resolvedConfig.coreGBufferBitsPerPixel} + Meta {resolvedConfig.surfaceMetadataBitsPerPixel} = {resolvedConfig.commonGBufferPassBitsPerPixel} bit");
                        EditorGUILayout.TextField("Effective TRC", $"{resolvedConfig.effectiveTemporalBackend} ({resolvedConfig.effectiveTemporalMode}, {resolvedConfig.effectiveRenderScale:0.00}x)");
                    }
                    --EditorGUI.indentLevel;
                }
            }
            InfinityInspectorGUI.EndFoldout();

            // 3. Lighting & Shadows
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Lighting", "Lighting & Shadows"))
            {
                EditorGUILayout.PropertyField(m_AtmosphericalProfile, new GUIContent("Atmospherical Profile"));
                EditorGUILayout.PropertyField(m_DiffusionProfiles, new GUIContent("Diffusion Profiles"), true);
                if (!m_DiffusionProfiles.hasMultipleDifferentValues)
                {
                    for (int i = 0; i < m_DiffusionProfiles.arraySize; ++i)
                    {
                        if (m_DiffusionProfiles.GetArrayElementAtIndex(i).objectReferenceValue == null)
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

            // 4. Reflection
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Reflection", "Reflection"))
            {
                EditorGUILayout.PropertyField(m_ReflectionProbeResolution, new GUIContent("Reflection Probe Resolution", "Face size of each probe in the probe atlas (GGX-prefiltered mips). 0 disables probes."));
                EditorGUILayout.PropertyField(m_MaxReflectionProbes, new GUIContent("Max Reflection Probes", "Probe slots in the atlas (6 slices each)."));
                DrawStatusLine("Clustered Reflection Atlas", resolvedConfig.reflectionState);
                EditorGUILayout.HelpBox("Dual execution active: Spatially Indexed Reflection Probes (Box / OrientedBox / Sphere) + Raster Reflection Volumes compose into specular IBL before Sky.", MessageType.None);
            }
            InfinityInspectorGUI.EndFoldout();

            // 5. Decal
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Decal", "Decal"))
            {
                DrawStatusLine("Direct GBuffer Decal Path", resolvedConfig.directDecalState);
                EditorGUILayout.HelpBox("Supported paths execute in order: DBuffer -> Direct GBuffer -> Clustered Decals (priority by drawOrder, filtered by Rendering Layer).", MessageType.None);
            }
            InfinityInspectorGUI.EndFoldout();

            // 6. Analytic Shadow
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "AnalyticShadow", "Analytic Shadow"))
            {
                DrawResolvedField(m_EnableAnalyticShadow, new GUIContent("Analytic Shadow", "Enables InfinityAnalyticOccluder volumes for low-frequency indirect occlusion and direct soft shadows."), ConfigFieldState.Supported);
                ++EditorGUI.indentLevel;
                DrawResolvedField(m_EnableAnalyticIndirectOcclusion, new GUIContent("Indirect Occlusion", "Hemisphere visibility modulation on ambient indirect lighting."), resolvedConfig.analyticIndirectState);
                DrawResolvedField(m_EnableAnalyticDirectShadow, new GUIContent("Direct Soft Shadow", "Per-light analytical cone visibility attenuation on directional and local lights."), resolvedConfig.analyticDirectState);
                --EditorGUI.indentLevel;
            }
            InfinityInspectorGUI.EndFoldout();

            // 7. Temporal Reconstruction (TRC)
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "TemporalReconstruction", "Temporal Reconstruction"))
            {
                DrawResolvedField(m_TemporalBackend, new GUIContent("Temporal Backend", "Unified Temporal Reconstruction provider backend (Native TAA, Native TAAU, or extensible external SDK backends)."), resolvedConfig.temporalBackendState);
                DrawResolvedField(m_TemporalMode, new GUIContent("Reconstruction Mode", "NativeAA (1.0x), Upscaling, or Supersampling."), resolvedConfig.temporalModeState);
                DrawResolvedField(m_RenderResolutionPolicy, new GUIContent("Resolution Policy", "Controls whether render scale is fixed manually or driven by Quality Preset."), ConfigFieldState.Supported);
                DrawResolvedField(m_TemporalQuality, new GUIContent("Quality Preset", "Quality preset scale factor when Resolution Policy is QualityPreset."), resolvedConfig.temporalQualityState);
                DrawResolvedField(m_RenderScale, new GUIContent("Render Scale", "Manual render scale [0.5, 1.0] when FixedScale upscaling is active."), resolvedConfig.renderScaleState);
                EditorGUILayout.PropertyField(m_TemporalSharpness, new GUIContent("Output Sharpness", "Display-only contrast adaptive sharpening (never fed back into history)."));
                EditorGUILayout.PropertyField(m_SuperResolution, new GUIContent("Legacy Super Resolution Toggle", "Synchronized with Native TAAU when m_TemporalBackend is NativeTAA."));
            }
            InfinityInspectorGUI.EndFoldout();

            // 8. Advanced
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Advanced", "Advanced"))
            {
                EditorGUILayout.LabelField("Frame Settings · Cameras", EditorStyles.miniBoldLabel);
                InfinityTech.Rendering.Editor.InfinityFrameSettingsGUI.DrawSettings(serializedObject.FindProperty("defaultFrameSettings"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Frame Settings · Reflection Probe Captures", EditorStyles.miniBoldLabel);
                InfinityTech.Rendering.Editor.InfinityFrameSettingsGUI.DrawSettings(serializedObject.FindProperty("reflectionFrameSettings"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Default Materials", EditorStyles.miniBoldLabel);
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
