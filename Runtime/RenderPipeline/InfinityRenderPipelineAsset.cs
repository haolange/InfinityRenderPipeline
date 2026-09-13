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
