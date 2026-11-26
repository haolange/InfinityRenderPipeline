#include "Packages/com.infinity.render-pipeline/Shaders/Library/TextureView.hlsl"
#ifndef _PackDataInclude
#define _PackDataInclude

#include "Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

Texture2D g_BestFitNormal_LUT;

// Surface routing identifies the execution path, not a global material BRDF switch.
#define SURFACE_ROUTE_NONE     0u
#define SURFACE_ROUTE_DEFERRED 1u
#define SURFACE_ROUTE_FORWARD  2u
#define SURFACE_ROUTE_UNLIT    3u

#define SURFACE_FLAG_HAS_BAKED_GI 1u
#define SURFACE_FLAG_SUBSURFACE   2u

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

float2 UnitVectorToHemiOctahedron(float3 N)
{
    N.xy /= dot(1, abs(N));
    return float2(N.x + N.y, N.x - N.y);
}

float3 HemiOctahedronToUnitVector(float2 Oct)
{
    Oct = float2(Oct.x + Oct.y, Oct.x - Oct.y) * 0.5;
    float3 N = float3(Oct, 1 - dot(1, abs(Oct)));
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
    uint   Route;
    uint   Flags;
    uint   RenderingLayer;
};

struct SubsurfaceCustomData
{
    uint  ProfileIndex;
    float Thickness;
};

struct ReconstructInput
{
    uint2 PixelCoord;
    float2 CoCgR;
    float2 CoCgL;
    float2 CoCgT;
    float2 CoCgB;
    float4 NeighborValid;
};

float3 EncodeBestFit(float3 Dir)
{
    float3 uN = abs(Dir);
    float maxNAbs = max(uN.z, max(uN.x, uN.y));
    float2 texcoord = uN.z < maxNAbs ? (uN.y < maxNAbs ? uN.yz : uN.xz) : uN.xy;
    texcoord = texcoord.x < texcoord.y ? texcoord.yx : texcoord.xy;
    texcoord.y /= texcoord.x;
    Dir /= maxNAbs;
    Dir *= InfinitySampleLevel(g_BestFitNormal_LUT, Global_point_clamp_sampler, texcoord, 0).r;
    return Dir;
}

float EdgeFilter(float2 center, float2 a0, float2 a1, float2 a2, float2 a3, float4 valid)
{
    float4 delta = abs(float4(a0.x, a1.x, a2.x, a3.x) - center.x);
    float4 w = (1.0 - step(0.1176, delta)) * valid;
    float total = dot(w, 1.0);
    if (total == 0)
    {
        // Clear pixels encode no surface and must never supply their zero channel.
        float4 distance = delta + (1.0 - valid) * 1e4;
        float nearest = min(min(distance.x, distance.y), min(distance.z, distance.w));
        w = step(distance, nearest) * valid;
        total = dot(w, 1.0);
    }
    return total > 0 ? dot(w, float4(a0.y, a1.y, a2.y, a3.y)) / total : 128.0 / 255.0;
}

float PackSurfaceMetadataR(uint route, uint flags)
{
    uint packed = (route & 0x3u) | ((flags & 0x3Fu) << 2);
    return packed / 255.0;
}

void UnpackSurfaceMetadataR(float packedR, out uint route, out uint flags)
{
    uint packed = (uint)(packedR * 255.0 + 0.5);
    route = packed & 0x3u;
    flags = (packed >> 2) & 0x3Fu;
}

float2 PackSurfaceMetadata(uint route, uint flags, uint renderingLayer)
{
    return float2(PackSurfaceMetadataR(route, flags), (renderingLayer & 0xFFu) / 255.0);
}

void UnpackSurfaceMetadata(float2 packed, bool hasRenderingLayers, out uint route, out uint flags, out uint renderingLayer)
{
    UnpackSurfaceMetadataR(packed.r, route, flags);
    renderingLayer = hasRenderingLayers ? (uint)(packed.g * 255.0 + 0.5) : 0xFFu;
}

float2 PackCustomDataSSS(uint profileIndex, float thickness)
{
    return float2(saturate(profileIndex / 255.0), saturate(thickness));
}

SubsurfaceCustomData UnpackCustomDataSSS(float2 packed)
{
    SubsurfaceCustomData data;
    data.ProfileIndex = (uint)(packed.r * 255.0 + 0.5);
    data.Thickness = packed.g;
    return data;
}

// GBuffer albedo uses Color.hlsl RGBToYCoCg / YCoCgToRGB (8-bit chroma bias 128/255).
float3 GBufferAlbedoToYCoCg(float3 rgb)
{
    return RGBToYCoCg(rgb);
}

