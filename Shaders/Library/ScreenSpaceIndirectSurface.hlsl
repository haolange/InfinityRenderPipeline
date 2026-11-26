#ifndef INFINITY_SCREEN_SPACE_INDIRECT_SURFACE
#define INFINITY_SCREEN_SPACE_INDIRECT_SURFACE

#include "ViewUniform.hlsl"

// SRV_DepthTexture, SRV_GBufferB and SSGI_Resolution are bound by the caller; the inverse matrices come from ViewUniform.
struct SSGISurface
{
    float rawDepth;
    float3 normalWS;
    float3 positionVS;
    float3 positionWS;
};

// Any opaque surface. Deferred and forward pixels both write the GBuffer surface proxy; a forward material decides how
// it combines the result.
bool IsIndirectSurface(uint2 pixel, out float rawDepth)
{
    rawDepth = SRV_DepthTexture[pixel];
    return !ScreenSpaceIsFarDepth(rawDepth);
}

bool LoadSurface(uint2 pixel, out SSGISurface surface)
{
    surface = (SSGISurface)0;
    float rawDepth = 0;
    bool valid = IsIndirectSurface(pixel, rawDepth);
    surface.rawDepth = rawDepth;
    if (valid)
    {
        surface.normalWS = DecodeGBufferNormal(SRV_GBufferB[pixel]);
        float2 uv = (pixel + 0.5) * SSGI_Resolution.zw;
        float3 ndc = GetNDCPos(uv, rawDepth);
        surface.positionVS = GetViewSpacePos(ndc, Matrix_InvFlipYJitterProj);
        surface.positionWS = GetWorldSpacePos(ndc, Matrix_InvViewFlipYJitterProj);
    }
    return valid;
}

#endif
