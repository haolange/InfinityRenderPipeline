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

        SerializedProperty m_RayTrace;
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
        SerializedProperty m_DepthStencilFormat;
        SerializedProperty m_EnableRenderingLayers;
        SerializedProperty m_EnableAnalyticShadow;
        SerializedProperty m_EnableAnalyticIndirectOcclusion;
        SerializedProperty m_EnableAnalyticDirectShadow;
        SerializedProperty m_TemporalBackend;
        SerializedProperty m_TemporalMode;
        SerializedProperty m_TemporalQuality;
        SerializedProperty m_TemporalSharpness;
        bool m_ShowEffectiveConfiguration;

        void OnEnable()
        {
            m_RayTrace = serializedObject.FindProperty("enableRayTrace");
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
            m_DepthStencilFormat = serializedObject.FindProperty("m_DepthStencilFormat");
            m_EnableRenderingLayers = serializedObject.FindProperty("m_EnableRenderingLayers");
            m_EnableAnalyticShadow = serializedObject.FindProperty("m_EnableAnalyticShadow");
            m_EnableAnalyticIndirectOcclusion = serializedObject.FindProperty("m_EnableAnalyticIndirectOcclusion");
            m_EnableAnalyticDirectShadow = serializedObject.FindProperty("m_EnableAnalyticDirectShadow");
            m_TemporalBackend = serializedObject.FindProperty("m_TemporalBackend");
            m_TemporalMode = serializedObject.FindProperty("m_TemporalMode");
            m_TemporalQuality = serializedObject.FindProperty("m_TemporalQuality");
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
                EditorGUILayout.PropertyField(m_RayTrace, new GUIContent("Unified Ray Tracing"));
                EditorGUILayout.PropertyField(m_QualityVolumeProfile, new GUIContent("Quality Volume Profile"));
                EditorGUILayout.PropertyField(m_OutputMode, new GUIContent("Output Mode"));
                EditorGUILayout.PropertyField(m_HDREncoding, new GUIContent("HDR Encoding"));
            }
            InfinityInspectorGUI.EndFoldout();

            // 2. Surface & Attachments
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "SurfaceAttachments", "Surface & Attachments"))
            {
                DrawStatusLine("Opaque Color Path", resolvedConfig.colorPathState);
                DrawResolvedField(m_DepthStencilFormat, new GUIContent("Depth Stencil Format", "Main camera depth buffer. The stencil holds the object-motion, deferred and subsurface admission bits."), resolvedConfig.depthStencilState);
                DrawResolvedField(m_EnableRenderingLayers, new GUIContent("Rendering Layers", "Writes the object's four light channels into the sideband so lights, clustered decals and analytic occluders filter per pixel."), ConfigFieldState.Supported);

                ++EditorGUI.indentLevel;
                m_ShowEffectiveConfiguration = EditorGUILayout.Foldout(m_ShowEffectiveConfiguration, "Effective Configuration", true);
                if (m_ShowEffectiveConfiguration)
                {
                    ++EditorGUI.indentLevel;
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("GBuffer A / B", $"{EffectivePipelineConfiguration.GBufferAFormat} / {EffectivePipelineConfiguration.GBufferBFormat}");
                        EditorGUILayout.TextField("Scene Color", EffectivePipelineConfiguration.SceneColorFormat.ToString());
                        EditorGUILayout.TextField("Sideband", EffectivePipelineConfiguration.SidebandFormat + " (allocated on demand)");
                        EditorGUILayout.TextField("Geometry Color", $"{resolvedConfig.geometryColorBitsPerPixel} bit");
                        EditorGUILayout.TextField("Motion", EffectivePipelineConfiguration.MotionFormat.ToString());
                        EditorGUILayout.TextField("Depth Stencil", resolvedConfig.depthStencilFormat.ToString());
                        string trcSummary = $"{resolvedConfig.effectiveTemporalBackend} ({resolvedConfig.effectiveTemporalMode}, {resolvedConfig.effectiveRenderScale:0.00}x, MipBias {resolvedConfig.effectiveTemporalMipBias:0.00})";
                        if (resolvedConfig.hasTemporalFallback)
                        {
                            trcSummary += $" [Fallback from {resolvedConfig.requestedTemporalBackend}]";
                        }
                        EditorGUILayout.TextField("Effective TRC", trcSummary);
                    }
                    --EditorGUI.indentLevel;
                }
                --EditorGUI.indentLevel;
            }
            InfinityInspectorGUI.EndFoldout();

            // 3. Lighting & Shadows
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Lighting", "Lighting & Shadows"))
            {
                EditorGUILayout.PropertyField(m_AtmosphericalProfile, new GUIContent("Atmospherical Profile"));
                EditorGUILayout.PropertyField(m_DiffusionProfiles, new GUIContent("Diffusion Profiles", "Skin materials pick a profile by index 0..14; at most 15 profiles fit the sideband."), true);
                DrawStatusLine("Subsurface Scattering", resolvedConfig.subsurfaceState);
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
                EditorGUILayout.HelpBox("Paths execute in order: DBuffer -> Direct GBuffer -> Clustered Decals (priority by drawOrder; clustered decals filter by light channel).", MessageType.None);
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
                DrawResolvedField(m_TemporalBackend, new GUIContent("Temporal Backend", "Unified Temporal Reconstruction backend (Native, STP, FSR 3.1, SGSR v2, or platform-gated XeSS/DLSS)."), resolvedConfig.temporalBackendState, allowEditWhenUnsupported: true);
                DrawResolvedField(m_TemporalMode, new GUIContent("Reconstruction Mode", "NativeAA (1.0x) or Upscaling driven by the quality tier table."), resolvedConfig.temporalModeState);
                DrawResolvedField(m_TemporalQuality, new GUIContent("Upscale Quality", "Fixed quality preset scale factor active in Upscaling mode."), resolvedConfig.temporalQualityState);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.FloatField(new GUIContent("Effective Scale", "Resolved internal resolution scale for the active backend, mode, and quality preset."), resolvedConfig.effectiveRenderScale);
                    EditorGUILayout.FloatField(new GUIContent("Texture Mip Bias", "O(1) per-camera surface mip bias applied in ViewUniform."), resolvedConfig.effectiveTemporalMipBias);
                }
                DrawResolvedField(m_TemporalSharpness, new GUIContent("Output Sharpness", "Display-only sharpening strength (never fed back into temporal history)."), resolvedConfig.temporalSharpnessState);
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
                bool allTemporalPresent = shaders.taaShader != null
                    && shaders.temporalStpShader != null
                    && shaders.temporalFsrShader != null
                    && shaders.temporalSgsrShader != null;
                EditorGUILayout.HelpBox(allTemporalPresent
                    ? "Runtime shaders resolved from Global Settings."
                    : "Runtime shaders are registered, but one or more Temporal shaders (TAA, STP, FSR, SGSR) are missing. Open Project Settings > Graphics > Infinity RP.",
                    allTemporalPresent ? MessageType.Info : MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Infinity Global Settings / RuntimeShaders are not registered. Open Project Settings > Graphics.", MessageType.Error);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
