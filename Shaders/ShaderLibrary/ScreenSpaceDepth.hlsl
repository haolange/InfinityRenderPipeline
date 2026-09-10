#ifndef INFINITY_SCREEN_SPACE_DEPTH
#define INFINITY_SCREEN_SPACE_DEPTH
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
// Near, far, orthographic, reversed Z. Bound explicitly for each camera/pass.
float4 ScreenSpaceDepthParams;
bool ScreenSpaceIsFarDepth(float depth)
{
#if UNITY_REVERSED_Z
    return depth <= 1e-7;
#else
    return depth >= 1 - 1e-7;
#endif
}
float ScreenSpaceLinearEyeDepth(float rawDepth)
{
    float near = ScreenSpaceDepthParams.x, far = ScreenSpaceDepthParams.y;
    if (ScreenSpaceDepthParams.z != 0)
        return lerp(near, far, ScreenSpaceDepthParams.w != 0 ? 1 - rawDepth : rawDepth);
    float denominator = ScreenSpaceDepthParams.w != 0 ? near + rawDepth * (far - near) : far - rawDepth * (far - near);
    return near * far / max(denominator, 1e-8);
}
float ScreenSpaceDepthWeight(float center, float neighbor, float sigma)
{
    float delta = (center - neighbor) / max(sigma, 1e-6);
    return exp(-0.5 * delta * delta);
}
float ScreenSpaceNormalWeight(float3 center, float3 neighbor, float exponent)
{
    return pow(saturate(dot(center, neighbor)), exponent);
}
float3 ScreenSpaceViewDirection(float3 positionVS)
{
    return ScreenSpaceDepthParams.z != 0 ? float3(0, 0, 1) : normalize(-positionVS);
}
#endif