float3 GBufferYCoCgToAlbedo(float3 ycocg)
{
    return YCoCgToRGB(ycocg);
}

uint2 GBufferPixelCoord(float2 svPositionXY)
{
    return uint2(svPositionXY);
}

float3 DecodeGBufferNormal(float4 gBufferB)
{
#if defined(INFINITY_GBUFFER_BESTFIT_YCOCG)
    float lenSq = dot(gBufferB.xyz * 2.0 - 1.0, gBufferB.xyz * 2.0 - 1.0);
    return lenSq > 1e-8 ? normalize(gBufferB.xyz * 2.0 - 1.0) : float3(0, 0, 1);
#else
    return DecodeOctahedralNormal(gBufferB.rg);
#endif
}

float DecodeGBufferRoughness(float4 gBufferA)
{
#if defined(INFINITY_GBUFFER_BESTFIT_YCOCG)
    return gBufferA.b;
#else
    return gBufferA.a;
#endif
}

float DecodeGBufferMetallic(float4 gBufferA, float4 gBufferB)
{
#if defined(INFINITY_GBUFFER_BESTFIT_YCOCG)
    return gBufferA.a;
#else
    return gBufferB.b;
#endif
}

float DecodeGBufferSpecular(float4 gBufferB)
{
    return gBufferB.a;
}

void EncodeCoreGBuffer(SurfaceData surface, float2 svPositionXY, out float4 gBufferA, out float4 gBufferB)
{
#if defined(INFINITY_GBUFFER_BESTFIT_YCOCG)
    uint2 pixelCoord = GBufferPixelCoord(svPositionXY);
    float3 ycocg = GBufferAlbedoToYCoCg(surface.Albedo);
    gBufferA = float4(((pixelCoord.x & 1) == (pixelCoord.y & 1)) ? ycocg.rg : ycocg.rb, surface.Roughness, surface.Metallic);
    gBufferB = float4(EncodeBestFit(surface.Normal) * 0.5 + 0.5, surface.Specular);
#else
    gBufferA = float4(surface.Albedo, surface.Roughness);
    gBufferB = float4(EncodeOctahedralNormal(surface.Normal), surface.Metallic, surface.Specular);
#endif
}

void EncodeSurfaceGBuffer(SurfaceData surface, float2 svPositionXY, out float4 gBufferA, out float4 gBufferB, out float2 surfaceMetadata)
{
    EncodeCoreGBuffer(surface, svPositionXY, gBufferA, gBufferB);
    surfaceMetadata = PackSurfaceMetadata(surface.Route, surface.Flags, surface.RenderingLayer);
}

void DecodeSurface(uint2 pixel, Texture2D texA, Texture2D texB, Texture2D texMeta, float4 textureView, bool hasRenderingLayers, out SurfaceData surface)
{
    float4 gBufferA = texA[pixel];
    float4 gBufferB = texB[pixel];
    float2 meta = texMeta[pixel].rg;

#if defined(INFINITY_GBUFFER_BESTFIT_YCOCG)
    uint width, height;
    texA.GetDimensions(width, height);
    if (textureView.x > 0)
    {
        width = textureView.x;
        height = textureView.y;
    }
    int2 offsets[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
    float2 chroma[4];
    float4 valid = 0;
    [unroll] for (int i = 0; i < 4; ++i)
    {
        int2 neighbor = int2(pixel) + offsets[i];
        bool inside = all(neighbor >= 0) && all(neighbor < int2(width, height));
        int2 bounded = clamp(neighbor, 0, int2(width, height) - 1);
        chroma[i] = texA[bounded].rg;
        uint nRoute, nFlags;
        UnpackSurfaceMetadataR(texMeta[bounded].r, nRoute, nFlags);
        valid[i] = inside && nRoute != SURFACE_ROUTE_NONE ? 1.0 : 0.0;
    }
    float3 ycocg = gBufferA.rgb;
    ycocg.b = EdgeFilter(gBufferA.rg, chroma[0], chroma[1], chroma[2], chroma[3], valid);
    ycocg.rgb = ((pixel.x & 1) == (pixel.y & 1)) ? ycocg.rgb : ycocg.rbg;
    surface.Albedo = GBufferYCoCgToAlbedo(ycocg);
#else
    surface.Albedo = gBufferA.rgb;
#endif

    surface.Roughness = DecodeGBufferRoughness(gBufferA);
    surface.Normal = DecodeGBufferNormal(gBufferB);
    surface.Metallic = DecodeGBufferMetallic(gBufferA, gBufferB);
    surface.Specular = DecodeGBufferSpecular(gBufferB);
    UnpackSurfaceMetadata(meta, hasRenderingLayers, surface.Route, surface.Flags, surface.RenderingLayer);
}

#endif
