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

    albedo = lerp(albedo, dBufferA.rgb, dBufferA.a);
    float3 decalNormalWS = normalize(dBufferB.xyz * 2.0 - 1.0);
    normalWS = normalize(lerp(normalWS, decalNormalWS, dBufferB.a));
    roughness = lerp(roughness, dBufferC.r, dBufferC.a);
    reflectance = lerp(reflectance, dBufferC.g, dBufferC.a);
}

#endif
