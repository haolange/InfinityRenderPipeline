using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Feature;

namespace InfinityTech.Rendering.Pipeline
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/InfinityRenderPipelineAsset", order = 360)]
    public sealed class InfinityRenderPipelineAsset : RenderPipelineAsset<InfinityRenderPipeline>
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
        public float shadowDistance = 128;

        [SerializeField] Shader m_DefaultShader;
        [SerializeField] Material m_DefaultMaterial;
        [SerializeField] Material m_DefaultParticleMaterial;
        [SerializeField] Material m_DefaultLineMaterial;
        [SerializeField] Material m_DefaultTerrainMaterial;
        [SerializeField] Material m_Default2DMaterial;

        [System.NonSerialized] public InfinityRenderPipeline renderPipeline;

        public override Shader defaultShader => m_DefaultShader != null ? m_DefaultShader : ResolveMaterials()?.defaultLitShader;
        public override Material defaultMaterial => m_DefaultMaterial;
        public override Material defaultParticleMaterial => m_DefaultParticleMaterial != null ? m_DefaultParticleMaterial : defaultMaterial;
        public override Material defaultLineMaterial => m_DefaultLineMaterial != null ? m_DefaultLineMaterial : defaultMaterial;
        public override Material defaultTerrainMaterial => m_DefaultTerrainMaterial != null ? m_DefaultTerrainMaterial : defaultMaterial;
        public override Material default2DMaterial => m_Default2DMaterial != null ? m_Default2DMaterial : defaultMaterial;

        static InfinityRenderPipelineRuntimeMaterials ResolveMaterials()
        {
            GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeMaterials materials);
            return materials;
        }

        protected override RenderPipeline CreatePipeline()
        {
            if (atmosphericalProfile == null)
            {
                throw new InvalidOperationException("InfinityRP: AtmosphericalProfile is required on the pipeline asset. Atmosphere lives only on the profile.");
            }

            VolumeProfile defaultProfile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
            if (!DefaultVolumeProfileFactory.HasRequiredDefaultComponents(defaultProfile))
            {
                throw new InvalidOperationException("InfinityRP: default Volume profile must include required exposure/film/grading values and the complete optional-feature type registry.");
            }

            renderPipeline = new InfinityRenderPipeline(this);
            InfinityRenderPipelineRuntimeTextures textures;
            if (GraphicsSettings.TryGetRenderPipelineSettings(out textures) && textures != null && textures.bestFitNormalTexture != null)
            {
                Shader.SetGlobalTexture("g_BestFitNormal_LUT", textures.bestFitNormalTexture);
            }
            return renderPipeline;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
        }
    }
}
