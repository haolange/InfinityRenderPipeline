using UnityEngine;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace InfinityTech.Rendering.Pipeline
{
    public enum CustomSamplerId
    {
        ComputeCombineLuts,
        RasterDepth,
        RasterDepthRecovery,
        RasterDBuffer,
        RasterGBuffer,
        RasterObjectMotion,
        RasterCameraMotion,
        ComputeHiZ,
        ComputeEarlyFurthestHiZ,
        ComputeHalfResDownsample,
        RasterCascadeShadow,
        RasterLocalShadow,
        RasterCopyShadowDepthTiles,
        ComputeShadowMoments,
        ComputeAtmosphericLUT,
        ComputeLightTileMask,
        ComputeLightCookies,
        ComputeReflectionProbes,
        ComputeProbeTileMask,
        ComputeGroundTruthOcclusion,
        ComputeRayTracedOcclusion,
        RayTracingOcclusion,
        ComputeContactShadow,
        ComputeScreenSpaceReflection,
        ComputeScreenSpaceIndirect,
        ComputeTemporalColorPyramid,
        ComputeScreenSpaceComposite,
        CopyHistorySSR,
        CopyHistorySSGI,
        ComputeDeferredShading,
        RasterForward,
        ComputeBurleySubsurface,
        ComputeAtmosphericSkyAndFog,
        ComputeVolumetricFog,
        ComputeVolumetricCloud,
        CopyHistoryVolumetricFog,
        CopyHistoryVolumetricCloud,
        ComputeFogComposite,
        RasterTranslucentDepth,
        RasterTranslucentT0,
        RasterTranslucentT1,
        RasterTranslucentT2,
        ComputeColorPyramid,
        ComputeSuperResolution,
        ComputeAntiAliasing,
        PostProcessing,
        ComputeBloom,
        RasterPostCombine,
        ComputeExposure,
        ComputeDebugView,
        RasterWireOverlay,
        RasterGizmos,
        RasterUIOverlay,
        RasterOutputCopy,
        RasterUpscaleDepthForEditorOverlays,
        RasterFinalOutput,
        Depth,
        GBuffer,
        ObjectMotion,
        CascadeShadow,
        LocalShadow,
        Forward,
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
        public static readonly RGResourceKey HiZClosestBuffer = new RGResourceKey("_HiZClosestTexture");
        public static readonly RGResourceKey HiZFurthestBuffer = new RGResourceKey("_HiZFurthestTexture");
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
        public static readonly RGResourceKey ReflectionProbeAtlas = new RGResourceKey("_ReflectionProbeAtlasTexture");
        public static readonly RGResourceKey ProbeTileMaskBuffer = new RGResourceKey("_ProbeTileMaskBuffer");
        public static readonly RGResourceKey LightCookieAtlas = new RGResourceKey("_LightCookieAtlasTexture");
        public static readonly RGResourceKey CachedLocalShadowMap = new RGResourceKey("_CachedLocalShadowMapTexture");
        public static readonly RGResourceKey CascadeShadowMoments = new RGResourceKey("_CascadeShadowMomentsTexture");
        public static readonly RGResourceKey LocalShadowMoments = new RGResourceKey("_LocalShadowMomentsTexture");
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
        public static readonly RGResourceKey OcclusionBuffer = new RGResourceKey("_OcclusionTexture");
        public static readonly RGResourceKey OcclusionHalfBuffer = new RGResourceKey("_OcclusionHalfTexture");
        public static readonly RGResourceKey SpatialTempBuffer = new RGResourceKey("_SpatialTempTexture");
        public static readonly RGResourceKey HistoryOcclusionBuffer = new RGResourceKey("_HistoryOcclusionTexture");
        public static readonly RGResourceKey HistoryOcclusionDepthBuffer = new RGResourceKey("_HistoryOcclusionDepthTexture");
        public static readonly RGResourceKey ContactShadowBuffer = new RGResourceKey("_ContactShadowTexture");
        public static readonly RGResourceKey ContactShadowMaskBuffer = new RGResourceKey("_ContactShadowMaskTexture");
        public static readonly RGResourceKey LightTileMaskBuffer = new RGResourceKey("_LightTileMaskBuffer");
        public static readonly RGResourceKey SSRBuffer = new RGResourceKey("_SSRTexture");
        public static readonly RGResourceKey SSRRayHitBuffer = new RGResourceKey("_SSRRayHitTexture");
        public static readonly RGResourceKey SSRResolvedBuffer = new RGResourceKey("_SSRResolvedTexture");
        public static readonly RGResourceKey SSRHitDistanceBuffer = new RGResourceKey("_SSRHitDistanceTexture");
        public static readonly RGResourceKey SSRFilteredBuffer = new RGResourceKey("_SSRFilteredTexture");
        public static readonly RGResourceKey SSRTemporalBuffer = new RGResourceKey("_SSRTemporalTexture");
        public static readonly RGResourceKey SSRMetaBuffer = new RGResourceKey("_SSRMetaTexture");
        public static readonly RGResourceKey SSRDebugBuffer = new RGResourceKey("_SSRDebugTexture");
        public static readonly RGResourceKey SSRHitMeanBuffer = new RGResourceKey("_SSRHitMeanTexture");
        public static readonly RGResourceKey SSRHitDevBuffer = new RGResourceKey("_SSRHitDevTexture");
        public static readonly RGResourceKey SSRVarianceBuffer = new RGResourceKey("_SSRVarianceTexture");
        public static readonly RGResourceKey HistorySSRRadianceBuffer = new RGResourceKey("_HistorySSRRadianceTexture");
        public static readonly RGResourceKey HistorySSRMetaBuffer = new RGResourceKey("_HistorySSRMetaTexture");
        public static readonly RGResourceKey HistorySSRVarianceBuffer = new RGResourceKey("_HistorySSRVarianceTexture");
        public static readonly RGResourceKey HistorySSGINarrowBuffer = new RGResourceKey("_HistorySSGINarrowTexture");
        public static readonly RGResourceKey SSGIBuffer = new RGResourceKey("_SSGITexture");
        public static readonly RGResourceKey SSGIRayHitBuffer = new RGResourceKey("_SSGIRayHitTexture");
        public static readonly RGResourceKey SSGITraceRadianceBuffer = new RGResourceKey("_SSGITraceRadianceTexture");
        public static readonly RGResourceKey SSGIHitDistanceBuffer = new RGResourceKey("_SSGIHitDistanceTexture");
        public static readonly RGResourceKey SSGIFilteredBuffer = new RGResourceKey("_SSGIFilteredTexture");
        public static readonly RGResourceKey SSGICombinerNarrowBuffer = new RGResourceKey("_SSGICombinerNarrowTexture");
        public static readonly RGResourceKey SSGITemporalBuffer = new RGResourceKey("_SSGITemporalTexture");
        public static readonly RGResourceKey SSGIMetaBuffer = new RGResourceKey("_SSGIMetaTexture");
        public static readonly RGResourceKey HistorySSGIRadianceBuffer = new RGResourceKey("_HistorySSGIRadianceTexture");
        public static readonly RGResourceKey HistorySSGIMetaBuffer = new RGResourceKey("_HistorySSGIMetaTexture");
        public static readonly RGResourceKey LightingBuffer = new RGResourceKey("_LightingTexture");
        public static readonly RGResourceKey TemporalColorMipBuffer = new RGResourceKey("_TemporalColorMipTexture");
        public static readonly RGResourceKey TemporalColorSourceBuffer = new RGResourceKey("_TemporalColorSourceTexture");
        public static readonly RGResourceKey TemporalColorDepthBuffer = new RGResourceKey("_TemporalColorDepthTexture");
        public static readonly RGResourceKey OpaqueSceneColorBuffer = new RGResourceKey("_OpaqueSceneColorTexture");
        public static readonly RGResourceKey FoggedSceneColorBuffer = new RGResourceKey("_FoggedSceneColorTexture");
        public static readonly RGResourceKey ReactiveMaskBuffer = new RGResourceKey("_ReactiveMaskTexture");
        public static readonly RGResourceKey HistoryVolumetricFogBuffer = new RGResourceKey("_HistoryVolumetricFogTexture");
        public static readonly RGResourceKey HistoryVolumetricCloudBuffer = new RGResourceKey("_HistoryVolumetricCloudTexture");
        public static readonly RGResourceKey ScreenSpaceCompositeBuffer = new RGResourceKey("_ScreenSpaceCompositeTexture");
        public static readonly RGResourceKey ScatterDiffuseBuffer = new RGResourceKey("_ScatterDiffuseTexture");
        public static readonly RGResourceKey SubsurfaceBuffer = new RGResourceKey("_SubsurfaceTexture");
        public static readonly RGResourceKey VolumetricFogBuffer = new RGResourceKey("_VolumetricFogTexture");
        public static readonly RGResourceKey VolumetricCloudBuffer = new RGResourceKey("_VolumetricCloudTexture");
        public static readonly RGResourceKey TranslucentDepthBuffer = new RGResourceKey("_TranslucentDepthTexture");
        public static readonly RGResourceKey ColorPyramidBuffer = new RGResourceKey("_ColorPyramidTexture");
        public static readonly RGResourceKey SuperResolutionBuffer = new RGResourceKey("_SuperResolutionTexture");
        public static readonly RGResourceKey SRAccumulationBuffer = new RGResourceKey("_SRAccumulationBuffer");
        public static readonly RGResourceKey SRDepthBuffer = new RGResourceKey("_SRDepthBuffer");
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
        public static ShaderTagId RasterDepthPass = new ShaderTagId("RasterDepthPass");
        public static ShaderTagId RasterDBufferPass = new ShaderTagId("RasterDBufferPass");
        public static ShaderTagId RasterGBufferPass = new ShaderTagId("RasterGBufferPass");
        public static ShaderTagId RasterShadowPass = new ShaderTagId("RasterShadowPass");
        public static ShaderTagId RasterMotionPass = new ShaderTagId("RasterMotionPass");
        public static ShaderTagId RasterForwardPass = new ShaderTagId("RasterForwardPass");
        public static ShaderTagId RasterTranslucentDepthPass = new ShaderTagId("RasterTranslucentDepthPass");
        public static ShaderTagId RasterTranslucentT0Pass = new ShaderTagId("RasterTranslucentT0Pass");
        public static ShaderTagId RasterTranslucentT1Pass = new ShaderTagId("RasterTranslucentT1Pass");
        public static ShaderTagId RasterTranslucentT2Pass = new ShaderTagId("RasterTranslucentT2Pass");
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
