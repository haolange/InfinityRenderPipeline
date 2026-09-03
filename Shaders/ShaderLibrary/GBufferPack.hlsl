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
};

struct FReconstructInput
{
    uint2 PixelCoord;
    float2 CoCgR;
    float2 CoCgL;
    float2 CoCgT;
    float2 CoCgB;
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

float EdgeFilter(float2 center, float2 a0, float2 a1, float2 a2, float2 a3)
{
    float4 lum = float4(a0.x, a1.x, a2.x, a3.x);
    float4 w = 1.0f - step(0.1176, abs(lum - center.x));
    float W = w.x + w.y + w.z + w.w;
    //Handle the special case where all the weights are zero.
    //In HDR scenes it's better to set the chrominance to zero.
    //Here we just use the chrominance of the first neighbor.
    w.x = (W == 0) ? 1 : w.x;
    W = (W == 0) ? 1 : W;

    return (w.x * a0.y + w.y* a1.y + w.z* a2.y + w.w * a3.y) / W;
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

void EncodeGBuffer(FGBufferData GBufferData, uint2 PixelCoord, out float4 GBufferA, out float4 GBufferB, out float4 GBufferC)
{
    float3 YCoCgColor = RGBToYCoCg(GBufferData.Albedo);
    GBufferA = float4(((PixelCoord.x & 1) == (PixelCoord.y & 1)) ? YCoCgColor.rg : YCoCgColor.rb, GBufferData.Roughness, GBufferData.Reflactance);
    GBufferB = float4(EncodeBestFit(GBufferData.Normal) * 0.5 + 0.5, GBufferData.Specular);
    GBufferC = float4(
        PackGBufferCChannelR(GBufferData.ShadingModel, GBufferData.Flags),
        saturate(GBufferData.SSSProfileIndex / 255.0),
        saturate(GBufferData.Thickness),
        0);
}

void DecodeGBuffer(FReconstructInput ReconstructInput, float4 GBufferA, float4 GBufferB, float4 GBufferC, out FGBufferData GBufferData)
{
    float3 YCoCgColor = GBufferA.rgb;
    YCoCgColor.b = EdgeFilter(GBufferA.rg, ReconstructInput.CoCgR, ReconstructInput.CoCgL, ReconstructInput.CoCgT, ReconstructInput.CoCgB);
    YCoCgColor.rgb = ((ReconstructInput.PixelCoord.x & 1) == (ReconstructInput.PixelCoord.y & 1)) ? YCoCgColor.rgb : YCoCgColor.rbg;

    GBufferData.Specular = GBufferB.a;
    GBufferData.Roughness = GBufferA.b;
    GBufferData.Albedo = YCoCgToRGB(YCoCgColor);
    GBufferData.Reflactance = GBufferA.a;
    GBufferData.Normal = normalize(GBufferB.xyz * 2 - 1);
    UnpackGBufferCChannelR(GBufferC.r, GBufferData.ShadingModel, GBufferData.Flags);
    GBufferData.SSSProfileIndex = (uint)(GBufferC.g * 255.0 + 0.5);
    GBufferData.Thickness = GBufferC.b;
}

void DecodeGBuffer(uint2 pixel, Texture2D texA, Texture2D texB, Texture2D texC, out FGBufferData GBufferData)
{
    float4 gBufferA = texA[pixel];
    float4 gBufferB = texB[pixel];
    float4 gBufferC = texC[pixel];

    FReconstructInput reconstructInput;
    reconstructInput.PixelCoord = pixel;
    reconstructInput.CoCgR = texA[uint2(pixel.x + 1, pixel.y)].rg;
    reconstructInput.CoCgL = texA[uint2(pixel.x - 1, pixel.y)].rg;
    reconstructInput.CoCgT = texA[uint2(pixel.x, pixel.y + 1)].rg;
    reconstructInput.CoCgB = texA[uint2(pixel.x, pixel.y - 1)].rg;

    DecodeGBuffer(reconstructInput, gBufferA, gBufferB, gBufferC, GBufferData);
}

#endif
