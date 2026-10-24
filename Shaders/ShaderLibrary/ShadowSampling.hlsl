#ifndef _ShadowSamplingInclude
#define _ShadowSamplingInclude

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"
#include "ShadowMoments.hlsl"

// Per local shadow slice (C# FLocalShadowSliceGPU). depthParams: x, y = m22, m23 of the GPU projection; z = world
// texel size at unit depth (perspective) or world texel size (orthographic); w = 1 for orthographic.
// momentParams: x = 1 / far (moment depth normalization).
struct FLocalShadowSlice
{
    float4 atlasRect;
    float4 depthParams;
    float4 momentParams;
    float4 shadowOriginSize;
};

StructuredBuffer<float4x4> SRV_LocalShadowMatrices;
StructuredBuffer<FLocalShadowSlice> SRV_LocalShadowRects;

float4 _CascadeShadowMapSize;
int _CascadeCount;
float4x4 _CascadeMatrices[4];
float4 _CascadeSplitDistances;
float4 _CascadeSpheres[4];
// x = world texel size, y, z = m22, m23 of the GPU projection, w = blend width (fraction of the cascade radius).
float4 _CascadeShadowParams[4];
float4 _LocalShadowMapSize;
// x = PCF tent radius (texels), y = PCSS blocker samples, z = PCSS filter samples, w = PCSS max penumbra (texels).
float4 _ShadowFilterParams;
// x = EVSM exponent, y = light-bleeding reduction, z = minimum moment standard deviation.
float4 _ShadowMomentParams;

// Moment atlases (VSM / EVSM) at half the resolution of their depth atlas, same UV layout. Bound to a black texture
// when no visible light uses moments.
Texture2D<float4> SRV_CascadeShadowMoments;
Texture2D<float4> SRV_LocalShadowMoments;

// Depth is a plane in projected texture space. Compare each tap at that tap's
// receiver depth, rather than comparing sloped neighboring texels to the center.
float2 ShadowReceiverGradient(float4x4 worldToShadow, float3 position, float3 normal, float2 atlasScale)
{
    float2 gradient = 0;
    if (dot(normal, normal) >= 0.5)
    {
        float3 tangent = cross(normal, abs(normal.z) < 0.9 ? float3(0, 0, 1) : float3(0, 1, 0));
        float tangentLengthSquared = dot(tangent, tangent);
        // A missing surface normal (volumetric receivers) has no receiver plane.
        // Keep normalization defined even when the compiler flattens this branch.
        tangent *= rsqrt(tangentLengthSquared > 0 ? tangentLengthSquared : 1);
        float3 bitangent = cross(normal, tangent);
        float4 a = mul(worldToShadow, float4(position, 1));
        float4 b = mul(worldToShadow, float4(tangent, 0));
        float4 c = mul(worldToShadow, float4(bitangent, 0));
        float3 plane = cross(b.xyz * a.w - a.xyz * b.w, c.xyz * a.w - a.xyz * c.w);
        if (abs(plane.z) >= 1e-8)
        {
            gradient = -plane.xy / plane.z * 2 / atlasScale;
#if UNITY_UV_STARTS_AT_TOP
            gradient.y = -gradient.y;
#endif
        }
    }
    return gradient;
}

// The receiver is lit by a stored depth when it is not farther from the light.
float ShadowLit(float receiverZ, float storedZ)
{
#if UNITY_REVERSED_Z
    return receiverZ >= storedZ ? 1.0 : 0.0;
#else
    return receiverZ <= storedZ ? 1.0 : 0.0;
#endif
}

bool ShadowCloser(float storedZ, float receiverZ)
{
#if UNITY_REVERSED_Z
    return storedZ > receiverZ;
#else
    return storedZ < receiverZ;
#endif
}

float ShadowTexelWorld(float linearDepth, float4 depthParams)
{
    return depthParams.w != 0 ? depthParams.z : depthParams.z * linearDepth;
}

float SampleShadowHard(Texture2D<float> shadowMap, float2 uv, float compareZ, float2 texelSize, float4 clipRect, float2 gradient)
{
    float2 minUV = clipRect.xy + 0.5 * texelSize;
    float2 maxUV = clipRect.xy + clipRect.zw - 0.5 * texelSize;
    float2 sampleUV = (floor(clamp(uv, minUV, maxUV) / texelSize) + 0.5) * texelSize;
    return ShadowLit(compareZ + dot(gradient, sampleUV - uv), shadowMap.SampleLevel(Global_point_clamp_sampler, sampleUV, 0));
}

