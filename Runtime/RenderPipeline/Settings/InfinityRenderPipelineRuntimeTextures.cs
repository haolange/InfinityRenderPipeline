using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityRenderPipelineRuntimeTextures : IRenderPipelineResources
    {
        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [ResourcePath("Runtime/Resources/Textures/System_LUT/LUT_BestFit.png")]
        public Texture2D bestFitNormalTexture;
    }
}
