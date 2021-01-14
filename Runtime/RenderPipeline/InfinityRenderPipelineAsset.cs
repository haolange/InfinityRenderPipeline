using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [ExecuteInEditMode]
    [CreateAssetMenu(menuName = "InfinityRenderPipeline/InfinityRenderPipelineAsset")]
    public sealed class InfinityRenderPipelineAsset : RenderPipelineAsset
    {
        public bool EnableSRPBatch = true;
        public bool EnableDynamicBatch = true;
        public bool EnableInstanceBatch = true;
        public bool EnableRayTracing = false;

        public Shader DefaultShader;
        public Material DefaultMaterial;

        public InfinityRenderPipeline RenderPipeline;
        public override Shader defaultShader { get { return DefaultShader; } }
        public override Material defaultMaterial { get { return DefaultMaterial; } }


        protected override RenderPipeline CreatePipeline() {
            RenderPipeline = new InfinityRenderPipeline();
            return RenderPipeline;
        }

        protected override void OnValidate() {
            
        }

        protected override void OnDisable() {

        }
    }
}
