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
StructuredBuffer<float4x4> SRV_LocalShadowMatrices;
StructuredBuffer<float4> SRV_LocalShadowRects;

#include "Lighting/LightLoop.hlsl"

int SelectPointShadowFace(float3 L)
{
    float3 a = abs(L);
    if (a.x >= a.y && a.x >= a.z)
    {
        return L.x >= 0.0 ? 0 : 1;
    }
    if (a.y >= a.z)
    {
        return L.y >= 0.0 ? 2 : 3;
    }
    return L.z >= 0.0 ? 4 : 5;
}

#endif
