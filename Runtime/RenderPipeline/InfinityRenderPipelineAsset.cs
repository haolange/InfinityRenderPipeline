using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Feature;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        public bool enableSuperResolution = false;
        public bool enableSRPBatch = true;
        public bool enableDynamicBatch = true;
        public bool enableInstanceBatch = true;

        [SerializeField] 
        private VolumeProfile m_VolumeProfile;

        [Header("Atmosphere")]
        public AtmosphericalProfile atmosphericalProfile;

        [Header("Compute Shaders")]
        public ComputeShader meshDrawPipelineCS;
        public ComputeShader taaShader;
        public ComputeShader ssrShader;
        public ComputeShader ssaoShader;
        public ComputeShader ssgiShader;
        public ComputeShader combineLUTShader;
        public ComputeShader hiZShader;
        public ComputeShader halfResDownsampleShader;
        public ComputeShader zBinningShader;
        public ComputeShader contactShadowShader;
        public ComputeShader deferredShadingShader;
        public ComputeShader subsurfaceShader;
        public ComputeShader atmosphericLUTShader;
        public ComputeShader volumetricFogShader;
        public ComputeShader volumetricCloudShader;
        public ComputeShader colorPyramidShader;
        public ComputeShader superResolutionShader;
        public ComputeShader postProcessingShader;

        [Header("Shaders")]
        public Shader defaultShaderProxy;

        [Header("Materials")]
        public Material blitMaterial;
        public Material defaultMaterialProxy;

        [Header("Textures")]
        public Texture2D bestFitNormalTexture;

        [Header("Shadow Settings")]
        public int cascadeShadowMapResolution = 2048;
        public int localShadowMapResolution = 2048;
        public float shadowDistance = 128;

        [System.NonSerialized] public InfinityRenderPipeline renderPipeline;
        public override Shader defaultShader { get { return defaultShaderProxy; } }
        public override Material defaultMaterial { get { return defaultMaterialProxy; } }

        protected override RenderPipeline CreatePipeline() 
        {
            EnsureAssignedComputeShaders();
            renderPipeline = new InfinityRenderPipeline(this);
            Shader.SetGlobalTexture("g_BestFitNormal_LUT", bestFitNormalTexture);
            return renderPipeline;
        }

        protected override void OnValidate() 
        {
            base.OnValidate();
            EnsureAssignedComputeShaders();
        }

        const string PackageRoot = "Packages/com.infinity.render-pipeline/";

        internal void EnsureAssignedComputeShaders()
        {
#if UNITY_EDITOR
            meshDrawPipelineCS = CoalesceCompute(meshDrawPipelineCS, "Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute", "CullInstances", "ClearCommandCounts", "CompactCommandInstances", "PrefixSumCommands", "ScatterVisibleInstances", "BuildIndirectArgs");
            taaShader = CoalesceCompute(taaShader, "Shaders/RenderingFeature/TemporalAntiAliasing/Compute_TemporalAntiAliasing.compute", "Main");
            ssrShader = CoalesceCompute(ssrShader, "Shaders/RenderingFeature/ScreenSpaceReflection/Compute_ScreenSpaceReflection.compute", "Raytracing");
            ssaoShader = CoalesceCompute(ssaoShader, "Shaders/RenderingFeature/ScreenSpaceAmbientOcclusion/Compute_GroundTruthOcclusion.compute", "OcclusionTrace", "OcclusionSpatialX", "OcclusionSpatialY", "OcclusionTemporal", "OcclusionUpsample");
            ssgiShader = CoalesceCompute(ssgiShader, "Shaders/RenderingFeature/ScreenSpaceIndirectDiffuse/Compute_ScreenSpaceIndirectDiffuse.compute", "Raytracing");
            combineLUTShader = CoalesceCompute(combineLUTShader, "Shaders/ColorGrading/Compute_CombineLUTs.compute", "MainCS");
            hiZShader = CoalesceCompute(hiZShader, "Shaders/RenderingFeature/PyramidDepth/Compute_PyramidDepth.compute", "HiZ_Generation");
            halfResDownsampleShader = CoalesceCompute(halfResDownsampleShader, "Shaders/RenderingFeature/HalfResDownsample/Compute_HalfResDownsample.compute", "HalfResDownsample");
            zBinningShader = CoalesceCompute(zBinningShader, "Shaders/RenderingFeature/ZBinningLightList/Compute_ZBinningLightList.compute", "ZBinning", "TileLighting");
            contactShadowShader = CoalesceCompute(contactShadowShader, "Shaders/RenderingFeature/ContactShadow/Compute_ContactShadow.compute", "ContactShadowCS");
            deferredShadingShader = CoalesceCompute(deferredShadingShader, "Shaders/RenderingFeature/DeferredShading/Compute_DeferredShading.compute", "DeferredShadingCS");
            atmosphericLUTShader = CoalesceCompute(atmosphericLUTShader, "Shaders/RenderingFeature/AtmosphericLUT/Compute_AtmosphericLUT.compute", "TransmittanceLUT", "MultiScatteringLUT", "SkyViewLUT", "AerialPerspectiveLUT", "AtmosphereCubemap", "SunBuffer", "AtmosphereComposite");
            volumetricFogShader = CoalesceCompute(volumetricFogShader, "Shaders/RenderingFeature/VolumetricFog/Compute_VolumetricFog.compute", "ScatterDensity", "Integrate");
            volumetricCloudShader = CoalesceCompute(volumetricCloudShader, "Shaders/RenderingFeature/VolumetricCloud/Compute_VolumetricCloud.compute", "VolumetricCloudCS");
            colorPyramidShader = CoalesceCompute(colorPyramidShader, "Shaders/RenderingFeature/PyramidColor/Compute_PyramidColor.compute", "KMain");
            superResolutionShader = CoalesceCompute(superResolutionShader, "Shaders/RenderingFeature/SuperResolution/Compute_SuperResolution.compute", "SuperResolutionCS");
            postProcessingShader = CoalesceCompute(postProcessingShader, "Shaders/RenderingFeature/PostProcessing/Compute_PostProcessing.compute", "BloomDownsample", "BloomUpsample", "FinalCombine");
            subsurfaceShader = KeepIfKernels(subsurfaceShader, "BurleySubsurfaceCS");
#endif
        }

#if UNITY_EDITOR
        static ComputeShader CoalesceCompute(ComputeShader current, string relativePath, params string[] requiredKernels)
        {
            // A serialized reference is no proof of a successful compile, so it takes the same kernel
            // check as a freshly loaded one. Otherwise a broken shader reaches the passes and every
            // record-time gate waves it through.
            ComputeShader validated = KeepIfKernels(current, requiredKernels);
            if (validated != null)
            {
                return validated;
            }

            ComputeShader loaded = AssetDatabase.LoadAssetAtPath<ComputeShader>(PackageRoot + relativePath);
            if (loaded == current)
            {
                return null;
            }

            return KeepIfKernels(loaded, requiredKernels);
        }

        static ComputeShader KeepIfKernels(ComputeShader shader, params string[] requiredKernels)
        {
            if (!GraphicsUtility.HasRequiredKernels(shader, requiredKernels))
            {
                return null;
            }

            return shader;
        }
#endif

        protected override void OnDisable() 
        {
            base.OnDisable();
        }
    }
}
