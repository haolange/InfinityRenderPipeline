#ifndef INFINITY_TEMPORAL_COLOR_SOURCE
#define INFINITY_TEMPORAL_COLOR_SOURCE
#include "TextureView.hlsl"
#include "ScreenSpaceReprojection.hlsl"
Texture2D SRV_TemporalColor;
Texture2D SRV_TemporalColorMips;

// The TemporalColorParameters record BindTemporalColorView binds as one constant buffer.
cbuffer CBV_TemporalColorParameters
{
    float4 TemporalColorResolution;
    int TemporalColorValid;
    int TemporalColorMipCount;
    int TemporalColorPadding0;
    int TemporalColorPadding1;
};

float4 SampleTemporalColorMip(float2 uv, uint level)
{
    float4 value = 0;
    if (level == 0)
    {
        value = InfinitySampleLevel(SRV_TemporalColor,Global_bilinear_clamp_sampler,uv,0);
        value = value.a >= 1 ? float4(value.rgb,1) : 0;
    }
    else value = InfinitySampleLevel(SRV_TemporalColorMips,Global_bilinear_clamp_sampler,uv,level-1);
    return value;
}
// The previous frame's color at a hit, found by reprojecting the hit with its motion. No depth is kept across frames:
// the hit is accepted when it reprojects on screen onto accepted temporal samples.
float3 SampleTemporalHitColor(float2 hitUV, float hitDepth, float2 motion, float mip,
    float4x4 inverseVP, float4x4 previousVP, float4x4 motionVP, float4x4 previousMotionVP, out float confidence)
{
    float3 color = 0;
    confidence = 0;
    if (TemporalColorValid != 0 && all(isfinite(motion)))
    {
        float2 previousUV;
        float previousDepth;
        ReprojectSurface(hitUV,hitDepth,motion,inverseVP,previousVP,motionVP,previousMotionVP,previousUV,previousDepth);
        // Unified temporal color is reconstructed on the unjittered display grid.
        float2 colorUV = previousUV - View_JitterUV.zw;
        if (all(colorUV >= 0) && all(colorUV < 1))
        {
            float lod = clamp(mip,0,TemporalColorMipCount-1);
            float4 value = lerp(SampleTemporalColorMip(colorUV,(uint)floor(lod)),
                SampleTemporalColorMip(colorUV,(uint)ceil(lod)),frac(lod));
            if (value.a > 0) color = value.rgb / value.a;
            confidence = saturate(value.a);
        }
    }
    return color;
}
#endif
