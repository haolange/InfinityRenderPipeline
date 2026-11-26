#ifndef INFINITY_SCREEN_SPACE_REPROJECTION
#define INFINITY_SCREEN_SPACE_REPROJECTION
#include "ScreenSpaceDepth.hlsl"
void ReprojectSurface(float2 uv, float depth, float2 surfaceMotion, float4x4 inverseJitteredVP,
    float4x4 previousJitteredVP, float4x4 currentMotionVP, float4x4 previousMotionVP,
    out float2 historyUV, out float previousDepth)
{
    historyUV = -1;
    previousDepth = depth;
    float4 world = mul(inverseJitteredVP, float4(uv * 2 - 1, depth, 1));
    world /= world.w;
    float4 previous = mul(previousJitteredVP, world);
    float4 currentMotion = mul(currentMotionVP, world);
    float4 previousMotion = mul(previousMotionVP, world);
    if (previous.w > 0 && currentMotion.w > 0 && previousMotion.w > 0)
    {
        float2 cameraMotion = (currentMotion.xy / currentMotion.w - previousMotion.xy / previousMotion.w) * 0.5;
        historyUV = previous.xy / previous.w * 0.5 + 0.5 + cameraMotion - surfaceMotion;
        previousDepth = previous.z / previous.w;
    }
}
// Reprojects the virtual image of a reflection (hit-point reprojection, Stachowiak 2015): for a planar mirror the
// reflected object appears behind the surface, along the view ray, at the reflected ray's hit distance. Rough lobes
// have no single virtual point; callers blend toward surface reprojection by roughness. Assumes a static reflected
// object (there is no motion vector for the virtual point).
float2 ReprojectVirtualPoint(float3 surfaceWS, float3 viewRayWS, float hitDistance, float4x4 previousJitteredVP)
{
    float2 historyUV = -1;
    float3 virtualWS = surfaceWS + viewRayWS * hitDistance;
    float4 previous = mul(previousJitteredVP, float4(virtualWS, 1));
    if (previous.w > 0) historyUV = previous.xy / previous.w * 0.5 + 0.5;
    return historyUV;
}
#endif
