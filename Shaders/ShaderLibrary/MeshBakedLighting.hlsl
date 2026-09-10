#ifndef INFINITY_MESH_BAKED_LIGHTING
#define INFINITY_MESH_BAKED_LIGHTING
#include "Lightmap.hlsl"
struct FMeshBakedLighting
{
    float4 metadata, scaleOffset;
    float4 shAr, shAg, shAb, shBr, shBg, shBb, shC;
    float4 occlusion;
};
StructuredBuffer<FMeshBakedLighting> meshBakedLightingBuffer;
#if defined(INFINITY_MESH_LIGHTMAP)
TEXTURE2D(_MeshLightmapColor); SAMPLER(sampler_MeshLightmapColor);
#if defined(INFINITY_MESH_DIRECTIONAL)
TEXTURE2D(_MeshLightmapDirection);
#endif
#if defined(INFINITY_MESH_SHADOWMASK)
TEXTURE2D(_MeshShadowMask); SAMPLER(sampler_MeshShadowMask);
#endif
#endif
void SampleMeshBakedLighting(uint transformIndex, float2 uv, float3 normal, out float4 diffuse, out float4 mask)
{
    FMeshBakedLighting data = meshBakedLightingBuffer[transformIndex];
    mask = data.metadata.y > 0 ? data.occlusion : 1;
#if defined(INFINITY_MESH_LIGHTMAP)
    uv = uv * data.scaleOffset.xy + data.scaleOffset.zw;
#if defined(INFINITY_MESH_DIRECTIONAL)
    diffuse = float4(SampleDirectionalLightmap(TEXTURE2D_ARGS(_MeshLightmapColor, sampler_MeshLightmapColor),
        TEXTURE2D_ARGS(_MeshLightmapDirection, sampler_MeshLightmapColor), uv, float4(1, 1, 0, 0), normal, true), 1);
#else
    diffuse = float4(SampleSingleLightmap(TEXTURE2D_ARGS(_MeshLightmapColor, sampler_MeshLightmapColor),
        uv, float4(1, 1, 0, 0), true), 1);
#endif
    mask = 1;
#if defined(INFINITY_MESH_SHADOWMASK)
    mask = _MeshShadowMask.Sample(sampler_MeshShadowMask, uv);
#endif
#else
    float3 sh = SHEvalLinearL0L1(normal, data.shAr, data.shAg, data.shAb) + SHEvalLinearL2(normal, data.shBr, data.shBg, data.shBb, data.shC);
    diffuse = float4(data.metadata.y > 0 ? max(0, sh) : 0, data.metadata.y > 0 ? 1 : 0);
#endif
}
#endif
