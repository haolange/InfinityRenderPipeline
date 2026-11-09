using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityRenderPipelineRuntimeShaders : IRenderPipelineResources, IRenderPipelineGraphicsSettings
    {
        public const string PackagePath = "Packages/com.infinity.render-pipeline";

        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        public bool isAvailableInPlayerBuild => true;

        [ResourcePath("Shaders/Utility/Compute_Utility.compute")]
        public ComputeShader utilityShader;

        [ResourcePath("Shaders/Mesh/Compute_MeshDrawPipeline.compute")]
        public ComputeShader meshDrawPipelineCS;

        [ResourcePath("Shaders/Pass/Temporal/Compute_TemporalAntiAliasing.compute")]
        public ComputeShader taaShader;

        [ResourcePath("Shaders/Pass/ScreenSpace/Compute_ScreenSpaceReflection.compute")]
        public ComputeShader ssrShader;

        [ResourcePath("Shaders/Pass/ScreenSpace/Compute_GroundTruthOcclusion.compute")]
        public ComputeShader ssaoShader;

        [ResourcePath("Shaders/Pass/ScreenSpace/Compute_ScreenSpaceIndirectDiffuse.compute")]
        public ComputeShader ssgiShader;

        [ResourcePath("Shaders/Pass/Post/Compute_CombineLUTs.compute")]
        public ComputeShader combineLUTShader;

        [ResourcePath("Shaders/Pass/Geometry/Compute_PyramidDepth.compute")]
        public ComputeShader hiZShader;

        [ResourcePath("Shaders/Pass/Geometry/Compute_HalfResDownsample.compute")]
        public ComputeShader halfResDownsampleShader;

        [ResourcePath("Shaders/Pass/Lighting/Compute_LightTileMask.compute")]
        public ComputeShader lightTileMaskShader;

        [ResourcePath("Shaders/Pass/Lighting/Compute_CubeFilter.compute")]
        public ComputeShader cubeFilterShader;

        [ResourcePath("Shaders/Pass/Lighting/Compute_LightCookie.compute")]
        public ComputeShader lightCookieShader;

        [ResourcePath("Shaders/Pass/Shadow/Compute_ShadowMoment.compute")]
        public ComputeShader shadowMomentShader;

        [ResourcePath("Shaders/Pass/Shadow/Compute_ContactShadow.compute")]
        public ComputeShader contactShadowShader;

        [ResourcePath("Shaders/Pass/Lighting/Compute_DeferredShading.compute")]
        public ComputeShader deferredShadingShader;

        [ResourcePath("Shaders/Pass/Lighting/Compute_BurleySubsurface.compute")]
        public ComputeShader subsurfaceShader;

        [ResourcePath("Shaders/Pass/Volumetric/Compute_AtmosphericLUT.compute")]
        public ComputeShader atmosphericLUTShader;

        [ResourcePath("Shaders/Pass/Volumetric/Compute_VolumetricFog.compute")]
        public ComputeShader volumetricFogShader;

        [ResourcePath("Shaders/Pass/Volumetric/Compute_VolumetricCloud.compute")]
        public ComputeShader volumetricCloudShader;

        [ResourcePath("Shaders/Pass/Volumetric/Compute_FogComposite.compute")]
        public ComputeShader fogCompositeShader;

        [ResourcePath("Shaders/Pass/Translucent/Compute_PyramidColor.compute")]
        public ComputeShader colorPyramidShader;

        [ResourcePath("Shaders/Pass/ScreenSpace/Compute_ScreenSpaceComposite.compute")]
        public ComputeShader screenSpaceCompositeShader;

        [ResourcePath("Shaders/Pass/Temporal/Compute_SuperResolution.compute")]
        public ComputeShader superResolutionShader;

        [ResourcePath("Shaders/Pass/Post/Compute_PostProcessing.compute")]
        public ComputeShader postProcessingShader;

        [ResourcePath("Shaders/Pass/Debug/Compute_DebugView.compute")]
        public ComputeShader debugViewShader;
    }
}
