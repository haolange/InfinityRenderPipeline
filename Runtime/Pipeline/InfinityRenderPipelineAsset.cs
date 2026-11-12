using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Atmosphere;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "Rendering/Infinity/Pipeline Asset", order = 360)]
    [HelpURL(InfinityTech.Core.InfinityDocumentation.BaseUrl + "pipeline-asset.md")]
    public sealed partial class InfinityRenderPipelineAsset : RenderPipelineAsset<InfinityRenderPipeline>, IProbeVolumeEnabledRenderPipeline
    {
        public VolumeProfile qualityVolumeProfile => m_QualityVolumeProfile;

        [SerializeField] bool updateProxy = true;
        [SerializeField] bool enableRayTrace = false;
        [SerializeField] bool enableSuperResolution = false;
        [SerializeField] bool enableSRPBatch = true;
        [SerializeField] bool enableInstanceBatch = true;
        [SerializeField] EMeshDrawBackendPolicy meshBackendPolicy = EMeshDrawBackendPolicy.Auto;

        // CameraDimensionDescriptor clamps it to [0.5, 1] for super-resolution cameras.
        [SerializeField, Range(0.5f, 1.0f)]
        float m_RenderScale = 1.0f;

        [SerializeField]
        VolumeProfile m_QualityVolumeProfile;

        [SerializeField] AtmosphericalProfile atmosphericalProfile;
        [SerializeField] DiffusionProfile[] diffusionProfiles;

        [SerializeField] EOutputMode outputMode = EOutputMode.SDR;
        [SerializeField] EHDREncoding hdrEncoding = EHDREncoding.PQ_Rec2020;

        [SerializeField] int cascadeShadowMapResolution = 2048;
        [SerializeField] int localShadowMapResolution = 2048;
        [Min(0.0f)]
        [SerializeField] float shadowDistance = 128;
        // Cascade split positions as fractions of shadowDistance (the last cascade ends at shadowDistance).
        [SerializeField] Vector3 cascadeSplitRatios = new Vector3(0.067f, 0.2f, 0.467f);
        // Upper bound on local lights considered for the shadow atlas before tile budgeting.
        [Range(1, 64)]
        [SerializeField] int maxShadowedLocalLights = 16;
        // Width of the cached shadow atlas for OnEnable / OnDemand local lights (its height is the local atlas
        // resolution); 0 disables caching and every light renders every frame.
        [Min(0)]
        [SerializeField] int cachedShadowAtlasWidth = 4096;
        // Light probe system: Light Probe Groups (+ Light Probe Proxy Volumes) or Adaptive Probe Volumes.
        [SerializeField] ELightProbeSystem lightProbeSystem = ELightProbeSystem.LegacyLightProbes;
        [SerializeField] ProbeVolumeTextureMemoryBudget probeVolumeMemoryBudget = ProbeVolumeTextureMemoryBudget.MemoryBudgetMedium;
        [SerializeField] ProbeVolumeBlendingTextureMemoryBudget probeVolumeBlendingMemoryBudget = ProbeVolumeBlendingTextureMemoryBudget.MemoryBudgetMedium;
        [SerializeField] ProbeVolumeSHBands probeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL1;
        [SerializeField] bool supportProbeVolumeGPUStreaming;
        [SerializeField] bool supportProbeVolumeDiskStreaming;
        [SerializeField] bool supportProbeVolumeScenarios;
        [SerializeField] bool supportProbeVolumeScenarioBlending;

        public bool supportProbeVolume => lightProbeSystem == ELightProbeSystem.ProbeVolumes;
        public ProbeVolumeSHBands maxSHBands => supportProbeVolume ? probeVolumeSHBands : ProbeVolumeSHBands.SphericalHarmonicsL1;
        [System.Obsolete("This property is no longer necessary. #from(2023.3)")]
        public ProbeVolumeSceneData probeVolumeSceneData => null;

        // Frame Settings: features each camera may request (Game / SceneView / Preview cameras use the camera defaults,
        // reflection probe captures the reflection defaults; InfinityAdditionalCameraData can override single settings).
        [SerializeField] FrameSettings defaultFrameSettings = FrameSettings.AllEnabled;
        [SerializeField] FrameSettings reflectionFrameSettings = FrameSettings.ReflectionDefaults;

        // Reflection probe atlas: face resolution (power of two, GGX mips) and probe slots (6 slices each); 0 disables
        // probes (the sky reflects everything).
        [Min(0)]
        [SerializeField] int reflectionProbeResolution = 128;
        [Range(0, 128)]
        [SerializeField] int maxReflectionProbes = 32;

        // Light cookie / IES atlas (power of two, RGBA8 with mips); 0 disables cookies.
        [Min(0)]
        [SerializeField] int cookieAtlasResolution = 2048;
        // Largest atlas block of one cookie; bigger textures are minified into it.
        [Min(16)]
        [SerializeField] int cookieMaxSize = 512;
        // Directional cascades used (1-4); the atlas keeps the 2x2 layout.
        [Range(1, 4)]
        [SerializeField] int cascadeCount = 4;
        // Fraction of each cascade, at its outer edge, blended into the next cascade.
        [Range(0.0f, 0.5f)]
        [SerializeField] float cascadeBlendWidth = 0.1f;
        // Shadow filtering quality: Low..Ultra pick QualityPresets.ShadowFilter; Custom uses customShadowFilter.
        [SerializeField] EQualityLevel shadowFilterQuality = EQualityLevel.Medium;
        [SerializeField] ShadowFilterSettings customShadowFilter = QualityPresets.ShadowFilter[(int)EQualityLevel.Medium];

        [SerializeField] Shader m_DefaultShader;
        [SerializeField] Material m_DefaultMaterial;
        [SerializeField] Material m_DefaultParticleMaterial;
        [SerializeField] Material m_DefaultLineMaterial;
        [SerializeField] Material m_DefaultTerrainMaterial;
        [SerializeField] Material m_Default2DMaterial;

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

            return new InfinityRenderPipeline(this);
        }
    }
}
