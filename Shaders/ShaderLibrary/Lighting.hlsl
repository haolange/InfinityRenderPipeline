#ifndef _LightingInclude
#define _LightingInclude

#include "Lighting/LightRecord.hlsl"

// First g_DirectionalLightCount records are directional; the remaining g_LocalLightCount are local, sorted by view
// depth. Light-list indices are absolute record indices.
int g_DirectionalLightCount;
int g_LocalLightCount;
int g_SunRecordIndex;
int g_LocalShadowSliceCount;
StructuredBuffer<FLightRecord> g_LightRecordBuffer;

#include "Lighting/LightLoop.hlsl"

int SelectPointShadowFace(float3 L)
{
    float3 a = abs(L);
    int face = 0;
    if (a.x >= a.y && a.x >= a.z)
    {
        face = L.x >= 0.0 ? 0 : 1;
    }
    else if (a.y >= a.z)
    {
        face = L.y >= 0.0 ? 2 : 3;
    }
    else face = L.z >= 0.0 ? 4 : 5;
    return face;
}

#endif
