#ifndef _TranslucentCommonInclude
#define _TranslucentCommonInclude

#include "Common.hlsl"
#include "AtmosphereCommon.hlsl"

Texture3D<float4> _VolumetricFogTexture;
Texture2D<float4> _ColorPyramidTexture;
Texture2D<float> _TranslucentDepthTexture;
Texture3D<float4> _AtmosphereAerialPerspectiveLUT;

float VolFog_MaxDistance;
float VolFog_AerialDistance;

float4 SampleVolumetricFog(float2 screenUV, float linearDepth)
{
#if defined(_VOLUMETRIC_FOG)
    float slice = saturate(sqrt(linearDepth / max(VolFog_MaxDistance, 1.0)));
    return _VolumetricFogTexture.SampleLevel(Global_trilinear_clamp_sampler, float3(screenUV, slice), 0);
#else
    return float4(0, 0, 0, 1);
#endif
}

float3 ApplyAerialToSurface(float3 color, float2 screenUV, float linearDepth)
{
#if defined(_AERIAL_PERSPECTIVE)
    float4 aerial = SampleAtmosphereAerialLUT(_AtmosphereAerialPerspectiveLUT, screenUV, linearDepth, VolFog_AerialDistance);
    return color * aerial.a + aerial.rgb;
#else
    return color;
#endif
}

float4 ApplyT0Fog(float3 albedo, float alpha, float2 screenUV, float linearDepth)
{
    float4 fog = SampleVolumetricFog(screenUV, linearDepth);
    float3 color = albedo * fog.a + fog.rgb;
    color = ApplyAerialToSurface(color, screenUV, linearDepth);
    return float4(color, alpha);
}

float4 SampleRefractionPyramid(float2 screenUV, float3 normalWS, float refractionStrength, float roughness)
{
#if defined(_REFRACTION_PYRAMID)
    float2 offset = normalWS.xy * refractionStrength;
    float lod = roughness * 4.0;
    return _ColorPyramidTexture.SampleLevel(Global_bilinear_clamp_sampler, saturate(screenUV + offset), lod);
#else
    return 0;
#endif
}

float TranslucentReactive(float alpha)
{
    return saturate(alpha);
}

float2 TranslucentMotion(float4 clipPos, float4 clipPosOld)
{
    float2 hPos = clipPos.xy / max(clipPos.w, 1e-6);
    float2 hPosOld = clipPosOld.xy / max(clipPosOld.w, 1e-6);
    float2 ndcPos = (hPos + 1.0) * 0.5;
    float2 ndcPosOld = (hPosOld + 1.0) * 0.5;
    return ndcPos - ndcPosOld;
}

#endif
