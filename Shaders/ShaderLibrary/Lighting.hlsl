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

// Half extents of a planar emitter. A disk is shaded as the square of equal area until the disk evaluation lands
// (0.5.0 S8).
float2 RectHalfSize(FLightRecord light)
{
    return IsRectLight(light) ? 0.5 * light.area.xy : (0.5 * sqrt(3.14159265) * light.shape.x).xx;
}

// Karis representative point for rectangular area specular (no LTC LUT).
float3 KarisRectRepresentativePoint(float3 positionWS, float3 R, FLightRecord light)
{
    float3 center = light.positionRange.xyz;
    float3 axisX = light.right.xyz;
    float3 axisY = LightUp(light);
    float2 halfSize = RectHalfSize(light);
    float halfW = halfSize.x;
    float halfH = halfSize.y;
    float3 planeN = normalize(cross(axisX, axisY));
    float denom = dot(R, planeN);
    float t = dot(center - positionWS, planeN) / (abs(denom) > 1e-4 ? denom : 1e-4);
    float3 hit = positionWS + R * max(t, 0.0);
    float3 local = hit - center;
    float x = clamp(dot(local, axisX), -halfW, halfW);
    float y = clamp(dot(local, axisY), -halfH, halfH);
    return center + axisX * x + axisY * y;
}

// Frostbite / Drobot rectangle form factor for diffuse (horizon-aware solid angle).
float FrostbiteRectFormFactor(float3 positionWS, float3 normalWS, FLightRecord light)
{
    float3 center = light.positionRange.xyz;
    float3 axisX = light.right.xyz;
    float3 axisY = LightUp(light);
    float2 halfSize = RectHalfSize(light);
    float halfW = halfSize.x;
    float halfH = halfSize.y;
    float3 p0 = center - axisX * halfW - axisY * halfH;
    float3 p1 = center + axisX * halfW - axisY * halfH;
    float3 p2 = center + axisX * halfW + axisY * halfH;
    float3 p3 = center - axisX * halfW + axisY * halfH;

    float3 v0 = normalize(p0 - positionWS);
    float3 v1 = normalize(p1 - positionWS);
    float3 v2 = normalize(p2 - positionWS);
    float3 v3 = normalize(p3 - positionWS);

    float3 n01 = normalize(cross(v0, v1));
    float3 n12 = normalize(cross(v1, v2));
    float3 n23 = normalize(cross(v2, v3));
    float3 n30 = normalize(cross(v3, v0));

    float g0 = acos(clamp(dot(v0, v1), -1.0, 1.0));
    float g1 = acos(clamp(dot(v1, v2), -1.0, 1.0));
    float g2 = acos(clamp(dot(v2, v3), -1.0, 1.0));
    float g3 = acos(clamp(dot(v3, v0), -1.0, 1.0));

    float form = g0 * dot(n01, normalWS) + g1 * dot(n12, normalWS) + g2 * dot(n23, normalWS) + g3 * dot(n30, normalWS);
    return max(form, 0.0) * 0.5 * rcp(3.14159265);
}

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
