using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityRenderPipelineRuntimeMaterials : IRenderPipelineResources
    {
        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [ResourcePath("Runtime/Resources/Materials/M_Blit.mat")]
        public Material blitMaterial;

        [ResourcePath("Shaders/Surface/InfinityLit.shader")]
        public Shader defaultLitShader;

        [ResourcePath("Shaders/Surface/InfinityUnlit.shader")]
        public Shader defaultUnlitShader;
    }
}
