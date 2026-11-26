#ifndef INFINITY_GBUFFER_OUTPUT_INCLUDED
#define INFINITY_GBUFFER_OUTPUT_INCLUDED

// RasterGBufferPass targets: 128 bit of color at most. The sideband is bound only when the camera allocated it.
struct GBufferFragmentOutput
{
    float4 gBufferA   : SV_Target0;
    float4 gBufferB   : SV_Target1;
    float3 sceneColor : SV_Target2;
#if defined(INFINITY_SIDEBAND)
    float4 sideband   : SV_Target3;
#endif
};

#endif
