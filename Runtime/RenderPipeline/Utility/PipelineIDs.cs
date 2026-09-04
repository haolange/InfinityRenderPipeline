using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace InfinityTech.Rendering.Pipeline
{
    public enum CustomSamplerId
    {
        ComputeCombineLuts,
        RenderDepth,
        RenderDBuffer,
        RenderGBuffer,
        RenderObjectMotion,
        RenderCameraMotion,
        ComputeHiZ,
        ComputeHalfResDownsample,
        RenderCascadeShadow,
        RenderLocalShadow,
        ComputeAtmosphericLUT,
        ComputeZBinningLightList,
        ComputeGroundTruthOcclusion,
        ComputeContactShadow,
        ComputeScreenSpaceReflection,
        ComputeScreenSpaceIndirect,
        ComputeOpaqueLightingPyramid,
        ComputeScreenSpaceComposite,
        CopyHistorySSR,
        CopyHistorySSGI,
        ComputeDeferredShading,
        RenderForward,
        ComputeBurleySubsurface,
        RenderAtmosphericSkyAndFog,
        ComputeVolumetricFog,
        ComputeVolumetricCloud,
        CopyHistoryVolumetricFog,
        CopyHistoryVolumetricCloud,
        ComputeFogComposite,
        ClearReactiveMask,
        RenderTranslucentDepth,
        RenderTranslucentT0,
        RenderTranslucentT1,
        RenderTranslucentT2,
        ComputeColorPyramid,
        CopyHistoryOcclusion,
        CopyHistoryOcclusionDepth,
        ComputeSuperResolution,
        ComputeAntiAliasing,
        CopyHistoryAntiAliasing,
        CopyHistoryDepth,
        CopyHistorySuperResolution,
        PostProcessing,
        ComputeBloom,
        ComputePostCombine,
        ComputeExposure,
        ComputeOutputTransform,
        RenderWireOverlay,
        RenderGizmos,
        Present,
        Max,
    }

    public static class InfinityCustomSamplerExtension
    {
        static CustomSampler[] s_Samplers;

        public static CustomSampler GetSampler(this CustomSamplerId samplerId)
        {
            // Lazy init
            if (s_Samplers == null)
            {
                s_Samplers = new CustomSampler[(int)CustomSamplerId.Max];

                for (int i = 0; i < (int)CustomSamplerId.Max; ++i)
                {
                    var id = (CustomSamplerId)i;
                    s_Samplers[i] = CustomSampler.Create("C#_" + id);
                }
            }

            return s_Samplers[(int)samplerId];
        }
    }

    public static class InfinityShaderIDs
    {
        public static int DepthBuffer = Shader.PropertyToID("_DepthTexture");
        public static int HiZBuffer = Shader.PropertyToID("_HiZTexture");
        public static int HalfResDepthBuffer = Shader.PropertyToID("_HalfResDepthTexture");
        public static int HalfResNormalBuffer = Shader.PropertyToID("_HalfResNormalTexture");
        public static int DBufferA = Shader.PropertyToID("_DBufferTextureA");
        public static int DBufferB = Shader.PropertyToID("_DBufferTextureB");
        public static int DBufferC = Shader.PropertyToID("_DBufferTextureC");
        public static int GBufferA = Shader.PropertyToID("_GBufferTextureA");
        public static int GBufferB = Shader.PropertyToID("_GBufferTextureB");
        public static int GBufferC = Shader.PropertyToID("_GBufferTextureC");
        public static int MotionBuffer = Shader.PropertyToID("_MotionTexture");
        public static int CascadeShadowMap = Shader.PropertyToID("_CascadeShadowMapTexture");
        public static int LocalShadowMap = Shader.PropertyToID("_LocalShadowMapTexture");
        public static int AtmosphereTransmittanceLUT = Shader.PropertyToID("_AtmosphereTransmittanceLUT");
        public static int AtmosphereScatteringLUT = Shader.PropertyToID("_AtmosphereScatteringLUT");
        public static int AtmosphereMultiScatteringLUT = Shader.PropertyToID("_AtmosphereMultiScatteringLUT");
        public static int AtmosphereSkyViewLUT = Shader.PropertyToID("_AtmosphereSkyViewLUT");
        public static int AtmosphereAerialPerspectiveLUT = Shader.PropertyToID("_AtmosphereAerialPerspectiveLUT");
        public static int AtmosphereCubemap = Shader.PropertyToID("_AtmosphereCubemap");
        public static int AtmosphereSunBuffer = Shader.PropertyToID("_AtmosphereSunBuffer");
        public static int AtmosphereSkySH = Shader.PropertyToID("_AtmosphereSkySH");
        public static int AtmosphereGGXPrefilter = Shader.PropertyToID("_AtmosphereGGXPrefilter");
        public static int AtmosphereIBLMaxMip = Shader.PropertyToID("_AtmosphereIBLMaxMip");
        public static int ZBinLightListBuffer = Shader.PropertyToID("_ZBinLightListBuffer");
        public static int ZBinCountBuffer = Shader.PropertyToID("_ZBinCountBuffer");
        public static int ZBinRangeBuffer = Shader.PropertyToID("_ZBinRangeBuffer");
        public static int TileLightListBuffer = Shader.PropertyToID("_TileLightListBuffer");
        public static int TileLightCountBuffer = Shader.PropertyToID("_TileLightCountBuffer");
        public static int TileLightRangeBuffer = Shader.PropertyToID("_TileLightRangeBuffer");
        public static int OcclusionBuffer = Shader.PropertyToID("_OcclusionTexture");
        public static int OcclusionHalfBuffer = Shader.PropertyToID("_OcclusionHalfTexture");
        public static int SpatialTempBuffer = Shader.PropertyToID("_SpatialTempTexture");
        public static int HistoryOcclusionBuffer = Shader.PropertyToID("_HistoryOcclusionTexture");
        public static int HistoryOcclusionDepthBuffer = Shader.PropertyToID("_HistoryOcclusionDepthTexture");
        public static int ContactShadowBuffer = Shader.PropertyToID("_ContactShadowTexture");
        public static int SSRBuffer = Shader.PropertyToID("_SSRTexture");
        public static int SSRHitPDFBuffer = Shader.PropertyToID("_SSRHitPDFTexture");
        public static int SSRRadianceBuffer = Shader.PropertyToID("_SSRRadianceTexture");
        public static int SSRSpatialBuffer = Shader.PropertyToID("_SSRSpatialTexture");
        public static int SSRTemporalBuffer = Shader.PropertyToID("_SSRTemporalTexture");
        public static int SSRMomentsBuffer = Shader.PropertyToID("_SSRMomentsTexture");
        public static int SSRDepthNormalBuffer = Shader.PropertyToID("_SSRDepthNormalTexture");
        public static int HistorySSRRadianceBuffer = Shader.PropertyToID("_HistorySSRRadianceTexture");
        public static int HistorySSRMomentsBuffer = Shader.PropertyToID("_HistorySSRMomentsTexture");
        public static int HistorySSRDepthNormalBuffer = Shader.PropertyToID("_HistorySSRDepthNormalTexture");
        public static int SSGIBuffer = Shader.PropertyToID("_SSGITexture");
        public static int SSGIRadianceBuffer = Shader.PropertyToID("_SSGIRadianceTexture");
        public static int SSGISpatialBuffer = Shader.PropertyToID("_SSGISpatialTexture");
        public static int SSGITemporalBuffer = Shader.PropertyToID("_SSGITemporalTexture");
        public static int SSGIMomentsBuffer = Shader.PropertyToID("_SSGIMomentsTexture");
        public static int SSGIDepthNormalBuffer = Shader.PropertyToID("_SSGIDepthNormalTexture");
        public static int HistorySSGIRadianceBuffer = Shader.PropertyToID("_HistorySSGIRadianceTexture");
        public static int HistorySSGIMomentsBuffer = Shader.PropertyToID("_HistorySSGIMomentsTexture");
        public static int HistorySSGIDepthNormalBuffer = Shader.PropertyToID("_HistorySSGIDepthNormalTexture");
        public static int LightingBuffer = Shader.PropertyToID("_LightingTexture");
        public static int OpaqueLightingPyramidBuffer = Shader.PropertyToID("_OpaqueLightingPyramidTexture");
        public static int OpaqueSceneColorBuffer = Shader.PropertyToID("_OpaqueSceneColorTexture");
        public static int FoggedSceneColorBuffer = Shader.PropertyToID("_FoggedSceneColorTexture");
        public static int ReactiveMaskBuffer = Shader.PropertyToID("_ReactiveMaskTexture");
        public static int HistoryVolumetricFogBuffer = Shader.PropertyToID("_HistoryVolumetricFogTexture");
        public static int HistoryVolumetricCloudBuffer = Shader.PropertyToID("_HistoryVolumetricCloudTexture");
        public static int ScreenSpaceCompositeBuffer = Shader.PropertyToID("_ScreenSpaceCompositeTexture");
        public static int SubsurfaceBuffer = Shader.PropertyToID("_SubsurfaceTexture");
        public static int VolumetricFogBuffer = Shader.PropertyToID("_VolumetricFogTexture");
        public static int VolumetricCloudBuffer = Shader.PropertyToID("_VolumetricCloudTexture");
        public static int TranslucentDepthBuffer = Shader.PropertyToID("_TranslucentDepthTexture");
        public static int ColorPyramidBuffer = Shader.PropertyToID("_ColorPyramidTexture");
        public static int SuperResolutionBuffer = Shader.PropertyToID("_SuperResolutionTexture");
        public static int AntiAliasingBuffer = Shader.PropertyToID("_AntiAliasingBuffer");
        public static int PostProcessBuffer = Shader.PropertyToID("_PostProcessTexture");
        public static int DisplayColorBuffer = Shader.PropertyToID("_DisplayColorTexture");
        public static int BloomBuffer = Shader.PropertyToID("_BloomTexture");
        public static int CombineLookupTexture = Shader.PropertyToID("CombineLookupTexture");
        public static int ExposureEVBuffer = Shader.PropertyToID("_ExposureEVTexture");
        public static int MainTexture = Shader.PropertyToID("_MainTex");
        public static int ScaleBias = Shader.PropertyToID("_ScaleBais");
        public static int InstanceIndexOffset = Shader.PropertyToID("instanceIndexOffset");
        public static int InstanceIndexBuffer = Shader.PropertyToID("instanceIndexBuffer");
        public static int TransformBuffer = Shader.PropertyToID("transformBuffer");
        public static int PreviousTransformBuffer = Shader.PropertyToID("previousTransformBuffer");
    }

    public static class InfinityPassIDs
    {
        public static ShaderTagId DepthPass = new ShaderTagId("DepthPass");
        public static ShaderTagId DBufferPass = new ShaderTagId("DBufferPass");
        public static ShaderTagId GBufferPass = new ShaderTagId("GBufferPass");
        public static ShaderTagId ShadowPass = new ShaderTagId("ShadowPass");
        public static ShaderTagId MotionPass = new ShaderTagId("MotionPass");
        public static ShaderTagId ForwardPass = new ShaderTagId("ForwardPass");
        public static ShaderTagId TranslucentDepthPass = new ShaderTagId("TranslucentDepthPass");
        public static ShaderTagId TranslucentT0Pass = new ShaderTagId("TranslucentT0Pass");
        public static ShaderTagId TranslucentT1Pass = new ShaderTagId("TranslucentT1Pass");
        public static ShaderTagId TranslucentT2Pass = new ShaderTagId("TranslucentT2Pass");
    }

    public static class InfinityRenderQueue
    {
        public enum Priority
        {
            Background = UnityEngine.Rendering.RenderQueue.Background,
            OpaqueLast = UnityEngine.Rendering.RenderQueue.GeometryLast,
            TransparentFirst = UnityEngine.Rendering.RenderQueue.Transparent,
            TransparentLast = UnityEngine.Rendering.RenderQueue.Transparent + 500,
        }
        public static readonly RenderQueueRange k_RenderQueue_AllOpaque = new RenderQueueRange { lowerBound = (int)Priority.Background, upperBound = (int)Priority.OpaqueLast };
        public static readonly RenderQueueRange k_RenderQueue_AllTransparent = new RenderQueueRange { lowerBound = (int)Priority.TransparentFirst, upperBound = (int)Priority.TransparentLast };
    }
}
