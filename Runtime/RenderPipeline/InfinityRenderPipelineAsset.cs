using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "Rendering/Infinity/Pipeline Asset", order = 360)]
    [HelpURL(InfinityTech.Rendering.Pipeline.InfinityDocumentation.BaseUrl + "pipeline-asset.md")]
    public sealed class InfinityRenderPipelineAsset : RenderPipelineAsset<InfinityRenderPipeline>, IProbeVolumeEnabledRenderPipeline
    {
        public VolumeProfile qualityVolumeProfile
        {
            get => m_QualityVolumeProfile;
            set => m_QualityVolumeProfile = value;
        }

        public bool updateProxy = true;
        public bool enableRayTrace = false;
        public bool enableSuperResolution = false;
        public bool enableSRPBatch = true;
        public bool enableDynamicBatch = true;
        public bool enableInstanceBatch = true;
        public EMeshBackendPolicy meshBackendPolicy = EMeshBackendPolicy.Auto;

        [SerializeField, Range(0.5f, 1.0f)]
        float m_RenderScale = 1.0f;
        public float renderScale
        {
            get => m_RenderScale;
            set => m_RenderScale = Mathf.Clamp(value, 0.5f, 1.0f);
        }

        [SerializeField]
        VolumeProfile m_QualityVolumeProfile;

        public AtmosphericalProfile atmosphericalProfile;
        public DiffusionProfile[] diffusionProfiles;

        public EOutputMode outputMode = EOutputMode.SDR;
        public EHDREncoding hdrEncoding = EHDREncoding.PQ_Rec2020;

        public int cascadeShadowMapResolution = 2048;
        public int localShadowMapResolution = 2048;
        [Min(0.0f)]
        public float shadowDistance = 128;
        // Cascade split positions as fractions of shadowDistance (the last cascade ends at shadowDistance).
        public Vector3 cascadeSplitRatios = new Vector3(0.067f, 0.2f, 0.467f);
        // Upper bound on local lights considered for the shadow atlas before tile budgeting.
        [Range(1, 64)]
        public int maxShadowedLocalLights = 16;
        // Width of the cached shadow atlas for OnEnable / OnDemand local lights (its height is the local atlas
        // resolution); 0 disables caching and every light renders every frame.
        [Min(0)]
        public int cachedShadowAtlasWidth = 4096;
        // Light probe system: Light Probe Groups (+ Light Probe Proxy Volumes) or Adaptive Probe Volumes.
        public ELightProbeSystem lightProbeSystem = ELightProbeSystem.LegacyLightProbes;
        public ProbeVolumeTextureMemoryBudget probeVolumeMemoryBudget = ProbeVolumeTextureMemoryBudget.MemoryBudgetMedium;
        public ProbeVolumeBlendingTextureMemoryBudget probeVolumeBlendingMemoryBudget = ProbeVolumeBlendingTextureMemoryBudget.MemoryBudgetMedium;
        public ProbeVolumeSHBands probeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL1;
        public bool supportProbeVolumeGPUStreaming;
        public bool supportProbeVolumeDiskStreaming;
        public bool supportProbeVolumeScenarios;
        public bool supportProbeVolumeScenarioBlending;

        public bool supportProbeVolume => lightProbeSystem == ELightProbeSystem.ProbeVolumes;
        public ProbeVolumeSHBands maxSHBands => supportProbeVolume ? probeVolumeSHBands : ProbeVolumeSHBands.SphericalHarmonicsL1;
        [System.Obsolete("This property is no longer necessary. #from(2023.3)")]
        public ProbeVolumeSceneData probeVolumeSceneData => null;

        // Frame Settings: features each camera may request (Game / SceneView / Preview cameras use the camera defaults,
        // reflection probe captures the reflection defaults; InfinityAdditionalCameraData can override single settings).
        public FFrameSettings defaultFrameSettings = FFrameSettings.AllEnabled;
        public FFrameSettings reflectionFrameSettings = FFrameSettings.ReflectionDefaults;

        // Reflection probe atlas: face resolution (power of two, GGX mips) and probe slots (6 slices each); 0 disables
        // probes (the sky reflects everything).
        [Min(0)]
        public int reflectionProbeResolution = 128;
        [Range(0, 128)]
        public int maxReflectionProbes = 32;

        // Light cookie / IES atlas (power of two, RGBA8 with mips); 0 disables cookies.
        [Min(0)]
        public int cookieAtlasResolution = 2048;
        // Largest atlas block of one cookie; bigger textures are minified into it.
        [Min(16)]
        public int cookieMaxSize = 512;
        // Directional cascades used (1-4); the atlas keeps the 2x2 layout.
        [Range(1, 4)]
        public int cascadeCount = 4;
        // Fraction of each cascade, at its outer edge, blended into the next cascade.
        [Range(0.0f, 0.5f)]
        public float cascadeBlendWidth = 0.1f;
        // Shadow filtering quality: Low..Ultra pick QualityPresets.ShadowFilter; Custom uses customShadowFilter.
        public EQualityLevel shadowFilterQuality = EQualityLevel.Medium;
        public FShadowFilterSettings customShadowFilter = QualityPresets.ShadowFilter[(int)EQualityLevel.Medium];

        public FShadowFilterSettings ResolveShadowFilter() => shadowFilterQuality == EQualityLevel.Custom
            ? customShadowFilter
            : QualityPresets.Select(QualityPresets.ShadowFilter, shadowFilterQuality);

        [SerializeField] Shader m_DefaultShader;
        [SerializeField] Material m_DefaultMaterial;
        [SerializeField] Material m_DefaultParticleMaterial;
        [SerializeField] Material m_DefaultLineMaterial;
        [SerializeField] Material m_DefaultTerrainMaterial;
        [SerializeField] Material m_Default2DMaterial;

        [System.NonSerialized] public InfinityRenderPipeline renderPipeline;

        public override Shader defaultShader => m_DefaultShader != null ? m_DefaultShader : ResolveMaterials()?.defaultLitShader;
        public override Material defaultMaterial => m_DefaultMaterial != null ? m_DefaultMaterial : ResolveUnlitMaterial();
        public override Material defaultParticleMaterial => m_DefaultParticleMaterial != null ? m_DefaultParticleMaterial : ResolveUnlitMaterial();
        public override Material defaultLineMaterial => m_DefaultLineMaterial != null ? m_DefaultLineMaterial : ResolveUnlitMaterial();
        public override Material defaultTerrainMaterial => m_DefaultTerrainMaterial != null ? m_DefaultTerrainMaterial : ResolveUnlitMaterial();
        public override Material default2DMaterial => m_Default2DMaterial != null ? m_Default2DMaterial : ResolveUnlitMaterial();

        static Material ResolveUnlitMaterial()
        {
            InfinityRenderPipelineRuntimeMaterials materials = ResolveMaterials();
            if (materials != null && materials.defaultUnlitMaterial != null)
            {
                return materials.defaultUnlitMaterial;
            }

            throw new InvalidOperationException("InfinityRP: default Unlit material is required on GlobalSettings RuntimeMaterials.");
        }

        static InfinityRenderPipelineRuntimeMaterials ResolveMaterials()
        {
            GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeMaterials materials);
            return materials;
        }

#if UNITY_EDITOR
        internal void RequestEditorRecreation()
        {
            // Use Unity's normal RP asset validation lifecycle after an explicit settings edit.
            base.OnValidate();
        }
#endif

        protected override bool requiresCompatibleRenderPipelineGlobalSettings => true;

        protected override void EnsureGlobalSettings()
        {
            InfinityRenderPipelineGlobalSettings.Require();
        }

        protected override RenderPipeline CreatePipeline()
        {
            if (atmosphericalProfile == null)
            {
                throw new InvalidOperationException("InfinityRP: AtmosphericalProfile is required on the pipeline asset. Atmosphere lives only on the profile.");
            }

            renderPipeline = new InfinityRenderPipeline(this);
            return renderPipeline;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
        }
    }
}