// Tent PCF from GatherRed: each gather fetches a 2x2 texel block, every texel is compared at the receiver plane depth
// of its own center and weighted by the tent of half-width radius + 0.5 texels. No comparison sampler is needed.
float SampleShadowTent(Texture2D<float> shadowMap, float2 uv, float compareZ, float2 texelSize, float4 clipRect, float radius, float2 gradient)
{
    radius = max(radius, 1.0);
    float halfWidth = radius + 0.5;
    float2 position = uv / texelSize - 0.5;
    int2 tileMin = (int2)round(clipRect.xy / texelSize);
    int2 tileMax = (int2)round((clipRect.xy + clipRect.zw) / texelSize) - 2;
    int2 first = (int2)floor(position - halfWidth);
    int2 last = (int2)ceil(position + halfWidth);
    float sum = 0;
    float weightSum = 0;
    [loop]
    for (int by = first.y; by <= last.y; by += 2)
    {
        [loop]
        for (int bx = first.x; bx <= last.x; bx += 2)
        {
            int2 block = clamp(int2(bx, by), tileMin, max(tileMin, tileMax));
            float4 depths = shadowMap.GatherRed(Global_point_clamp_sampler, (float2(block) + 1.0) * texelSize);
            // Gather order: w = (x, y), z = (x + 1, y), x = (x, y + 1), y = (x + 1, y + 1).
            float2 texels[4] = { float2(block) + float2(0, 1), float2(block) + float2(1, 1), float2(block) + float2(1, 0), float2(block) };
            [unroll]
            for (int i = 0; i < 4; ++i)
            {
                float2 weight2 = max(0.0, halfWidth - abs(texels[i] - position));
                float weight = weight2.x * weight2.y;
                float2 tapUV = (texels[i] + 0.5) * texelSize;
                sum += weight * ShadowLit(compareZ + dot(gradient, tapUV - uv), depths[i]);
                weightSum += weight;
            }
        }
    }
    return weightSum > 0 ? sum / weightSum : 1.0;
}

float2 VogelDiskSample(uint index, uint count, float rotation)
{
    float radius = sqrt((index + 0.5) / count);
    float theta = index * 2.39996323 + rotation;
    return radius * float2(cos(theta), sin(theta));
}

// Percentage-closer soft shadows (Fernando 2005). lightSize is the source radius in world units for local lights or
// tan(angular radius) for directional lights; depths are linear distances along the light.
float SampleShadowPCSS(Texture2D<float> shadowMap, float2 uv, float compareZ, float2 texelSize, float4 clipRect, float2 gradient,
    float4 depthParams, float lightSize, bool directional)
{
    uint blockerSamples = (uint)max(_ShadowFilterParams.y, 4);
    uint filterSamples = (uint)max(_ShadowFilterParams.z, 4);
    float maxTexels = max(_ShadowFilterParams.w, 2);
    float minTexels = max(_ShadowFilterParams.x, 1);
    float2 minUV = clipRect.xy + texelSize;
    float2 maxUV = clipRect.xy + clipRect.zw - texelSize;
    float receiverDepth = ShadowLinearDepth(compareZ, depthParams);
    float texelWorld = max(ShadowTexelWorld(receiverDepth, depthParams), 1e-6);
    float rotation = InterleavedGradientNoise(uv / texelSize, 0) * 6.2831853;

    // Blocker search: where a blocker between the receiver and the light could hide part of the source.
    float searchWorld = lightSize * receiverDepth;
    if (!directional)
    {
#if UNITY_REVERSED_Z
        float nearDepth = ShadowLinearDepth(1.0, depthParams);
#else
        float nearDepth = ShadowLinearDepth(0.0, depthParams);
#endif
        // A blocker can be anywhere from the near plane to the receiver. Project the emitter's largest
        // possible footprint there; a receiver-plane radius alone misses blockers on the lit penumbra side.
        nearDepth = max(nearDepth, 1e-4);
        searchWorld = lightSize * max(receiverDepth - nearDepth, 0) / nearDepth;
    }
    float searchTexels = clamp(searchWorld / texelWorld, minTexels, maxTexels);
    float blockerDepth = 0;
    float blockerCount = 0;
    [loop]
    for (uint i = 0; i < blockerSamples; ++i)
    {
        float2 tapUV = clamp(uv + VogelDiskSample(i, blockerSamples, rotation) * searchTexels * texelSize, minUV, maxUV);
        // Point sampling reads the texel center, so the receiver-plane correction must use that same position.
        tapUV = (floor(tapUV / texelSize) + 0.5) * texelSize;
        float stored = shadowMap.SampleLevel(Global_point_clamp_sampler, tapUV, 0);
        if (ShadowCloser(stored, compareZ + dot(gradient, tapUV - uv)))
        {
            blockerDepth += ShadowLinearDepth(stored, depthParams);
            blockerCount += 1;
        }
    }
    float shadow = 1.0;
    if (blockerCount != 0)
    {
        blockerDepth /= blockerCount;

        // Penumbra width on the receiver, converted to shadow-map texels.
        float penumbraWorld = directional
        ? 2.0 * lightSize * max(receiverDepth - blockerDepth, 0)
        : 2.0 * lightSize * max(receiverDepth - blockerDepth, 0) / max(blockerDepth, 1e-4);
        float filterTexels = clamp(0.5 * penumbraWorld / texelWorld, minTexels, maxTexels);
        float lit = 0;
        [loop]
        for (uint j = 0; j < filterSamples; ++j)
        {
            float2 tapUV = clamp(uv + VogelDiskSample(j, filterSamples, rotation) * filterTexels * texelSize, minUV, maxUV);
            tapUV = (floor(tapUV / texelSize) + 0.5) * texelSize;
            lit += ShadowLit(compareZ + dot(gradient, tapUV - uv), shadowMap.SampleLevel(Global_point_clamp_sampler, tapUV, 0));
        }
        shadow = lit / filterSamples;
    }
    return shadow;
}

