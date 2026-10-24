#ifndef INFINITY_TEMPORAL_COLOR_SOURCE
#define INFINITY_TEMPORAL_COLOR_SOURCE
#include "TextureView.hlsl"
#include "ScreenSpaceReprojection.hlsl"
Texture2D SRV_TemporalColor;
Texture2D SRV_TemporalColorMips;
Texture2D SRV_TemporalDepth;
int TemporalColorValid, TemporalColorMipCount;
float4 TemporalColorResolution, TemporalColorJitterDelta;

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
float3 SampleTemporalHitColor(float2 hitUV, float hitDepth, float2 motion, float4 metadata, float mip,
    float4x4 inverseVP, float4x4 previousVP, float4x4 motionVP, float4x4 previousMotionVP, out float confidence)
{
    float3 color = 0;
    confidence = 0;
    if (TemporalColorValid != 0 && metadata.z > 0 && isfinite(metadata.x) && metadata.x >= 0 && metadata.x <= 1)
    {
        float2 depthUV;
        float expectedDepth;
        ReprojectSurface(hitUV,hitDepth,motion,inverseVP,previousVP,motionVP,previousMotionVP,depthUV,expectedDepth);
        // Motion metadata carries the actual previous object surface, including moving meshes.
        expectedDepth = metadata.x;
        float2 colorUV = depthUV - TemporalColorJitterDelta.xy;
        if (all(colorUV >= 0) && all(colorUV < 1)
            && HistoryDepthConfidence(SRV_TemporalDepth,SRV_TemporalDepth_InfinityView,depthUV,expectedDepth,0) > 0)
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
