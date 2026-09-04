#ifndef _LightingInclude
#define _LightingInclude

#define LIGHT_TYPE_DIRECTIONAL 0
#define LIGHT_TYPE_POINT 1
#define LIGHT_TYPE_SPOT 2
#define LIGHT_TYPE_RECT 3

#define LIGHT_FLAG_SHADOW 1
#define LIGHT_FLAG_CONTACT 2
#define LIGHT_FLAG_VOLUMETRIC 4
#define LIGHT_FLAG_INDIRECT 8

// Packed visible-light record. First g_DirectionalLightCount entries are directional;
// remaining g_LocalLightCount entries are Point/Spot/Rect. ZBin indices are absolute.
// Must match C# FLightRecord (explicit 16-byte aligned Sequential pack).
struct FLightRecord
{
    float4 radiance;
    float4 positionRange;
    float4 directionSpot;
    float4 shape;
    float4 axisX;
    float4 axisY;
    float4 shadowAtlasRect;
    float4 shadowSoftVol;
    float4 extra;
    int lightType;
    int lightLayer;
    int flags;
    int shadowMatrixIndex;
    int shadowSliceCount;
    int shadowType;
    int visibleLightIndex;
    int unused0;
};

struct FLightBounds
{
    float4 centerRadius;
    float4 zRange;
};

int g_DirectionalLightCount;
int g_LocalLightCount;
int g_HasTileLightList;
int g_SunRecordIndex;
int g_LocalShadowSliceCount;
StructuredBuffer<FLightRecord> g_LightRecordBuffer;
StructuredBuffer<FLightBounds> g_LightBoundsBuffer;
StructuredBuffer<float4x4> SRV_LocalShadowMatrices;
StructuredBuffer<float4> SRV_LocalShadowRects;
StructuredBuffer<uint2> SRV_TileLightRange;
StructuredBuffer<uint> SRV_TileLightList;
StructuredBuffer<uint2> SRV_ZBinRange;
StructuredBuffer<uint> SRV_ZBinLightList;

float3 LightRadiance(FLightRecord light)
{
    return light.radiance.rgb;
}

float DistanceAttenuation(float distance, float range)
{
    float invRange = rcp(max(range, 1e-4));
    float d = saturate(1.0 - pow(distance * invRange, 4.0));
    return d * d * rcp(max(distance * distance, 1e-4));
}

float SpotAttenuation(float3 L, float3 spotToLight, float innerCos, float outerCos)
{
    float cosAngle = dot(L, spotToLight);
    return saturate((cosAngle - outerCos) / max(innerCos - outerCos, 1e-4));
}

// Karis representative point for rectangular area specular (no LTC LUT).
float3 KarisRectRepresentativePoint(float3 positionWS, float3 R, FLightRecord light)
{
    float3 center = light.positionRange.xyz;
    float3 axisX = light.axisX.xyz;
    float3 axisY = light.axisY.xyz;
    float halfW = light.shape.x * 0.5;
    float halfH = light.shape.y * 0.5;
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
    float3 axisX = light.axisX.xyz;
    float3 axisY = light.axisY.xyz;
    float halfW = light.shape.x * 0.5;
    float halfH = light.shape.y * 0.5;
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