// Size of the light for PCSS: world radius for local lights, tan(angular radius) for the sun.
float ShadowLightSize(FLightRecord light)
{
    float size = 0;
    if (IsDirectionalLight(light))
    {
        size = light.shape.w + light.misc.y;
    }
    else if (IsRectLight(light))
    {
        size = sqrt(light.area.x * light.area.y / 3.14159265);
    }
    else size = light.shape.x + light.shape.y;
    return size;
}

// Bilinear moments inside the slice; the moment atlas has half the texel density of the depth atlas.
float SampleShadowMoments(Texture2D<float4> momentMap, int mode, float2 uv, float compareZ, float2 texelSize, float4 clipRect, float4 depthParams, float invFar)
{
    float2 momentTexel = 2.0 * texelSize;
    float2 sampleUV = clamp(uv, clipRect.xy + 0.5 * momentTexel, clipRect.xy + clipRect.zw - 0.5 * momentTexel);
    float4 moments = momentMap.SampleLevel(Global_bilinear_clamp_sampler, sampleUV, 0);
    return ShadowMomentVisibility(moments, ShadowMomentDepth(compareZ, depthParams, invFar), mode, _ShadowMomentParams);
}

float FilterShadow(Texture2D<float> shadowMap, Texture2D<float4> momentMap, FLightRecord light, float2 uv, float compareZ, float2 texelSize, float4 clipRect, float2 gradient, float4 depthParams, float invFar, float lightSize)
{
    float shadow = 1;
    if (light.shadowFilter == SHADOW_FILTER_VSM || light.shadowFilter == SHADOW_FILTER_EVSM)
    {
        int mode = light.shadowFilter == SHADOW_FILTER_EVSM ? SHADOW_MOMENT_EVSM : SHADOW_MOMENT_VSM;
        shadow = SampleShadowMoments(momentMap, mode, uv, compareZ, texelSize, clipRect, depthParams, invFar);
    }
    else if (light.shadowFilter == SHADOW_FILTER_HARD)
    {
        shadow = SampleShadowHard(shadowMap, uv, compareZ, texelSize, clipRect, gradient);
    }
    else if (light.shadowFilter == SHADOW_FILTER_PCSS && lightSize > 0)
    {
        shadow = SampleShadowPCSS(shadowMap, uv, compareZ, texelSize, clipRect, gradient, depthParams, lightSize, IsDirectionalLight(light));
    }
    else
    {
        shadow = SampleShadowTent(shadowMap, uv, compareZ, texelSize, clipRect, _ShadowFilterParams.x, gradient);
    }
    return shadow;
}

float SampleCascadeSlice(Texture2D<float> cascadeShadowMap, FLightRecord light, int cascadeIdx, float3 worldPos, float3 normal)
{
    float4 shadowCoord = mul(_CascadeMatrices[cascadeIdx], float4(worldPos, 1.0));
    float shadow = 1;
    if (shadowCoord.w > 0.0)
    {
        shadowCoord.xyz /= shadowCoord.w;
        float2 localUV = shadowCoord.xy * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
        localUV.y = 1 - localUV.y;
#endif
        if (all(localUV >= 0.0) && all(localUV <= 1.0))
        {
            uint slice = (uint)cascadeIdx;
            uint col = slice & 1u;
            uint row = slice >> 1;
            float4 clipRect = float4(col * 0.5, row * 0.5, 0.5, 0.5);
            float2 shadowUV = localUV * 0.5 + clipRect.xy;
            float4 params = _CascadeShadowParams[cascadeIdx];
            float4 depthParams = float4(params.y, params.z, params.x, 1);
            shadow = FilterShadow(cascadeShadowMap, SRV_CascadeShadowMoments, light, shadowUV, shadowCoord.z, _CascadeShadowMapSize.zw, clipRect,
                ShadowReceiverGradient(_CascadeMatrices[cascadeIdx], worldPos, normal, clipRect.zw), depthParams, 1.0, ShadowLightSize(light));
        }
    }
    return shadow;
}

