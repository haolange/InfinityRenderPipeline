#ifndef INFINITY_DECAL_CLUSTER
#define INFINITY_DECAL_CLUSTER
// Spatially indexed decal records in drawOrder priority inside each 3D frustum cluster.

#include "Common.hlsl"
#include "Spatial/SpatialClusterList.hlsl"

#define DECAL_AFFECT_ALBEDO   1u
#define DECAL_AFFECT_NORMAL   2u
#define DECAL_AFFECT_MATERIAL 4u

// C# DecalRecord (Runtime/Decal/DecalContext.cs, GPU-RECORD marker).
struct DecalRecord
{
    float4 worldToBox0;
    float4 worldToBox1;
    float4 worldToBox2;
    float4 albedoAlpha;
    // x = roughness, y = metallic, z = specular, w = affectMask (uint bits via asuint / integer conversion)
    float4 materialParams;
    // xyz = projector up (+Y in world), w = normal blend weight
    float4 normalBlend;
    // x = scale, y = -cosEnd * scale, z = enabled (1 when > 0), w = unused
    float4 angleFadeParams;
    // x = renderingLayerMask, y = blendDistance, zw = unused
    float4 layerAndFlags;
};

// C# DecalUniform (Runtime/Decal/DecalContext.cs, GPU-RECORD marker).
#if defined(_CLUSTERED_DECALS)
cbuffer DecalUniform
{
    float4 g_DecalClusterParams;
    float4 g_DecalZSliceParams;
    int g_DecalCount;
    int g_DecalPadding0;
    int g_DecalPadding1;
    int g_DecalPadding2;
};

StructuredBuffer<DecalRecord> SRV_DecalRecords;
StructuredBuffer<uint2> SRV_DecalClusterHeaders;
StructuredBuffer<uint> SRV_DecalClusterIndices;

void ApplyClusteredDecals(
    uint2 pixelCoord,
    float viewDepth,
    float3 worldPos,
    uint renderingLayer,
    inout float3 albedo,
    inout float3 normalWS,
    inout float roughness,
    inout float metallic,
    inout float specular)
{
    if (g_DecalCount <= 0)
    {
        return;
    }

    SpatialClusterLoopState loop;
    SpatialBeginClusterLoop(pixelCoord, viewDepth, g_DecalCount,
        g_DecalClusterParams, g_DecalZSliceParams, SRV_DecalClusterHeaders, loop);

    uint decalIndex = 0;
    [loop]
    while (SpatialNextClusterItem(loop, SRV_DecalClusterIndices, decalIndex))
    {
        DecalRecord rec = SRV_DecalRecords[decalIndex];
        uint layerMask = (uint)rec.layerAndFlags.x;
        if ((layerMask & renderingLayer) == 0u)
        {
            continue;
        }

        float4 p = float4(worldPos, 1.0);
        float3 localPos = float3(dot(rec.worldToBox0, p), dot(rec.worldToBox1, p), dot(rec.worldToBox2, p));
        if (any(abs(localPos) > 0.5))
        {
            continue;
        }

        float coverage = saturate(rec.albedoAlpha.a);
        if (rec.angleFadeParams.z > 0.0)
        {
            float3 projectorUp = rec.normalBlend.xyz;
            coverage *= saturate(dot(normalWS, projectorUp) * rec.angleFadeParams.x + rec.angleFadeParams.y);
        }
        if (coverage <= 1e-4)
        {
            continue;
        }

        uint affectMask = (uint)rec.materialParams.w;
        if ((affectMask & DECAL_AFFECT_ALBEDO) != 0u)
        {
            albedo = lerp(albedo, rec.albedoAlpha.rgb, coverage);
        }
        if ((affectMask & DECAL_AFFECT_NORMAL) != 0u)
        {
            float nWeight = coverage * saturate(rec.normalBlend.w);
            normalWS = normalize(lerp(normalWS, rec.normalBlend.xyz, nWeight));
        }
        if ((affectMask & DECAL_AFFECT_MATERIAL) != 0u)
        {
            roughness = lerp(roughness, rec.materialParams.x, coverage);
            metallic = lerp(metallic, rec.materialParams.y, coverage);
            specular = lerp(specular, rec.materialParams.z, coverage);
        }
    }
}
#endif

#endif
