using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityRenderPipelineRuntimeShaders : IRenderPipelineResources
    {
        public const string PackagePath = "Packages/com.infinity.render-pipeline";

        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [ResourcePath("Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute")]
        public ComputeShader meshDrawPipelineCS;

        [ResourcePath("Shaders/RenderingFeature/TemporalAntiAliasing/Compute_TemporalAntiAliasing.compute")]
        public ComputeShader taaShader;

        [ResourcePath("Shaders/RenderingFeature/ScreenSpaceReflection/Compute_ScreenSpaceReflection.compute")]
        public ComputeShader ssrShader;

        [ResourcePath("Shaders/RenderingFeature/ScreenSpaceAmbientOcclusion/Compute_GroundTruthOcclusion.compute")]
        public ComputeShader ssaoShader;

        [ResourcePath("Shaders/RenderingFeature/ScreenSpaceIndirectDiffuse/Compute_ScreenSpaceIndirectDiffuse.compute")]
        public ComputeShader ssgiShader;

        [ResourcePath("Shaders/ColorGrading/Compute_CombineLUTs.compute")]
        public ComputeShader combineLUTShader;

        [ResourcePath("Shaders/RenderingFeature/PyramidDepth/Compute_PyramidDepth.compute")]
        public ComputeShader hiZShader;

        [ResourcePath("Shaders/RenderingFeature/HalfResDownsample/Compute_HalfResDownsample.compute")]
        public ComputeShader halfResDownsampleShader;

        [ResourcePath("Shaders/RenderingFeature/ZBinningLightList/Compute_ZBinningLightList.compute")]
        public ComputeShader zBinningShader;

        [ResourcePath("Shaders/RenderingFeature/ContactShadow/Compute_ContactShadow.compute")]
        public ComputeShader contactShadowShader;

        [ResourcePath("Shaders/RenderingFeature/DeferredShading/Compute_DeferredShading.compute")]
        public ComputeShader deferredShadingShader;

        [ResourcePath("Shaders/RenderingFeature/BurleySubsurface/Compute_BurleySubsurface.compute")]
        public ComputeShader subsurfaceShader;

        [ResourcePath("Shaders/RenderingFeature/AtmosphericLUT/Compute_AtmosphericLUT.compute")]
        public ComputeShader atmosphericLUTShader;

        [ResourcePath("Shaders/RenderingFeature/VolumetricFog/Compute_VolumetricFog.compute")]
        public ComputeShader volumetricFogShader;

        [ResourcePath("Shaders/RenderingFeature/VolumetricCloud/Compute_VolumetricCloud.compute")]
        public ComputeShader volumetricCloudShader;

        [ResourcePath("Shaders/RenderingFeature/FogComposite/Compute_FogComposite.compute")]
        public ComputeShader fogCompositeShader;

        [ResourcePath("Shaders/RenderingFeature/PyramidColor/Compute_PyramidColor.compute")]
        public ComputeShader colorPyramidShader;

        [ResourcePath("Shaders/RenderingFeature/ScreenSpaceComposite/Compute_ScreenSpaceComposite.compute")]
        public ComputeShader screenSpaceCompositeShader;

        [ResourcePath("Shaders/RenderingFeature/SuperResolution/Compute_SuperResolution.compute")]
        public ComputeShader superResolutionShader;

        [ResourcePath("Shaders/RenderingFeature/PostProcessing/Compute_PostProcessing.compute")]
        public ComputeShader postProcessingShader;

        [ResourcePath("Shaders/RenderingFeature/OutputTransform/Compute_OutputTransform.compute")]
        public ComputeShader outputTransformShader;

        [ResourcePath("Shaders/RenderingFeature/DebugView/Compute_DebugView.compute")]
        public ComputeShader debugViewShader;

        [ResourcePath("Shaders/RenderingFeature/RayTracing/Compute_RayTracedAmbientOcclusion.compute")]
        public ComputeShader rtaoShader;
    }
}
