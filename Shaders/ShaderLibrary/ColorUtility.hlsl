#ifndef _ColorUtilityInclude
#define _ColorUtilityInclude

// Owned here so Infinity Common.hlsl does not collide with Unity Core Common.hlsl's Luminance.
#ifndef UNITY_COMMON_INCLUDED
float Luminance(float3 linearRgb)
{
    return dot(linearRgb, float3(0.2126729, 0.7151522, 0.0721750));
}

float Luminance(float4 linearRgba)
{
    return Luminance(linearRgba.rgb);
}
#endif

#endif
