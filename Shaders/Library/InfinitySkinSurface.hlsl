#ifndef INFINITY_SKIN_SURFACE
#define INFINITY_SKIN_SURFACE

#include "BSDF.hlsl"
#include "InfinitySkinMaterial.hlsl"

Texture2D _MainTex;
SamplerState sampler_MainTex;
Texture2D _NormalTexture;
SamplerState sampler_NormalTexture;
Texture2D _OcclusionTexture;
SamplerState sampler_OcclusionTexture;

struct SkinSurface
{
    float3 albedo;
    float3 normalWS;
    float occlusion;
};

// The material inputs every color pass of InfinitySkin reads. Skin is a dielectric: metallic is 0.
SkinSurface EvaluateSkinSurface(float2 uv, float3 normalWS, float3 tangentWS, float3 bitangentWS)
{
    SkinSurface surface;
    surface.albedo = _MainTex.Sample(sampler_MainTex, uv * _BaseColorTile).rgb * _BaseColor.rgb;
    float3 normalTS = UnpackNormal(_NormalTexture.Sample(sampler_NormalTexture, uv * _NormalTile));
    float3x3 tangentMatrix = float3x3(tangentWS, bitangentWS, normalize(normalWS));
    surface.normalWS = normalize(mul(normalTS, tangentMatrix));
    surface.occlusion = lerp(1, _OcclusionTexture.Sample(sampler_OcclusionTexture, uv * _BaseColorTile).r, _OcclusionStrength);
    return surface;
}

// The microfacet description of skin: the light loop's specular takes the dual-lobe Beckmann under INFINITY_SKIN_SPECULAR.
MicrofaceContext InitSkinMicrofaceContext(SkinSurface surface)
{
    MicrofaceContext microfaceContext = InitMicrofaceContext(_SpecularLevel, _Roughness, 0, surface.albedo);
    microfaceContext.SecondLobeMix = _SecondLobeMix;
    microfaceContext.SecondLobeRoughnessScale = _SecondLobeRoughnessScale;
    return microfaceContext;
}

// Subsurface profile written to the sideband: the material stores 0..14 and the sideband 1..15, 0 marking no profile.
uint SkinSidebandProfile()
{
    return (uint)_DiffusionProfileIndex + 1u;
}

#endif