// Picks the first cascade whose culling sphere holds the point and blends into the next one over the outer
// blend-width fraction of the sphere radius.
float SampleCascadeShadow(Texture2D<float> cascadeShadowMap, FLightRecord light, float3 worldPos, float viewDepth, float3 normal = float3(0, 0, 0))
{
    float shadow = 1;
    if (_CascadeCount > 0)
    {
        int cascadeIdx = -1;
        float radiusFraction = 0;
        [unroll]
        for (int i = 0; i < 4; ++i)
        {
            float3 delta = worldPos - _CascadeSpheres[i].xyz;
            float distanceSqr = dot(delta, delta);
            if (cascadeIdx < 0 && _CascadeSpheres[i].w > 0 && distanceSqr <= _CascadeSpheres[i].w)
            {
                cascadeIdx = i;
                radiusFraction = sqrt(distanceSqr / _CascadeSpheres[i].w);
            }
        }
        if (cascadeIdx >= 0 && viewDepth < _CascadeSplitDistances.w)
        {
            shadow = SampleCascadeSlice(cascadeShadowMap, light, cascadeIdx, worldPos, normal);
            float blendWidth = _CascadeShadowParams[cascadeIdx].w;
            int next = cascadeIdx + 1;
            if (blendWidth > 0 && next < 4 && _CascadeSpheres[next].w > 0)
            {
                float blend = saturate((radiusFraction - (1.0 - blendWidth)) / blendWidth);
                if (blend > 0)
                {
                    shadow = lerp(shadow, SampleCascadeSlice(cascadeShadowMap, light, next, worldPos, normal), blend);
                }
            }
        }
    }
    return shadow;
}

float SampleLocalShadow(Texture2D<float> localShadowMap, FLightRecord light, float3 worldPos, float3 normal = float3(0, 0, 0))
{
    float shadow = 1;
    if ((light.flags & LIGHT_FLAG_SHADOW) != 0 && light.shadowMatrixIndex >= 0 && light.shadowSliceCount > 0)
    {
        int face = 0;
        if (light.shadowSliceCount == 6)
        {
            face = SelectPointShadowFace(worldPos - SRV_LocalShadowRects[light.shadowMatrixIndex].shadowOriginSize.xyz);
        }

        int slice = light.shadowMatrixIndex + face;
        float4x4 shadowMatrix = SRV_LocalShadowMatrices[slice];
        FLocalShadowSlice sliceData = SRV_LocalShadowRects[slice];
        float4 clipRect = sliceData.atlasRect;
        float4 shadowCoord = mul(shadowMatrix, float4(worldPos, 1.0));
        if (shadowCoord.w > 0.0)
        {
            shadowCoord.xyz /= shadowCoord.w;
            float2 localUV = shadowCoord.xy * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
            localUV.y = 1 - localUV.y;
#endif
            if (all(localUV >= 0.0) && all(localUV <= 1.0))
            {
                float2 shadowUV = localUV * clipRect.zw + clipRect.xy;
                shadow = FilterShadow(localShadowMap, SRV_LocalShadowMoments, light, shadowUV, shadowCoord.z, _LocalShadowMapSize.zw, clipRect,
                    ShadowReceiverGradient(shadowMatrix, worldPos, normal, clipRect.zw), sliceData.depthParams, sliceData.momentParams.x, sliceData.shadowOriginSize.w);
            }
        }
    }
    return shadow;
}

int _InfinityShadowmaskMode;
float _InfinityShadowDistance;

float MixBakedAndRealtimeShadow(FLightRecord light, float realtime, float4 mask, float viewDepth)
{
    float fade = saturate((viewDepth / max(_InfinityShadowDistance, 0.001) - 0.9) * 10);
    float combined = lerp(realtime, 1, fade);
    if (LightHasFlag(light, LIGHT_FLAG_SHADOWMASK) && light.bakedOcclusionChannel >= 0 && light.bakedOcclusionChannel <= 3)
    {
        float baked = mask[light.bakedOcclusionChannel];
        combined = _InfinityShadowmaskMode == 0 ? min(baked, lerp(realtime, 1, fade)) : lerp(realtime, baked, fade);
    }
    return lerp(1, combined, LightShadowStrength(light));
}

#endif
