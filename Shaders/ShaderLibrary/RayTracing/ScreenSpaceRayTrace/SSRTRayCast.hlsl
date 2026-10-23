#ifndef INFINITY_SCREEN_SPACE_RAY_TRACE
#define INFINITY_SCREEN_SPACE_RAY_TRACE
#include "../../ScreenSpaceDepth.hlsl"
#include "../../Common.hlsl"
#include "../../Random.hlsl"
#include "../../Montcalo.hlsl"

float GetScreenFadeBord(float2 uv, float width)
{
    return saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) / max(width, 1e-5));
}

// Trace one projected segment through the closest-depth pyramid. Coarse cells
// can reject a ray, but only mip zero can produce a hit. UV and depth always
// refer to the same surface texel, never a component-wise aggregate.
bool TraceScreenSpaceRay(float3 originVS, float3 directionVS, float maxDistance, float thickness,
    uint budget, float jitter, float4x4 projection, Texture2D depthPyramid, float4 textureView, out float3 hit)
{
    hit = 0;
    float distance = maxDistance;
    if (directionVS.z > 0) distance = min(distance, max(0, (-ScreenSpaceDepthParams.x - originVS.z) / directionVS.z) * 0.99);
    if (distance <= 0) return false;
    float4 startClip = mul(projection, float4(originVS, 1));
    float4 endClip = mul(projection, float4(originVS + directionVS * distance, 1));
    if (startClip.w <= 0 || endClip.w <= 0) return false;
    float3 start = startClip.xyz / startClip.w;
    float3 end = endClip.xyz / endClip.w;
    start.xy = start.xy * 0.5 + 0.5;
    end.xy = end.xy * 0.5 + 0.5;
    float3 delta = end - start;
    uint width, height, mipCount;
    depthPyramid.GetDimensions(0, width, height, mipCount);
    if (textureView.x > 0) { width = textureView.x; height = textureView.y; mipCount = 1 + floor(log2(max(width, height))); }
    float2 fullSize = float2(width, height);
    float pixels = max(abs(delta.x) * width, abs(delta.y) * height);
    if (pixels < 1) return false;
    float t = (1.01 + saturate(jitter)) / pixels;
    int level = min(2, (int)mipCount - 1);
    [loop]
    for (uint iteration = 0; iteration < budget * 4 && t < 1; ++iteration)
    {
        float3 ray = start + delta * t;
        if (any(ray.xy < 0) || any(ray.xy >= 1) || ray.z < 0 || ray.z > 1) return false;
        uint2 mipSize = max(uint2(1, 1), uint2(width, height) >> level);
        float scale = (float)(1u << level);
        uint2 cell = min((uint2)(ray.xy * fullSize / scale), mipSize - 1);
        float2 lower = cell * scale / fullSize;
        float2 upper = min((cell + 1) * scale / fullSize, 1);
        if (cell.x == mipSize.x - 1) upper.x = 1;
        if (cell.y == mipSize.y - 1) upper.y = 1;
        float2 boundary = float2(delta.x >= 0 ? upper.x : lower.x, delta.y >= 0 ? upper.y : lower.y);
        float tx = abs(delta.x) > 1e-8 ? (boundary.x - ray.x) / delta.x : 1e20;
        float ty = abs(delta.y) > 1e-8 ? (boundary.y - ray.y) / delta.y : 1e20;
        float nextT = min(1, t + max(1e-7, min(tx, ty)));
        float surface = depthPyramid.Load(int3(cell, level)).r;
        float endDepth = start.z + delta.z * nextT;
#if UNITY_REVERSED_Z
        bool inFront = min(ray.z, endDepth) > surface;
        bool sky = surface <= 1e-7;
#else
        bool inFront = max(ray.z, endDepth) < surface;
        bool sky = surface >= 1 - 1e-7;
#endif
        if (!inFront && !sky && level > 0) { level--; continue; }
        if (!inFront && !sky)
        {
            float surfaceEye = ScreenSpaceLinearEyeDepth(surface);
            float nearEye = min(ScreenSpaceLinearEyeDepth(ray.z), ScreenSpaceLinearEyeDepth(endDepth));
            float farEye = max(ScreenSpaceLinearEyeDepth(ray.z), ScreenSpaceLinearEyeDepth(endDepth));
            if (farEye >= surfaceEye && nearEye <= surfaceEye + thickness)
            {
                // Depth is stored at the traversed texel center. Reconstructing it at a
                // subpixel cell boundary would combine two different surface locations.
                hit = float3((float2(cell) + 0.5) / fullSize, surface);
                return true;
            }
        }
        t = nextT + 1e-3 / pixels;
        level = min(level + 1, (int)mipCount - 1);
    }
    return false;
}
// Linear march for rough surfaces: uniform steps over the screen-clipped projected segment against one level of the
// closest-depth pyramid, then a secant refinement between the last two samples. Cheaper than the Hi-Z walk and less
// precise; the lobe noise of rough surfaces hides the step error. The hit is snapped to a mip-zero texel center so
// UV and depth always describe the same surface texel, as in TraceScreenSpaceRay.
bool TraceScreenSpaceRayLinear(float3 originVS, float3 directionVS, float maxDistance, float thickness,
    uint steps, float jitter, float4x4 projection, Texture2D depthPyramid, float4 textureView, uint level, out float3 hit)
{
    hit = 0;
    float distance = maxDistance;
    if (directionVS.z > 0) distance = min(distance, max(0, (-ScreenSpaceDepthParams.x - originVS.z) / directionVS.z) * 0.99);
    if (distance <= 0) return false;
    float4 startClip = mul(projection, float4(originVS, 1));
    float4 endClip = mul(projection, float4(originVS + directionVS * distance, 1));
    if (startClip.w <= 0 || endClip.w <= 0) return false;
    float3 start = startClip.xyz / startClip.w;
    float3 end = endClip.xyz / endClip.w;
    start.xy = start.xy * 0.5 + 0.5;
    end.xy = end.xy * 0.5 + 0.5;
    float3 delta = end - start;

    // Clip the segment to the screen so no step is spent outside it.
    float exitT = 1;
    if (delta.x > 1e-8) exitT = min(exitT, (1 - start.x) / delta.x);
    else if (delta.x < -1e-8) exitT = min(exitT, -start.x / delta.x);
    if (delta.y > 1e-8) exitT = min(exitT, (1 - start.y) / delta.y);
    else if (delta.y < -1e-8) exitT = min(exitT, -start.y / delta.y);

    uint width, height, mipCount;
    depthPyramid.GetDimensions(0, width, height, mipCount);
    if (textureView.x > 0) { width = textureView.x; height = textureView.y; mipCount = 1 + floor(log2(max(width, height))); }
    level = min(level, mipCount - 1);
    uint2 mipSize = max(uint2(1, 1), uint2(width, height) >> level);
    float pixels = max(abs(delta.x) * width, abs(delta.y) * height) * exitT;
    if (pixels < 1) return false;

    steps = max(steps, 1u);
    float pixelT = exitT / pixels;
    float stepT = exitT / steps;
    float t = pixelT * 1.01 + stepT * saturate(jitter);
    float previousT = 0;
    float previousDelta = -1;
    [loop]
    for (uint i = 0; i < steps && t <= exitT; ++i)
    {
        float3 ray = start + delta * t;
        if (ray.z < 0 || ray.z > 1) return false;
        uint2 cell = min((uint2)(ray.xy * float2(mipSize)), mipSize - 1);
        float surface = depthPyramid.Load(int3(cell, level)).r;
        if (!ScreenSpaceIsFarDepth(surface))
        {
            float rayEye = ScreenSpaceLinearEyeDepth(ray.z);
            float surfaceEye = ScreenSpaceLinearEyeDepth(surface);
            float depthDelta = rayEye - surfaceEye;
            if (depthDelta >= 0 && depthDelta <= thickness)
            {
                float hitT = previousDelta < 0 ? lerp(previousT, t, saturate(-previousDelta / max(depthDelta - previousDelta, 1e-6))) : t;
                float2 hitUV = start.xy + delta.xy * hitT;
                uint2 texel = min((uint2)(hitUV * float2(width, height)), uint2(width, height) - 1);
                float texelDepth = depthPyramid.Load(int3(texel, 0)).r;
                if (ScreenSpaceIsFarDepth(texelDepth)) return false;
                hit = float3((float2(texel) + 0.5) / float2(width, height), texelDepth);
                return true;
            }
            previousDelta = depthDelta;
        }
        else
        {
            previousDelta = -1;
        }
        previousT = t;
        t += stepT;
    }
    return false;
}
#endif
