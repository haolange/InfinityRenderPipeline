// Geometry color

#ifndef INFINITY_GEOMETRY_COLOR_INCLUDED
#define INFINITY_GEOMETRY_COLOR_INCLUDED

#include "IndirectComposition.hlsl"
#include "Sideband.hlsl"
#include "GBufferOutput.hlsl"

// What only the geometry draw can know, written once. Scene Color is indirect diffuse only: the baked source
// (lightmap, probe volumes, proxy volume or light probes), else the dynamic sky, scaled by the material occlusion.
// Emission is a later pass. Sideband R stays 0 here; only a forward pixel writes the diffuse share. G, B and A are the
// mixed-light visibility, the light channels and the profile.
void WriteGeometryColor(float3 diffuseColor, float occlusion, float4 bakedDiffuse, float4 shadowmask, float3 normalWS,
    StructuredBuffer<float4> skySH, uint lightChannels, uint profile, inout GBufferFragmentOutput output)
{
    output.sceneColor = diffuseColor * IndirectIrradiance(bakedDiffuse, normalWS, skySH) * occlusion;
#if defined(INFINITY_SIDEBAND)
    SidebandData sideband;
    sideband.DiffuseRatio = 0;
    sideband.BakedVisibility = SelectMixedLightVisibility(shadowmask);
    sideband.LightChannels = lightChannels & SIDEBAND_LIGHT_CHANNELS_ALL;
    sideband.Profile = profile;
    output.sideband = PackSideband(sideband);
#endif
}

#endif
