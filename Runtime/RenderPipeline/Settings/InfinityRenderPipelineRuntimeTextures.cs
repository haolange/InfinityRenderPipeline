using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityRenderPipelineRuntimeTextures : IRenderPipelineResources, IRenderPipelineGraphicsSettings
    {
        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        public bool isAvailableInPlayerBuild => true;

        [ResourcePath("Runtime/Resources/Textures/System_LUT/LUT_BestFit.png")]
        public Texture2D bestFitNormalTexture;
    }
}
