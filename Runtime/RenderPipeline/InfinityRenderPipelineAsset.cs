using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/InfinityRenderPipelineAsset", order = 360)]
    public sealed class InfinityRenderPipelineAsset : RenderPipelineAsset
    {
        public bool enableRayTrace = false;
        public bool enableSRPBatch = true;
        public bool enableDynamicBatch = true;
        public bool enableInstanceBatch = true;

        public Shader defaultShaderProxy;
        public Material defaultMaterialProxy;

        public Texture2D bestFitNormal;

        public InfinityRenderPipeline RenderPipeline;
        public override Shader defaultShader { get { return defaultShaderProxy; } }
        public override Material defaultMaterial { get { return defaultMaterialProxy; } }


        protected override RenderPipeline CreatePipeline() 
        {
            RenderPipeline = new InfinityRenderPipeline();
            Shader.SetGlobalTexture("g_BestFirNormal_LUT", bestFitNormal);

            return RenderPipeline;
        }

        protected override void OnValidate() 
        {
            
        }

        protected override void OnDisable() 
        {

        }
    }
}
