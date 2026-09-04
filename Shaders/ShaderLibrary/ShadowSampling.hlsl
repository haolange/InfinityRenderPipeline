#ifndef _ShadowSamplingInclude
#define _ShadowSamplingInclude

#include "Common.hlsl"
#include "Lighting.hlsl"

float4 _CascadeShadowMapSize;
int _CascadeCount;
float4x4 _CascadeMatrices[4];
float4 _CascadeSplitDistances;
float4 _LocalShadowMapSize;

float SampleShadowPCF3x3(Texture2D<float> shadowMap, float2 uv, float compareZ, float2 texelSize, float4 clipRect)
{
    float2 minUV = clipRect.xy + texelSize;
    float2 maxUV = clipRect.xy + clipRect.zw - texelSize;
    float sum = 0;
    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            float2 sampleUV = clamp(uv + float2(x, y) * texelSize, minUV, maxUV);
            float depth = shadowMap.SampleLevel(Global_bilinear_clamp_sampler, sampleUV, 0);
#if UNITY_REVERSED_Z
            sum += compareZ >= depth ? 1.0 : 0.0;
#else
            sum += compareZ <= depth ? 1.0 : 0.0;
#endif
        }
    }
    return sum / 9.0;
}

float SampleCascadeShadow(Texture2D<float> cascadeShadowMap, float3 worldPos, float viewDepth)
{
    if (_CascadeCount <= 0)
    {
        return 1.0;
    }

    int cascadeIdx = 3;
    [unroll]
    for (int i = 0; i < 4; ++i)
    {
        if (viewDepth < _CascadeSplitDistances[i])
        {
            cascadeIdx = i;
            break;
        }
    }

    float4 shadowCoord = mul(_CascadeMatrices[cascadeIdx], float4(worldPos, 1.0));
    if (shadowCoord.w <= 0.0)
    {
        return 1.0;
    }
    shadowCoord.xyz /= shadowCoord.w;
    float2 localUV = shadowCoord.xy * 0.5 + 0.5;
    if (any(localUV < 0.0) || any(localUV > 1.0))
    {
        return 1.0;
    }
    int col = cascadeIdx % 2;
    int row = cascadeIdx / 2;
    float4 clipRect = float4(col * 0.5, row * 0.5, 0.5, 0.5);
    float2 shadowUV = localUV * 0.5 + clipRect.xy;
    return SampleShadowPCF3x3(cascadeShadowMap, shadowUV, shadowCoord.z, _CascadeShadowMapSize.zw, clipRect);
}

float SampleLocalShadow(Texture2D<float> localShadowMap, FLightRecord light, float3 worldPos)
{
    if ((light.flags & LIGHT_FLAG_SHADOW) == 0 || light.shadowMatrixIndex < 0 || light.shadowSliceCount <= 0)
    {
        return 1.0;
    }

    int face = 0;
    if (light.lightType == LIGHT_TYPE_POINT)
    {
        face = SelectPointShadowFace(worldPos - light.positionRange.xyz);
        face = min(face, light.shadowSliceCount - 1);
    }

    int slice = light.shadowMatrixIndex + face;
    float4x4 shadowMatrix = SRV_LocalShadowMatrices[slice];
    float4 clipRect = SRV_LocalShadowRects[slice];
    float4 shadowCoord = mul(shadowMatrix, float4(worldPos, 1.0));
    if (shadowCoord.w <= 0.0)
    {
        return 1.0;
    }
    shadowCoord.xyz /= shadowCoord.w;
    float2 localUV = shadowCoord.xy * 0.5 + 0.5;
    if (any(localUV < 0.0) || any(localUV > 1.0))
    {
        return 1.0;
    }
    float2 shadowUV = localUV * clipRect.zw + clipRect.xy;
#if UNITY_REVERSED_Z
    float compareZ = saturate(shadowCoord.z + 0.002);
#else
    float compareZ = saturate(shadowCoord.z - 0.002);
#endif
    return SampleShadowPCF3x3(localShadowMap, shadowUV, compareZ, _LocalShadowMapSize.zw, clipRect);
}

#endif
