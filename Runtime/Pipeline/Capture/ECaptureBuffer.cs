#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace InfinityTech.Rendering.Pipeline
{
    // The buffers a capture request can name. The request keeps the names for its evidence; the session parses them once.
    [Flags]
    internal enum ECaptureBuffer : ulong
    {
        None = 0,
        SubsurfaceIrradiance = 1UL << 0,
        SSGIMeta = 1UL << 1,
        SSGIHitDistance = 1UL << 2,
        TemporalColorSource = 1UL << 3,
        TemporalColorMips = 1UL << 4,
        SRAccumulation = 1UL << 5,
        TAAInput = 1UL << 6,
        TAAHistoryColor = 1UL << 7,
        TAAAccumulation = 1UL << 8,
        TAAReprojection = 1UL << 9,
        ReactiveMask = 1UL << 10,
        AOTrace = 1UL << 11,
        AOSpatialX = 1UL << 12,
        AOSpatialY = 1UL << 13,
        AOTemporal = 1UL << 14,
        DisplayColor = 1UL << 15,
        PostProcess = 1UL << 16,
        Lighting = 1UL << 17,
        DeferredLighting = 1UL << 18,
        LightTileMask = 1UL << 19,
        Motion = 1UL << 20,
        Depth = 1UL << 21,
        DBufferA = 1UL << 22,
        DBufferB = 1UL << 23,
        DBufferC = 1UL << 24,
        GBufferA = 1UL << 25,
        GBufferB = 1UL << 26,
        Sideband = 1UL << 27,
        Occlusion = 1UL << 28,
        SSR = 1UL << 29,
        SSGI = 1UL << 30,
        SceneColor = 1UL << 31,
        AntiAliasing = 1UL << 32,
        ContactShadowSun = 1UL << 33,
        ContactShadowMask = 1UL << 34,
        CascadeShadow = 1UL << 35,
        LocalShadow = 1UL << 36,
        CachedShadow = 1UL << 37,
        CascadeShadowMoments = 1UL << 38,
        LocalShadowMoments = 1UL << 39,
        HiZClosest = 1UL << 40,
        HiZFurthest = 1UL << 41,
        SSRRayHit = 1UL << 42,
        SSRResolved = 1UL << 43,
        SSRTileClass = 1UL << 44,
        SSRMeta = 1UL << 45,
        SSRTemporal = 1UL << 46,
        SSGIRayHit = 1UL << 47,
        SSGITraceRadiance = 1UL << 48,
        SSGITemporal = 1UL << 49,
        HalfResDepth = 1UL << 50,
        HalfResNormal = 1UL << 51,
        VolumetricCloud = 1UL << 52
    }
}
#endif
