using UnityEngine;
using InfinityTech.Rendering.RenderGraph;
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
        ComputeRayTracedOcclusion,
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
        ComputeTemporalSharpen,
        CopyHistoryAntiAliasing,
        CopyHistoryDepth,
        CopyHistorySuperResolution,
        PostProcessing,
        ComputeBloom,
        ComputePostCombine,
        ComputeExposure,
        ComputeDebugView,
        RenderWireOverlay,
        RenderGizmos,
        RenderUIOverlay,
        CopyPostProcessInput,
        ResolveDisplayDepth,
        Present,
        ComputeMeshGpuCull,
        ComputeMeshGpuCullCascade,
        ComputeMeshGpuCullLocal,
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

    // Semantic graph resources are RGResourceKey (registered in RGScoper); plain shader property IDs stay int.
    public static class InfinityShaderIDs
    {
        public static readonly RGResourceKey BakedDiffuseBuffer = new RGResourceKey("BakedDiffuseBuffer");
        public static readonly RGResourceKey BakedOcclusionBuffer = new RGResourceKey("BakedOcclusionBuffer");
        public static readonly RGResourceKey IndirectDiffuseBuffer = new RGResourceKey("IndirectDiffuseBuffer");
        public static readonly RGResourceKey IndirectSpecularBuffer = new RGResourceKey("IndirectSpecularBuffer");
        public static readonly RGResourceKey PreDebugColorBuffer = new RGResourceKey("_PreDebugColorTexture");
        public static readonly RGResourceKey DisplayDepthBuffer = new RGResourceKey("_DisplayDepthTexture");
        public static readonly RGResourceKey DepthBuffer = new RGResourceKey("_DepthTexture");
        public static readonly RGResourceKey HiZBuffer = new RGResourceKey("_HiZTexture");
        public static readonly RGResourceKey HalfResDepthBuffer = new RGResourceKey("_HalfResDepthTexture");
        public static readonly RGResourceKey HalfResNormalBuffer = new RGResourceKey("_HalfResNormalTexture");
        public static readonly RGResourceKey DBufferA = new RGResourceKey("_DBufferTextureA");
        public static readonly RGResourceKey DBufferB = new RGResourceKey("_DBufferTextureB");
        public static readonly RGResourceKey DBufferC = new RGResourceKey("_DBufferTextureC");
        public static readonly RGResourceKey GBufferA = new RGResourceKey("_GBufferTextureA");
        public static readonly RGResourceKey GBufferB = new RGResourceKey("_GBufferTextureB");
        public static readonly RGResourceKey GBufferC = new RGResourceKey("_GBufferTextureC");
        public static readonly RGResourceKey MotionBuffer = new RGResourceKey("_MotionTexture");
        public static readonly RGResourceKey CascadeShadowMap = new RGResourceKey("_CascadeShadowMapTexture");
        public static readonly RGResourceKey LocalShadowMap = new RGResourceKey("_LocalShadowMapTexture");
        public static readonly RGResourceKey AtmosphereTransmittanceLUT = new RGResourceKey("_AtmosphereTransmittanceLUT");
        public static readonly RGResourceKey AtmosphereScatteringLUT = new RGResourceKey("_AtmosphereScatteringLUT");
        public static readonly RGResourceKey AtmosphereMultiScatteringLUT = new RGResourceKey("_AtmosphereMultiScatteringLUT");
        public static readonly RGResourceKey AtmosphereSkyViewLUT = new RGResourceKey("_AtmosphereSkyViewLUT");
        public static readonly RGResourceKey AtmosphereAerialPerspectiveLUT = new RGResourceKey("_AtmosphereAerialPerspectiveLUT");
        public static readonly RGResourceKey AtmosphereCubemap = new RGResourceKey("_AtmosphereCubemap");
        public static readonly RGResourceKey AtmosphereSunBuffer = new RGResourceKey("_AtmosphereSunBuffer");
        public static readonly RGResourceKey AtmosphereSkySH = new RGResourceKey("_AtmosphereSkySH");
        public static readonly RGResourceKey AtmosphereGGXPrefilter = new RGResourceKey("_AtmosphereGGXPrefilter");
        public static readonly int AtmosphereIBLMaxMip = Shader.PropertyToID("_AtmosphereIBLMaxMip");
        public static readonly RGResourceKey ZBinLightListBuffer = new RGResourceKey("_ZBinLightListBuffer");
        public static readonly RGResourceKey ZBinCountBuffer = new RGResourceKey("_ZBinCountBuffer");
        public static readonly RGResourceKey ZBinRangeBuffer = new RGResourceKey("_ZBinRangeBuffer");
        public static readonly RGResourceKey TileLightListBuffer = new RGResourceKey("_TileLightListBuffer");
        public static readonly RGResourceKey TileLightCountBuffer = new RGResourceKey("_TileLightCountBuffer");
        public static readonly RGResourceKey TileLightRangeBuffer = new RGResourceKey("_TileLightRangeBuffer");
        public static readonly RGResourceKey OcclusionBuffer = new RGResourceKey("_OcclusionTexture");
        public static readonly RGResourceKey OcclusionHalfBuffer = new RGResourceKey("_OcclusionHalfTexture");
        public static readonly RGResourceKey SpatialTempBuffer = new RGResourceKey("_SpatialTempTexture");
        public static readonly RGResourceKey HistoryOcclusionBuffer = new RGResourceKey("_HistoryOcclusionTexture");
        public static readonly RGResourceKey HistoryOcclusionDepthBuffer = new RGResourceKey("_HistoryOcclusionDepthTexture");
        public static readonly RGResourceKey ContactShadowBuffer = new RGResourceKey("_ContactShadowTexture");
        public static readonly RGResourceKey SSRBuffer = new RGResourceKey("_SSRTexture");
        public static readonly RGResourceKey SSRHitPDFBuffer = new RGResourceKey("_SSRHitPDFTexture");
        public static readonly RGResourceKey SSRRadianceBuffer = new RGResourceKey("_SSRRadianceTexture");
        public static readonly RGResourceKey SSRSpatialBuffer = new RGResourceKey("_SSRSpatialTexture");
        public static readonly RGResourceKey SSRTemporalBuffer = new RGResourceKey("_SSRTemporalTexture");
        public static readonly RGResourceKey SSRMomentsBuffer = new RGResourceKey("_SSRMomentsTexture");
        public static readonly RGResourceKey SSRDepthNormalBuffer = new RGResourceKey("_SSRDepthNormalTexture");
        public static readonly RGResourceKey HistorySSRRadianceBuffer = new RGResourceKey("_HistorySSRRadianceTexture");
        public static readonly RGResourceKey HistorySSRMomentsBuffer = new RGResourceKey("_HistorySSRMomentsTexture");
        public static readonly RGResourceKey HistorySSRDepthNormalBuffer = new RGResourceKey("_HistorySSRDepthNormalTexture");
        public static readonly RGResourceKey SSGIBuffer = new RGResourceKey("_SSGITexture");
        public static readonly RGResourceKey SSGIRadianceBuffer = new RGResourceKey("_SSGIRadianceTexture");
        public static readonly RGResourceKey SSGISpatialBuffer = new RGResourceKey("_SSGISpatialTexture");
        public static readonly RGResourceKey SSGITemporalBuffer = new RGResourceKey("_SSGITemporalTexture");
        public static readonly RGResourceKey SSGIMomentsBuffer = new RGResourceKey("_SSGIMomentsTexture");
        public static readonly RGResourceKey SSGIDepthNormalBuffer = new RGResourceKey("_SSGIDepthNormalTexture");
        public static readonly RGResourceKey HistorySSGIRadianceBuffer = new RGResourceKey("_HistorySSGIRadianceTexture");
        public static readonly RGResourceKey HistorySSGIMomentsBuffer = new RGResourceKey("_HistorySSGIMomentsTexture");
        public static readonly RGResourceKey HistorySSGIDepthNormalBuffer = new RGResourceKey("_HistorySSGIDepthNormalTexture");
        public static readonly RGResourceKey LightingBuffer = new RGResourceKey("_LightingTexture");
        public static readonly RGResourceKey OpaqueLightingPyramidBuffer = new RGResourceKey("_OpaqueLightingPyramidTexture");
        public static readonly RGResourceKey OpaqueSceneColorBuffer = new RGResourceKey("_OpaqueSceneColorTexture");
        public static readonly RGResourceKey FoggedSceneColorBuffer = new RGResourceKey("_FoggedSceneColorTexture");
        public static readonly RGResourceKey ReactiveMaskBuffer = new RGResourceKey("_ReactiveMaskTexture");
        public static readonly RGResourceKey HistoryVolumetricFogBuffer = new RGResourceKey("_HistoryVolumetricFogTexture");
        public static readonly RGResourceKey HistoryVolumetricCloudBuffer = new RGResourceKey("_HistoryVolumetricCloudTexture");
        public static readonly RGResourceKey ScreenSpaceCompositeBuffer = new RGResourceKey("_ScreenSpaceCompositeTexture");
        public static readonly RGResourceKey SubsurfaceBuffer = new RGResourceKey("_SubsurfaceTexture");
        public static readonly RGResourceKey VolumetricFogBuffer = new RGResourceKey("_VolumetricFogTexture");
        public static readonly RGResourceKey VolumetricCloudBuffer = new RGResourceKey("_VolumetricCloudTexture");
        public static readonly RGResourceKey TranslucentDepthBuffer = new RGResourceKey("_TranslucentDepthTexture");
        public static readonly RGResourceKey ColorPyramidBuffer = new RGResourceKey("_ColorPyramidTexture");
        public static readonly RGResourceKey SuperResolutionBuffer = new RGResourceKey("_SuperResolutionTexture");
        public static readonly RGResourceKey TAAAccumulationBuffer = new RGResourceKey("_TAAAccumulationBuffer");
        public static readonly RGResourceKey TAADepthBuffer = new RGResourceKey("_TAADepthBuffer");
        public static readonly RGResourceKey TAAReprojectionBuffer = new RGResourceKey("_TAAReprojectionBuffer");
        public static readonly RGResourceKey MotionMetadataBuffer = new RGResourceKey("_MotionMetadataBuffer");
        public static readonly RGResourceKey AntiAliasingBuffer = new RGResourceKey("_AntiAliasingBuffer");
        public static readonly RGResourceKey TAAConfidenceBuffer = new RGResourceKey("_TAAConfidenceTexture");
        public static readonly RGResourceKey PostProcessBuffer = new RGResourceKey("_PostProcessTexture");
        public static readonly RGResourceKey DisplayColorBuffer = new RGResourceKey("_DisplayColorTexture");
        public static readonly RGResourceKey BloomBuffer = new RGResourceKey("_BloomTexture");
        public static readonly RGResourceKey CombineLookupTexture = new RGResourceKey("CombineLookupTexture");
        public static readonly RGResourceKey ExposureEVBuffer = new RGResourceKey("_ExposureEVTexture");
        public static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        public static readonly int ScaleBias = Shader.PropertyToID("_ScaleBais");
        public static readonly int InstanceIndexOffset = Shader.PropertyToID("instanceIndexOffset");
        public static readonly int InstanceIndexBuffer = Shader.PropertyToID("instanceIndexBuffer");
        public static readonly int TransformBuffer = Shader.PropertyToID("transformBuffer");
        public static readonly int PreviousTransformBuffer = Shader.PropertyToID("previousTransformBuffer");
        public static readonly int RenderingLayerBuffer = Shader.PropertyToID("renderingLayerBuffer");
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
