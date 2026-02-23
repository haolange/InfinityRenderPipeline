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
        CopyMotionDepth,
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
        ComputeDeferredShading,
        RenderForward,
        ComputeBurleySubsurface,
        RenderAtmosphericSkyAndFog,
        RenderSkyBox,
        RenderAtmosphere,
        ComputeVolumetricFog,
        ComputeVolumetricCloud,
        RenderTranslucentDepth,
        ComputeColorPyramid,
        RenderForwardTranslucent,
        ComputeSuperResolution,
        ComputeAntiAliasing,
        CopyHistoryAntiAliasing,
        ComputePostProcessing,
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
        public static int MotionBuffer = Shader.PropertyToID("_MotionTexture");
        public static int MotionDepthBuffer = Shader.PropertyToID("_MotionDepthTexture");
        public static int CascadeShadowMap = Shader.PropertyToID("_CascadeShadowMapTexture");
        public static int LocalShadowMap = Shader.PropertyToID("_LocalShadowMapTexture");
        public static int AtmosphereTransmittanceLUT = Shader.PropertyToID("_AtmosphereTransmittanceLUT");
        public static int AtmosphereScatteringLUT = Shader.PropertyToID("_AtmosphereScatteringLUT");
        public static int AtmosphereMultiScatteringLUT = Shader.PropertyToID("_AtmosphereMultiScatteringLUT");
        public static int ZBinLightListBuffer = Shader.PropertyToID("_ZBinLightListBuffer");
        public static int TileLightListBuffer = Shader.PropertyToID("_TileLightListBuffer");
        public static int OcclusionBuffer = Shader.PropertyToID("_OcclusionTexture");
        public static int SpatialTempBuffer = Shader.PropertyToID("_SpatialTempTexture");
        public static int ContactShadowBuffer = Shader.PropertyToID("_ContactShadowTexture");
        public static int SSRBuffer = Shader.PropertyToID("_SSRTexture");
        public static int SSRHitPDFBuffer = Shader.PropertyToID("_SSRHitPDFTexture");
        public static int SSGIBuffer = Shader.PropertyToID("_SSGITexture");
        public static int LightingBuffer = Shader.PropertyToID("_LightingTexture");
        public static int SubsurfaceBuffer = Shader.PropertyToID("_SubsurfaceTexture");
        public static int VolumetricFogBuffer = Shader.PropertyToID("_VolumetricFogTexture");
        public static int VolumetricCloudBuffer = Shader.PropertyToID("_VolumetricCloudTexture");
        public static int TranslucentDepthBuffer = Shader.PropertyToID("_TranslucentDepthTexture");
        public static int ColorPyramidBuffer = Shader.PropertyToID("_ColorPyramidTexture");
        public static int TranslucentLightingBuffer = Shader.PropertyToID("_TranslucentLightingTexture");
        public static int SuperResolutionBuffer = Shader.PropertyToID("_SuperResolutionTexture");
        public static int AntiAliasingBuffer = Shader.PropertyToID("_AntiAliasingBuffer");
        public static int PostProcessBuffer = Shader.PropertyToID("_PostProcessTexture");
        public static int BloomBuffer = Shader.PropertyToID("_BloomTexture");
        public static int MainTexture = Shader.PropertyToID("_MainTex");
        public static int ScaleBias = Shader.PropertyToID("_ScaleBais");
        public static int MeshBatchOffset = Shader.PropertyToID("meshBatchOffset");
        public static int MeshBatchIndexs = Shader.PropertyToID("meshBatchIndexs");
        public static int MeshBatchBuffer = Shader.PropertyToID("meshBatchBuffer");
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
        public static ShaderTagId ForwardTranslucentPass = new ShaderTagId("ForwardTranslucentPass");
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
