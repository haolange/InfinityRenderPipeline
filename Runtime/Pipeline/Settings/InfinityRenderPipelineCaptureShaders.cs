using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Shaders of the capture diagnostics. The Editor and Development players use them; CaptureShadersStripper removes this
    // container from release players, so no capture shader ships there. Capture fails its request, never pipeline
    // construction, when the container is absent.
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityRenderPipelineCaptureShaders : IRenderPipelineResources, IRenderPipelineGraphicsSettings
    {
        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        public bool isAvailableInPlayerBuild => true;

        internal const string CaptureTexturePath = "Shaders/Utility/Compute_CaptureTexture.compute";
        internal const string CaptureDepthPath = "Shaders/Utility/Compute_CaptureDepth.compute";

        [ResourcePath(CaptureTexturePath)]
        public ComputeShader captureTextureShader;

        [ResourcePath(CaptureDepthPath)]
        public ComputeShader captureDepthShader;
    }
}
