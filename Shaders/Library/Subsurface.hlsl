// Subsurface

#ifndef INFINITY_SUBSURFACE_INCLUDED
#define INFINITY_SUBSURFACE_INCLUDED

#include "Common.hlsl"
#include "GBufferPack.hlsl"
#include "Sideband.hlsl"
#include "Random.hlsl"
#include "Montcalo.hlsl"
#include "ViewUniform.hlsl"

// Screen-space Burley diffusion of skin. Only pixels with the subsurface stencil bit run; the sideband's low nibble names
// their profile (1..15) and its R the diffuse share of Scene Color. RasterSubsurfaceExtract writes D = SceneColor · R to
// the irradiance target; RasterSubsurfaceScatter blurs D within one profile and blends Out = t·blur(D) + Dst·(1 − t·R),
// so the diffuse it removes is exactly the diffuse it blurred. Specular and emission stay where they are.

// C# DiffusionProfileRecord (Runtime/Lighting/DiffusionProfile.cs, GPU-RECORD marker).
struct DiffusionProfileRecord
{
    // rgb: scatter albedo, w: scatter distance in world units.
    float4 scatterAlbedoDistance;
    // x: maximum radius in scatter distances, y: thickness (the share of the diffuse that scatters), zw: unused.
    float4 parameters;
};

StructuredBuffer<DiffusionProfileRecord> SRV_DiffusionProfiles;
Texture2D<float> SRV_DepthTexture;
Texture2D SRV_GBufferTextureB;
Texture2D<float4> SRV_Sideband;
Texture2D<float4> SRV_LightingTexture;
Texture2D<float4> SRV_SubsurfaceIrradiance;
// xy: target size, zw: its reciprocal.
float4 _SubsurfaceResolution;
// x: sample count, y: profile count.
float4 _SubsurfaceSampling;

// Burley normalized diffusion R(r) = s / (8π r) · (exp(−s r) + exp(−s r / 3)), s = 1 / scatter distance.
float3 EvaluateBurleyDiffusionProfile(float r, float3 scatteringDistance)
{
    float3 s = 1.0 / max(scatteringDistance, 0.0001);
    return s * (exp(-s * r) + exp(-s * r / 3.0)) / (8.0 * Pi * max(r, 0.0001));
}

// Importance-samples the heavier exponential tail (rate s / 3) of the Burley profile.
float SampleBurleyRadius(float u, float scatterDistance)
{
    return -log(max(1.0 - u, 1e-6)) * 3.0 * max(scatterDistance, 0.0001);
}

float SubsurfaceEyeDepth(float depth)
{
    float4 position = mul(Matrix_InvFlipYJitterProj, float4(0, 0, depth, 1));
    return abs(position.z / position.w);
}

struct SubsurfaceTap
{
    float3 normal;
    float eyeDepth;
    uint profile;
};

SubsurfaceTap LoadSubsurfaceTap(uint2 pixel)
{
    SubsurfaceTap tap;
    tap.profile = UnpackSideband(SRV_Sideband[pixel]).Profile;
    tap.normal = DecodeGBufferNormal(SRV_GBufferTextureB[pixel]);
    tap.eyeDepth = SubsurfaceEyeDepth(SRV_DepthTexture[pixel]);
    return tap;
}

// The diffuse a subsurface pixel holds: Scene Color scaled by its diffuse share.
float3 ExtractSubsurfaceIrradiance(uint2 pixel)
{
    return SRV_LightingTexture[pixel].rgb * UnpackSideband(SRV_Sideband[pixel]).DiffuseRatio;
}

// Source color and alpha for Blend One SrcAlpha over Scene Color. (0, 1) keeps the pixel: a profile outside the asset's list,
// an empty slot, zero thickness or a footprint smaller than a pixel scatters nothing.
float4 ScatterSubsurface(uint2 pixel)
{
    SidebandData sideband = UnpackSideband(SRV_Sideband[pixel]);
    if (sideband.Profile == 0 || sideband.Profile > (uint)_SubsurfaceSampling.y)
    {
        return float4(0, 0, 0, 1);
    }
    DiffusionProfileRecord profile = SRV_DiffusionProfiles[sideband.Profile - 1];
    float3 albedo = profile.scatterAlbedoDistance.rgb;
    float distance = profile.scatterAlbedoDistance.w;
    float worldRadius = profile.parameters.x * distance;
    float thickness = saturate(profile.parameters.y);
    SubsurfaceTap center = LoadSubsurfaceTap(pixel);
    // Perspective projection divides by eye distance; orthographic projection has w = 1.
    float projectionW = abs(Matrix_FlipYJitterProj[3][3]) > 0.5 ? 1 : center.eyeDepth;
    float2 projectedScale = abs(float2(Matrix_FlipYJitterProj[0][0], Matrix_FlipYJitterProj[1][1])) * 0.5 / max(projectionW, 1e-5);
    if (thickness <= 0 || worldRadius * projectedScale.x * _SubsurfaceResolution.x < 1)
    {
        return float4(0, 0, 0, 1);
    }

    float2 uv = (pixel + 0.5) * _SubsurfaceResolution.zw;
    uint2 random = Rand3DPCG16(int3(pixel, 0)).xy;
    uint sampleCount = (uint)_SubsurfaceSampling.x;
    float3 sum = SRV_SubsurfaceIrradiance[pixel].rgb;
    float3 weights = 1;
    [loop] for (uint i = 0; i < sampleCount; ++i)
    {
        float2 sampleValue = Hammersley16(i, sampleCount, random);
        float r = SampleBurleyRadius(sampleValue.x, distance);
        if (r > worldRadius)
        {
            continue;
        }
        float angle = sampleValue.y * Two_Pi;
        float2 tapUV = uv + float2(cos(angle), sin(angle)) * r * projectedScale;
        if (any(tapUV < 0) || any(tapUV >= 1))
        {
            continue;
        }
        uint2 tapPixel = min((uint2)(tapUV * _SubsurfaceResolution.xy), (uint2)_SubsurfaceResolution.xy - 1);
        SubsurfaceTap tap = LoadSubsurfaceTap(tapPixel);
        if (tap.profile != sideband.Profile)
        {
            continue;
        }
        float normalWeight = saturate(dot(center.normal, tap.normal));
        if (normalWeight < 0.9 || abs(tap.eyeDepth - center.eyeDepth) > max(worldRadius, 1e-5))
        {
            continue;
        }
        // Compensate the sampled exponential radial PDF, including the polar Jacobian.
        float pdf = exp(-r / max(3 * distance, 1e-5)) / max(3 * distance, 1e-5);
        float3 weight = EvaluateBurleyDiffusionProfile(r, distance * albedo) * r / max(pdf, 1e-10) * normalWeight;
        sum += SRV_SubsurfaceIrradiance[tapPixel].rgb * weight;
        weights += weight;
    }
    return float4(thickness * sum / weights, 1 - thickness * sideband.DiffuseRatio);
}

#endif
