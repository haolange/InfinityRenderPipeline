using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Shader properties that more than one pass binds under the same name. Each pass shader declares its own resource;
    // the name is the shared contract, so its ID exists once here.
    internal static class PassShaderIDs
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal static readonly int Capture_SSRTileClass = Shader.PropertyToID("Capture_SSRTileClass");
#endif
        internal static readonly int InfinityHasProbes = Shader.PropertyToID("_InfinityHasProbes");
        internal static readonly int InfinitySourceDepth = Shader.PropertyToID("_InfinitySourceDepth");
        internal static readonly int Matrix_HistoryViewProj = Shader.PropertyToID("Matrix_HistoryViewProj");
        internal static readonly int Matrix_LastMotionViewProj = Shader.PropertyToID("Matrix_LastMotionViewProj");
        internal static readonly int Matrix_MotionViewProj = Shader.PropertyToID("Matrix_MotionViewProj");
        internal static readonly int SRV_AerialPerspectiveLUT = Shader.PropertyToID("SRV_AerialPerspectiveLUT");
        internal static readonly int SRV_DepthTexture = Shader.PropertyToID("SRV_DepthTexture");
        internal static readonly int SRV_GBufferB = Shader.PropertyToID("SRV_GBufferB");
        internal static readonly int SRV_GBufferC = Shader.PropertyToID("SRV_GBufferC");
        internal static readonly int SRV_GBufferTextureA = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static readonly int SRV_GBufferTextureB = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static readonly int SRV_GBufferTextureC = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static readonly int SRV_HiZClosestTexture = Shader.PropertyToID("SRV_HiZClosestTexture");
        internal static readonly int SRV_HistoryColorTexture = Shader.PropertyToID("SRV_HistoryColorTexture");
        internal static readonly int SRV_HistoryDepthTexture = Shader.PropertyToID("SRV_HistoryDepthTexture");
        internal static readonly int SRV_HistoryMeta = Shader.PropertyToID("SRV_HistoryMeta");
        internal static readonly int SRV_HistoryRadiance = Shader.PropertyToID("SRV_HistoryRadiance");
        internal static readonly int SRV_HitMotionMetadata = Shader.PropertyToID("SRV_HitMotionMetadata");
        internal static readonly int SRV_LightingTexture = Shader.PropertyToID("SRV_LightingTexture");
        internal static readonly int SRV_Meta = Shader.PropertyToID("SRV_Meta");
        internal static readonly int SRV_MotionTexture = Shader.PropertyToID("SRV_MotionTexture");
        internal static readonly int SRV_NormalTexture = Shader.PropertyToID("SRV_NormalTexture");
        internal static readonly int SRV_OcclusionTexture = Shader.PropertyToID("SRV_OcclusionTexture");
        internal static readonly int SRV_SceneColorTexture = Shader.PropertyToID("SRV_SceneColorTexture");
        internal static readonly int SRV_SkyViewLUT = Shader.PropertyToID("SRV_SkyViewLUT");
        internal static readonly int SRV_Temporal = Shader.PropertyToID("SRV_Temporal");
        internal static readonly int SRV_TransmittanceLUT = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static readonly int ScreenParams = Shader.PropertyToID("_ScreenParams");
        internal static readonly int ScreenSpaceDepthParams = Shader.PropertyToID("ScreenSpaceDepthParams");
        internal static readonly int ShadowCasterBias = Shader.PropertyToID("_ShadowCasterBias");
        internal static readonly int ShadowCasterLight = Shader.PropertyToID("_ShadowCasterLight");
        internal static readonly int UAV_HitDistance = Shader.PropertyToID("UAV_HitDistance");
        internal static readonly int UAV_LightingTexture = Shader.PropertyToID("UAV_LightingTexture");
        internal static readonly int UAV_Meta = Shader.PropertyToID("UAV_Meta");
        internal static readonly int UAV_OcclusionTexture = Shader.PropertyToID("UAV_OcclusionTexture");
        internal static readonly int UAV_Output = Shader.PropertyToID("UAV_Output");
        internal static readonly int UAV_RayHit = Shader.PropertyToID("UAV_RayHit");
        internal static readonly int UAV_ScatterDiffuse = Shader.PropertyToID("UAV_ScatterDiffuse");
        internal static readonly int UAV_SceneColor = Shader.PropertyToID("UAV_SceneColor");
        internal static readonly int UAV_Temporal = Shader.PropertyToID("UAV_Temporal");
        internal static readonly int UAV_TileArgs = Shader.PropertyToID("UAV_TileArgs");
        internal static readonly int VolFog_AerialDistance = Shader.PropertyToID("VolFog_AerialDistance");
        internal static readonly int VolFog_MaxDistance = Shader.PropertyToID("VolFog_MaxDistance");
        internal static readonly int WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    }
}
