#ifndef _DBufferInclude
#define _DBufferInclude

#include "Common.hlsl"

Texture2D _DBufferTextureA;
Texture2D _DBufferTextureB;
Texture2D _DBufferTextureC;

void ApplyDBuffer(float2 screenUV, inout float3 albedo, inout float3 normalWS, inout float roughness, inout float reflectance)
{
    float4 dBufferA = _DBufferTextureA.SampleLevel(Global_point_clamp_sampler, screenUV, 0);
    float4 dBufferB = _DBufferTextureB.SampleLevel(Global_point_clamp_sampler, screenUV, 0);
    float4 dBufferC = _DBufferTextureC.SampleLevel(Global_point_clamp_sampler, screenUV, 0);

    // The MRT blend has already premultiplied each attribute by accumulated coverage.
    albedo = albedo * (1.0 - dBufferA.a) + dBufferA.rgb;
    if (dBufferB.a > 0.0)
    {
        float3 decalNormalWS = normalize(dBufferB.xyz / dBufferB.a * 2.0 - 1.0);
        normalWS = normalize(lerp(normalWS, decalNormalWS, dBufferB.a));
    }
    roughness = roughness * (1.0 - dBufferC.a) + dBufferC.r;
    reflectance = reflectance * (1.0 - dBufferC.a) + dBufferC.g;
}

#endif
