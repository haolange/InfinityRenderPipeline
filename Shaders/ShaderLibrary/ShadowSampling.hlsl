#ifndef _ShadowSamplingInclude
#define _ShadowSamplingInclude

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Common.hlsl"
#include "Lighting.hlsl"

float4 _CascadeShadowMapSize;
int _CascadeCount;
float4x4 _CascadeMatrices[4];
float4 _CascadeSplitDistances;
float4 _CascadeSpheres[4];
float4 _LocalShadowMapSize;

// Depth is a plane in projected texture space. Compare each tap at that tap's
// receiver depth, rather than comparing sloped neighboring texels to the center.
float2 ShadowReceiverGradient(float4x4 worldToShadow, float3 position, float3 normal, float2 atlasScale)
{
    if (dot(normal, normal) < 0.5) return 0;
    float3 tangent = normalize(cross(normal, abs(normal.z) < 0.9 ? float3(0, 0, 1) : float3(0, 1, 0)));
    float3 bitangent = cross(normal, tangent);
    float4 a = mul(worldToShadow, float4(position, 1));
    float4 b = mul(worldToShadow, float4(tangent, 0));
    float4 c = mul(worldToShadow, float4(bitangent, 0));
    float3 plane = cross(b.xyz * a.w - a.xyz * b.w, c.xyz * a.w - a.xyz * c.w);
    if (abs(plane.z) < 1e-8) return 0;
    float2 gradient = -plane.xy / plane.z * 2 / atlasScale;
#if UNITY_UV_STARTS_AT_TOP
    gradient.y = -gradient.y;
#endif
    return gradient;
}

float SampleShadowPCF3x3(Texture2D<float> shadowMap, float2 uv, float compareZ, float2 texelSize, float4 clipRect, int filterType = 1, float2 gradient = float2(0, 0))
{
    float2 minUV = clipRect.xy + texelSize;
    float2 maxUV = clipRect.xy + clipRect.zw - texelSize;
    if (filterType == 0)
    {
        float2 sampleUV = (floor(clamp(uv, minUV, maxUV) / texelSize) + 0.5) * texelSize;
        compareZ += dot(gradient, sampleUV - uv);
        float depth = shadowMap.SampleLevel(Global_point_clamp_sampler, sampleUV, 0);
#if UNITY_REVERSED_Z
        return compareZ >= depth ? 1 : 0;
#else
        return compareZ <= depth ? 1 : 0;
#endif
    }
    float sum = 0;
    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            float2 sampleUV = clamp(uv + float2(x, y) * texelSize, minUV, maxUV);
            sampleUV = (floor(sampleUV / texelSize) + 0.5) * texelSize;
            float tapZ = compareZ + dot(gradient, sampleUV - uv);
            float depth = shadowMap.SampleLevel(Global_point_clamp_sampler, sampleUV, 0);
#if UNITY_REVERSED_Z
            sum += tapZ >= depth ? 1.0 : 0.0;
#else
            sum += tapZ <= depth ? 1.0 : 0.0;
#endif
        }
    }
    return sum / 9.0;
}

float SampleCascadeShadow(Texture2D<float> cascadeShadowMap, float3 worldPos, float viewDepth, int filterType = 1, float3 normal = float3(0, 0, 0))
{
    if (_CascadeCount <= 0)
    {
        return 1.0;
    }

    int cascadeIdx = -1;
    [unroll]
    for (int i = 0; i < 4; ++i)
    {
        float3 delta = worldPos - _CascadeSpheres[i].xyz;
        if (_CascadeSpheres[i].w > 0 && dot(delta, delta) <= _CascadeSpheres[i].w)
        {
            cascadeIdx = i;
            break;
        }
    }
    if (cascadeIdx < 0 || viewDepth >= _CascadeSplitDistances.w) return 1;

    float4 shadowCoord = mul(_CascadeMatrices[cascadeIdx], float4(worldPos, 1.0));
    if (shadowCoord.w <= 0.0)
    {
        return 1.0;
    }
    shadowCoord.xyz /= shadowCoord.w;
    float2 localUV = shadowCoord.xy * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    localUV.y = 1 - localUV.y;
#endif
    if (any(localUV < 0.0) || any(localUV > 1.0))
    {
        return 1.0;
    }
    int col = cascadeIdx % 2;
    int row = cascadeIdx / 2;
    float4 clipRect = float4(col * 0.5, row * 0.5, 0.5, 0.5);
    float2 shadowUV = localUV * 0.5 + clipRect.xy;
    return SampleShadowPCF3x3(cascadeShadowMap, shadowUV, shadowCoord.z, _CascadeShadowMapSize.zw, clipRect, filterType, ShadowReceiverGradient(_CascadeMatrices[cascadeIdx], worldPos, normal, clipRect.zw));
}

float SampleLocalShadow(Texture2D<float> localShadowMap, FLightRecord light, float3 worldPos, float3 normal = float3(0, 0, 0))
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
#if UNITY_UV_STARTS_AT_TOP
    localUV.y = 1 - localUV.y;
#endif
    if (any(localUV < 0.0) || any(localUV > 1.0))
    {
        return 1.0;
    }
    float2 shadowUV = localUV * clipRect.zw + clipRect.xy;
    return SampleShadowPCF3x3(localShadowMap, shadowUV, shadowCoord.z, _LocalShadowMapSize.zw, clipRect, light.shadowType, ShadowReceiverGradient(shadowMatrix, worldPos, normal, clipRect.zw));
}

int _InfinityShadowmaskMode;
float _InfinityShadowDistance;

float MixBakedAndRealtimeShadow(FLightRecord light, float realtime, float4 mask, float viewDepth)
{
    float fade = saturate((viewDepth / max(_InfinityShadowDistance, 0.001) - 0.9) * 10);
    if ((light.flags & 8) == 0 || light.bakedOcclusionChannel < 0 || light.bakedOcclusionChannel > 3)
        return lerp(1, lerp(realtime, 1, fade), light.shape.z);
    float baked = mask[light.bakedOcclusionChannel];
    float combined = _InfinityShadowmaskMode == 0 ? min(baked, lerp(realtime, 1, fade)) : lerp(realtime, baked, fade);
    return lerp(1, combined, light.shape.z);
}

#endif
