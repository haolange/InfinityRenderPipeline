#ifndef INFINITY_SCREEN_SPACE_REPROJECTION
#define INFINITY_SCREEN_SPACE_REPROJECTION
#include "ScreenSpaceDepth.hlsl"
void ReprojectSurface(float2 uv, float depth, float2 surfaceMotion, float4x4 inverseJitteredVP,
    float4x4 previousJitteredVP, float4x4 currentMotionVP, float4x4 previousMotionVP,
    out float2 historyUV, out float expectedDepth)
{
    float4 world = mul(inverseJitteredVP, float4(uv * 2 - 1, depth, 1));
    world /= world.w;
    float4 previous = mul(previousJitteredVP, world);
    float4 currentMotion = mul(currentMotionVP, world);
    float4 previousMotion = mul(previousMotionVP, world);
    if (previous.w <= 0 || currentMotion.w <= 0 || previousMotion.w <= 0)
    { historyUV = -1; expectedDepth = depth; return; }
    float2 cameraMotion = (currentMotion.xy / currentMotion.w - previousMotion.xy / previousMotion.w) * 0.5;
    historyUV = previous.xy / previous.w * 0.5 + 0.5 + cameraMotion - surfaceMotion;
    expectedDepth = previous.z / previous.w;
}
float HistoryDepthConfidence(Texture2D history, float2 uv, float expectedDepth, uint channel)
{
    if (any(uv < 0) || any(uv >= 1)) return 0;
    uint width, height; history.GetDimensions(width, height);
    int2 pixel = (int2)(uv * float2(width, height));
    float low = ScreenSpaceDepthParams.y, high = ScreenSpaceDepthParams.x;
    [unroll]
    for (int y = -1; y <= 1; y++)
    [unroll]
    for (int x = -1; x <= 1; x++)
    {
        float raw = history.Load(int3(clamp(pixel + int2(x, y), 0, int2(width, height) - 1), 0))[channel];
        float depth = ScreenSpaceLinearEyeDepth(raw);
        low = min(low, depth); high = max(high, depth);
    }
    float expected = ScreenSpaceLinearEyeDepth(expectedDepth);
    float margin = 0.5 * (high - low) + max(0.01 * expected, 0.001);
    return expected >= low - margin && expected <= high + margin ? 1 : 0;
}
#endif
