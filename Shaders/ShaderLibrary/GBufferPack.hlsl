#ifndef _PackDataInclude
#define _PackDataInclude

#include "Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

Texture2D g_BestFitNormal_LUT;

#define GBUFFER_SHADING_MODEL_DEFAULT_LIT 0u
#define GBUFFER_SHADING_MODEL_SUBSURFACE 1u
#define GBUFFER_FLAG_SUBSURFACE 1u
#define GBUFFER_FLAG_FORWARD 2u
#define GBUFFER_FLAG_VALID_SURFACE 4u

//CoordSpace
float2 UnitVectorToOctahedron(float3 N)
{
    N.xy /= dot( 1, abs(N) );
    if( N.z <= 0 ) {
        N.xy = ( 1 - abs(N.yx) ) * ( N.xy >= 0 ? 1 : -1 );
    }
    return N.xy;
}

float3 OctahedronToUnitVector(float2 Oct)
{
    float3 N = float3( Oct, 1 - dot( 1, abs(Oct) ) );
    if( N.z < 0 ) {
        N.xy = ( 1 - abs(N.yx) ) * ( N.xy >= 0 ? float2(1, 1) : float2(-1, -1) );
    }
    return normalize(N);
}

float2 UnitVectorToHemiOctahedron(float3 N)
{
	N.xy /= dot( 1, abs(N) );
	return float2( N.x + N.y, N.x - N.y );
}

float3 HemiOctahedronToUnitVector(float2 Oct)
{
	Oct = float2( Oct.x + Oct.y, Oct.x - Oct.y ) * 0.5;
	float3 N = float3( Oct, 1 - dot( 1, abs(Oct) ) );
	return normalize(N);
}

struct FGBufferData
{
    float Specular;
    float Roughness;
    float Reflactance;
    float3 Albedo;
    float3 Normal;
    uint ShadingModel;
    uint Flags;
    uint SSSProfileIndex;
    float Thickness;
    uint RenderingLayer;
};

struct FReconstructInput
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
    Dir *= g_BestFitNormal_LUT.SampleLevel(Global_point_clamp_sampler, texcoord, 0).r;
    return Dir;
}

float EdgeFilter(float2 center, float2 a0, float2 a1, float2 a2, float2 a3, float4 valid)
{
    float4 delta = abs(float4(a0.x, a1.x, a2.x, a3.x) - center.x);
    float4 w = (1.0 - step(0.1176, delta)) * valid;
    float total = dot(w, 1.0);
    if (total == 0)
    {
        // Missing chroma can only come from a covered, opposite-parity texel.
        // Clear pixels encode no surface and must never supply their zero channel.
        float4 distance = delta + (1.0 - valid) * 1e4;
        float nearest = min(min(distance.x, distance.y), min(distance.z, distance.w));
        w = step(distance, nearest) * valid;
        total = dot(w, 1.0);
    }
    return total > 0 ? dot(w, float4(a0.y, a1.y, a2.y, a3.y)) / total : 128.0 / 255.0;
}

float PackGBufferCChannelR(uint shadingModel, uint flags)
{
    uint packed = (shadingModel & 0xFu) | ((flags & 0xFu) << 4);
    return packed / 255.0;
}

void UnpackGBufferCChannelR(float packedR, out uint shadingModel, out uint flags)
{
    uint packed = (uint)(packedR * 255.0 + 0.5);
    shadingModel = packed & 0xFu;
    flags = (packed >> 4) & 0xFu;
}

// GBuffer albedo uses Color.hlsl RGBToYCoCg / YCoCgToRGB (8-bit chroma bias 128/255).
// Common.hlsl RGB2YCoCg / YCoCg2RGB are a different unnormalized transform (TAA / BC).
float3 GBufferAlbedoToYCoCg(float3 rgb)
{
    return RGBToYCoCg(rgb);
}

float3 GBufferYCoCgToAlbedo(float3 ycocg)
{
    return YCoCgToRGB(ycocg);
}

// Checkerboard parity is the integer pixel of SV_POSITION.xy (pixel center truncates).
// Compute decode must pass the same texel index (DispatchThreadID.xy / Texture2D[pixel]).
uint2 GBufferPixelCoord(float2 svPositionXY)
{
    return uint2(svPositionXY);
}

