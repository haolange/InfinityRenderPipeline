using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/InfinityRenderPipelineAsset", order = 360)]
    public sealed class InfinityRenderPipelineAsset : RenderPipelineAsset
    {
        public bool showShader = false;
        public bool showTexture = false;
        public bool showMaterial = false;
        public bool showAdvanced = true;

        public bool updateProxy = true;
        public bool enableRayTrace = false;
        public bool enableSRPBatch = true;
        public bool enableDynamicBatch = true;
        public bool enableInstanceBatch = true;

        public ComputeShader ssrShader;
        public ComputeShader taaShader;
        public ComputeShader ssaoShader;
        public ComputeShader ssgiShader;
        public Material blitMaterial;
        public Material defaultMaterialProxy;
        public Texture2D bestFitNormalTexture;
        public InfinityRenderPipeline renderPipeline;

        public Shader defaultShaderProxy;
        public override Shader defaultShader { get { return defaultShaderProxy; } }
        public override Material defaultMaterial { get { return defaultMaterialProxy; } }

        public override Type pipelineType => renderPipeline.GetType();

        protected override RenderPipeline CreatePipeline() 
        {
            renderPipeline = new InfinityRenderPipeline();
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
