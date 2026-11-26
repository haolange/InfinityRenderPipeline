#ifndef INFINITY_GBUFFER_OUTPUT_INCLUDED
#define INFINITY_GBUFFER_OUTPUT_INCLUDED

// Contiguous MRT slots for RasterGBufferPass (max 7 color + 1 depth = 8 NativeRenderPass cap).
// Base targets 0..3 are always bound; optional targets pack contiguously from SV_Target4.
struct GBufferFragmentOutput
{
    float4 gBufferA        : SV_Target0;
    float4 gBufferB        : SV_Target1;
    float4 surfaceMetadata : SV_Target2;
    float4 lightingBuffer  : SV_Target3;

#if defined(INFINITY_GBUFFER_BAKED_DIFFUSE)
    float4 bakedDiffuse    : SV_Target4;
    #if defined(SHADOWS_SHADOWMASK)
        float4 bakedShadowmask : SV_Target5;
        #if defined(INFINITY_GBUFFER_CUSTOM_DATA_SSS)
            float4 customData : SV_Target6;
        #endif
    #elif defined(INFINITY_GBUFFER_CUSTOM_DATA_SSS)
        float4 customData : SV_Target5;
    #endif
#elif defined(SHADOWS_SHADOWMASK)
    float4 bakedShadowmask : SV_Target4;
    #if defined(INFINITY_GBUFFER_CUSTOM_DATA_SSS)
        float4 customData : SV_Target5;
    #endif
#elif defined(INFINITY_GBUFFER_CUSTOM_DATA_SSS)
    float4 customData : SV_Target4;
#endif
};

#endif
