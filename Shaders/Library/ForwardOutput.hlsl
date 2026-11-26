#ifndef INFINITY_FORWARD_OUTPUT_INCLUDED
#define INFINITY_FORWARD_OUTPUT_INCLUDED

// Contiguous MRT slots for RasterForwardPass.
// SV_Target0 (LightingBuffer) is always bound; IndirectDiffuse, IndirectSpecular and ScatterDiffuse pack contiguously.
struct ForwardFragmentOutput
{
    float4 lightingBuffer : SV_Target0;

#if defined(INFINITY_FORWARD_INDIRECT_DIFFUSE)
    float4 indirectDiffuse : SV_Target1;
    #if defined(INFINITY_FORWARD_INDIRECT_SPECULAR)
        float4 indirectSpecular : SV_Target2;
        #if defined(INFINITY_SSS_OUTPUT)
            float4 scatterDiffuse : SV_Target3;
        #endif
    #elif defined(INFINITY_SSS_OUTPUT)
        float4 scatterDiffuse : SV_Target2;
    #endif
#elif defined(INFINITY_FORWARD_INDIRECT_SPECULAR)
    float4 indirectSpecular : SV_Target1;
    #if defined(INFINITY_SSS_OUTPUT)
        float4 scatterDiffuse : SV_Target2;
    #endif
#elif defined(INFINITY_SSS_OUTPUT)
    float4 scatterDiffuse : SV_Target1;
#endif
};

#endif
