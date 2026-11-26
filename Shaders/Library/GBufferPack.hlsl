#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
#ifndef _PackDataInclude
#define _PackDataInclude

#include "Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "GBufferNormal.hlsl"

// GBuffer A (R8G8B8A8_SRGB): albedo rgb, roughness. GBuffer B (R8G8B8A8_UNorm): octahedral normal xy, metallic, specular.
// Deferred and forward pixels both write it; for a forward pixel it is the surface proxy of the screen-space effects.

struct SurfaceData
{
    float3 Albedo;
    float  Roughness;
    float3 Normal;
    float  Metallic;
    float  Specular;
};

float DecodeGBufferRoughness(float4 gBufferA)
{
    return gBufferA.a;
}

void EncodeSurfaceGBuffer(SurfaceData surface, out float4 gBufferA, out float4 gBufferB)
{
    gBufferA = float4(surface.Albedo, surface.Roughness);
    gBufferB = float4(EncodeOctahedralNormal(surface.Normal), surface.Metallic, surface.Specular);
}

void DecodeSurface(uint2 pixel, Texture2D texA, Texture2D texB, out SurfaceData surface)
{
    float4 gBufferA = texA[pixel];
    float4 gBufferB = texB[pixel];
    surface.Albedo = gBufferA.rgb;
    surface.Roughness = gBufferA.a;
    surface.Normal = DecodeGBufferNormal(gBufferB);
    surface.Metallic = gBufferB.b;
    surface.Specular = gBufferB.a;
}

#endif
