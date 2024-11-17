using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/InfinityRenderPipelineAsset", order = 360)]
    public sealed class InfinityRenderPipelineAsset : RenderPipelineAsset<InfinityRenderPipeline>
    {
        public VolumeProfile volumeProfile
        {
            get => m_VolumeProfile;
            set => m_VolumeProfile = value;
        }

        public bool showShader = false;
        public bool showTexture = false;
        public bool showMaterial = false;
        public bool showProfile = false;
        public bool showAdvanced = true;

        public bool updateProxy = true;
        public bool enableRayTrace = false;
        public bool enableSRPBatch = true;
        public bool enableDynamicBatch = true;
        public bool enableInstanceBatch = true;

        [SerializeField] 
        private VolumeProfile m_VolumeProfile;

        public ComputeShader taaShader;
        public ComputeShader ssrShader;
        public ComputeShader ssaoShader;
        public ComputeShader ssgiShader;
        public ComputeShader combineLUTShader;

        public Shader defaultShaderProxy;

        public Material blitMaterial;
        public Material defaultMaterialProxy;

        public Texture2D bestFitNormalTexture;

        public InfinityRenderPipeline renderPipeline;
        public override Shader defaultShader { get { return defaultShaderProxy; } }
        public override Material defaultMaterial { get { return defaultMaterialProxy; } }

        protected override RenderPipeline CreatePipeline() 
        {
            renderPipeline = new InfinityRenderPipeline(this);
            Shader.SetGlobalTexture("g_BestFitNormal_LUT", bestFitNormalTexture);
            return renderPipeline;
        }

        protected override void OnValidate() 
        {
            base.OnValidate();
        }

        protected override void OnDisable() 
        {
            base.OnDisable();
        }
    }
}
