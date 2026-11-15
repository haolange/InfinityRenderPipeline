#ifndef INFINITY_SCREEN_SPACE_INDIRECT_SURFACE
#define INFINITY_SCREEN_SPACE_INDIRECT_SURFACE

#include "ViewUniform.hlsl"

// SRV_DepthTexture, SRV_GBufferB/C and SSGI_Resolution are bound by the caller; the inverse matrices come from ViewUniform.
struct SSGISurface
{
    float rawDepth;
    float3 normalWS;
    float3 positionVS;
    float3 positionWS;
};

// Deferred and valid. Forward-route pixels keep their own indirect diffuse.
bool IsIndirectSurface(uint2 pixel, out float rawDepth)
{
    rawDepth = SRV_DepthTexture[pixel];
    bool valid = false;
    if (!ScreenSpaceIsFarDepth(rawDepth))
    {
        uint shadingModel, flags;
        UnpackGBufferCChannelR(SRV_GBufferC[pixel].r, shadingModel, flags);
        valid = (flags & GBUFFER_FLAG_VALID_SURFACE) != 0 && (flags & GBUFFER_FLAG_FORWARD) == 0;
    }
    return valid;
}

bool LoadSurface(uint2 pixel, out SSGISurface surface)
{
    surface = (SSGISurface)0;
    float rawDepth = 0;
    bool valid = IsIndirectSurface(pixel, rawDepth);
    surface.rawDepth = rawDepth;
    if (valid)
    {
        surface.normalWS = normalize(SRV_GBufferB[pixel].xyz * 2 - 1);
        float2 uv = (pixel + 0.5) * SSGI_Resolution.zw;
        float3 ndc = GetNDCPos(uv, rawDepth);
        surface.positionVS = GetViewSpacePos(ndc, Matrix_InvFlipYJitterProj);
        surface.positionWS = GetWorldSpacePos(ndc, Matrix_InvViewFlipYJitterProj);
    }
    return valid;
}

#endif
