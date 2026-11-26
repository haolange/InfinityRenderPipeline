#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
#ifndef _PackDataInclude
#define _PackDataInclude

#include "Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// GBuffer A (R8G8B8A8_SRGB): albedo rgb, roughness. GBuffer B (R8G8B8A8_UNorm): octahedral normal xy, metallic, specular.
// Deferred and forward pixels both write it; for a forward pixel it is the surface proxy of the screen-space effects.

float2 UnitVectorToOctahedron(float3 N)
{
    N.xy /= dot(1, abs(N));
    if (N.z <= 0)
    {
        N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? 1 : -1);
    }
    return N.xy;
}

float3 OctahedronToUnitVector(float2 Oct)
{
    float3 N = float3(Oct, 1 - dot(1, abs(Oct)));
    if (N.z < 0)
    {
        N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? float2(1, 1) : float2(-1, -1));
    }
    return normalize(N);
}

float2 EncodeOctahedralNormal(float3 normalWS)
{
    float lenSq = dot(normalWS, normalWS);
    if (lenSq <= 1e-8)
    {
        return float2(0.5, 0.5);
    }
    return UnitVectorToOctahedron(normalWS * rsqrt(lenSq)) * 0.5 + 0.5;
}

float3 DecodeOctahedralNormal(float2 encoded)
{
    return OctahedronToUnitVector(encoded * 2.0 - 1.0);
}

struct SurfaceData
{
    float3 Albedo;
    float  Roughness;
    float3 Normal;
    float  Metallic;
    float  Specular;
};

float3 DecodeGBufferNormal(float4 gBufferB)
{
    return DecodeOctahedralNormal(gBufferB.rg);
}

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
