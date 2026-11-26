// Sideband

#ifndef INFINITY_SIDEBAND_INCLUDED
#define INFINITY_SIDEBAND_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// R8G8B8A8_UNorm, allocated only when one of its readers records.
// R: the diffuse share of a forward pixel's Scene Color luminance, for skin scattering. A pixel that was not forward-shaded
//    stays 0. Specular and emission are never counted.
// G, B: baked visibility of the first and second mixed directional light.
// A: four light channels in the high nibble, subsurface profile 1..15 in the low nibble (0 marks a pixel that is not subsurface).
#define SIDEBAND_LIGHT_CHANNELS_ALL 0xFu

struct SidebandData
{
    float  DiffuseRatio;
    float2 BakedVisibility;
    uint   LightChannels;
    uint   Profile;
};

// The values a reader sees for a pixel when the camera allocated no sideband.
SidebandData DefaultSideband()
{
    SidebandData data;
    data.DiffuseRatio = 0;
    data.BakedVisibility = 1;
    data.LightChannels = SIDEBAND_LIGHT_CHANNELS_ALL;
    data.Profile = 0;
    return data;
}

float4 PackSideband(SidebandData data)
{
    uint packedA = ((data.LightChannels & 0xFu) << 4) | (data.Profile & 0xFu);
    return float4(saturate(data.DiffuseRatio), saturate(data.BakedVisibility), packedA / 255.0);
}

SidebandData UnpackSideband(float4 packed)
{
    uint packedA = (uint)(packed.a * 255.0 + 0.5);
    SidebandData data;
    data.DiffuseRatio = packed.r;
    data.BakedVisibility = packed.gb;
    data.LightChannels = packedA >> 4;
    data.Profile = packedA & 0xFu;
    return data;
}

// The share of color's luminance that diffuse carries; a black pixel carries none.
float DiffuseRatio(float3 diffuse, float3 color)
{
    float total = Luminance(max(color, 0));
    return total > 0 ? saturate(Luminance(max(diffuse, 0)) / total) : 0;
}

// Unity shadowmask channel of the first (x) and second (y) mixed directional light of the camera, or -1 when the slot is empty.
float4 _MixedLightChannels;

// Baked visibility of the two mixed light slots, picked from a renderer's shadowmask or probe occlusion.
float2 SelectMixedLightVisibility(float4 shadowmask)
{
    int2 channel = (int2)_MixedLightChannels.xy;
    float2 visibility = 1;
    if (channel.x >= 0)
    {
        visibility.x = shadowmask[channel.x];
    }
    if (channel.y >= 0)
    {
        visibility.y = shadowmask[channel.y];
    }
    return visibility;
}

#endif
