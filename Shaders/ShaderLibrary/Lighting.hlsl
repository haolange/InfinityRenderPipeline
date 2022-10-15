#ifndef _LightingInclude
#define _LightingInclude

struct FDirectionalLightElement
{
    float4 color;

    float4 directional;

    float diffuse;
    float specular;
    float radius;
    int lightLayer;

    int enableIndirect;
    float indirectIntensity;
    int shadowType;
    float minSoftness;

    float maxSoftness;
    int enableContactShadow;
    float contactShadowLength;
    int enableVolumetric;

    float volumetricIntensity;
    float volumetricOcclusion;
    float maxDrawDistance;
    float maxDrawDistanceFade;
};
int g_DirectionalLightCount;
StructuredBuffer<FDirectionalLightElement> g_DirectionalLightBuffer;

#endif