void EncodeGBuffer(FGBufferData GBufferData, float2 svPositionXY, out float4 GBufferA, out float4 GBufferB, out float4 GBufferC)
{
    uint2 PixelCoord = GBufferPixelCoord(svPositionXY);
    float3 YCoCgColor = GBufferAlbedoToYCoCg(GBufferData.Albedo);
    GBufferA = float4(((PixelCoord.x & 1) == (PixelCoord.y & 1)) ? YCoCgColor.rg : YCoCgColor.rb, GBufferData.Roughness, GBufferData.Reflactance);
    GBufferB = float4(EncodeBestFit(GBufferData.Normal) * 0.5 + 0.5, GBufferData.Specular);
    GBufferC = float4(
        PackGBufferCChannelR(GBufferData.ShadingModel, GBufferData.Flags | GBUFFER_FLAG_VALID_SURFACE),
        saturate(GBufferData.SSSProfileIndex / 255.0),
        saturate(GBufferData.Thickness),
        GBufferData.RenderingLayer / 255.0);
}

void DecodeGBuffer(FReconstructInput ReconstructInput, float4 GBufferA, float4 GBufferB, float4 GBufferC, out FGBufferData GBufferData)
{
    float3 YCoCgColor = GBufferA.rgb;
    YCoCgColor.b = EdgeFilter(GBufferA.rg, ReconstructInput.CoCgR, ReconstructInput.CoCgL, ReconstructInput.CoCgT, ReconstructInput.CoCgB, ReconstructInput.NeighborValid);
    YCoCgColor.rgb = ((ReconstructInput.PixelCoord.x & 1) == (ReconstructInput.PixelCoord.y & 1)) ? YCoCgColor.rgb : YCoCgColor.rbg;

    GBufferData.Specular = GBufferB.a;
    GBufferData.Roughness = GBufferA.b;
    GBufferData.Albedo = GBufferYCoCgToAlbedo(YCoCgColor);
    GBufferData.Reflactance = GBufferA.a;
    GBufferData.Normal = normalize(GBufferB.xyz * 2 - 1);
    UnpackGBufferCChannelR(GBufferC.r, GBufferData.ShadingModel, GBufferData.Flags);
    GBufferData.SSSProfileIndex = (uint)(GBufferC.g * 255.0 + 0.5);
    GBufferData.Thickness = GBufferC.b;
    GBufferData.RenderingLayer = (uint)(GBufferC.a * 255.0 + 0.5);
}

void DecodeGBuffer(uint2 pixel, Texture2D texA, Texture2D texB, Texture2D texC, out FGBufferData GBufferData)
{
    float4 gBufferA = texA[pixel];
    float4 gBufferB = texB[pixel];
    float4 gBufferC = texC[pixel];

    FReconstructInput reconstructInput;
    reconstructInput.PixelCoord = pixel;
    uint width, height;
    texA.GetDimensions(width, height);
    int2 offsets[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
    float2 chroma[4];
    float4 valid = 0;
    [unroll] for (int i = 0; i < 4; ++i)
    {
        int2 neighbor = int2(pixel) + offsets[i];
        bool inside = all(neighbor >= 0) && all(neighbor < int2(width, height));
        int2 bounded = clamp(neighbor, 0, int2(width, height) - 1);
        chroma[i] = texA[bounded].rg;
        // Coverage is explicit: every packed normal, including RGB zero, can represent a real surface.
        uint packed = (uint)(texC[bounded].r * 255.0 + 0.5);
        valid[i] = inside && (packed & (GBUFFER_FLAG_VALID_SURFACE << 4)) != 0 ? 1.0 : 0.0;
    }
    reconstructInput.CoCgR = chroma[0];
    reconstructInput.CoCgL = chroma[1];
    reconstructInput.CoCgT = chroma[2];
    reconstructInput.CoCgB = chroma[3];
    reconstructInput.NeighborValid = valid;

    DecodeGBuffer(reconstructInput, gBufferA, gBufferB, gBufferC, GBufferData);
}

#endif
