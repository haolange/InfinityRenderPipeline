#ifndef _PackDataInclude
#define _PackDataInclude

#include "Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

Texture2D g_BestFitNormal_LUT;

//PackingData
float3 Pack1212To888(float2 x)
{
	// Pack 12:12 to 8:8:8
    #if 0
        uint2 x1212 = (uint2)(x * 4095);
        uint2 High = x1212 >> 8;
        uint2 Low = x1212 & 255;
        uint3 x888 = uint3( Low, High.x | (High.y << 4) );
        return x888 / 255.0;
    #else
        float2 x1212 = floor( x * 4095 );
        float2 High = floor( x1212 / 256 );	// x1212 >> 8
        float2 Low = x1212 - High * 256;	// x1212 & 255
        float3 x888 = float3( Low, High.x + High.y * 16 );
        return saturate( x888 / 255 );
    #endif
}

float2 Pack888To1212(float3 x)
{
	// Pack 8:8:8 to 12:12
    #if 0
        uint3 x888 = (uint3)(x * 255);
        uint High = x888.z >> 4;
        uint Low = x888.z & 15;
        uint2 x1212 = x888.xy | uint2( Low << 8, High << 8 );
        return x1212 / 4095.0;
    #else
        float3 x888 = floor( x * 255 );
        float High = floor( x888.z / 16 );	// x888.z >> 4
        float Low = x888.z - High * 16;		// x888.z & 15
        float2 x1212 = x888.xy + float2( Low, High ) * 256;
        return saturate( x1212 / 4095 );
    #endif
}

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

void EncodeGBuffer(FGBufferData GBufferData, uint2 PixelCoord, out float4 GBufferA, out float4 GBufferB)
{
    float3 YCoCgColor = RGBToYCoCg(GBufferData.Albedo);
    GBufferA = float4(((PixelCoord.x & 1) == (PixelCoord.y & 1)) ? YCoCgColor.rg : YCoCgColor.rb, GBufferData.Roughness, GBufferData.Reflactance);
    GBufferB = float4(EncodeBestFit(GBufferData.Normal) * 0.5 + 0.5, GBufferData.Specular);
}

void DecodeGBuffer(FReconstructInput ReconstructInput, float4 GBufferA, float4 GBufferB, out FGBufferData GBufferData)
{
    float3 YCoCgColor = GBufferA.rgb;
    YCoCgColor.b = EdgeFilter(GBufferA.rg, ReconstructInput.CoCgR, ReconstructInput.CoCgL, ReconstructInput.CoCgT, ReconstructInput.CoCgB);
    YCoCgColor.rgb = ((ReconstructInput.PixelCoord.x & 1) == (ReconstructInput.PixelCoord.y & 1)) ? YCoCgColor.rgb : YCoCgColor.rbg;

    GBufferData.Specular = GBufferB.a;
    GBufferData.Roughness = GBufferA.b;
    GBufferData.Albedo = YCoCgToRGB(YCoCgColor);
    GBufferData.Reflactance = GBufferA.a;
    GBufferData.Normal = normalize(GBufferB.xyz * 2 - 1);
}

uint2 EncodeMetallicSpecular(float Metallic, float Specular)
{
    float Specular4Bit = floor(Specular * 15);

    uint2 MetallicSpecular = 0;
    MetallicSpecular.x = (uint)(Metallic * 255) * 4;
    MetallicSpecular.x += Specular4Bit / 4;
    MetallicSpecular.y = Specular4Bit % 4;
    return MetallicSpecular;
}

void DecodeMetallicSpecular(uint2 MetallicSpecular, out float Metallic, out float Specular)
{
    Metallic = float(MetallicSpecular.x * 0.25) / 255;        
    uint Specular4Bit = MetallicSpecular.x % 4;
    Specular4Bit = (Specular4Bit * 4) + MetallicSpecular.y;        
    Specular = Specular4Bit / 15.0f;
}

void EncodeGBuffer_Normal20(FGBufferData GBufferData, out float4 GBufferA, out uint4 GBufferB)
{
    uint2 EncodeNormal = floor(UnitVectorToOctahedron(GBufferData.Normal) * 511 + 512);
    GBufferA = float4(GBufferData.Albedo, GBufferData.Roughness);
    GBufferB = uint4(EncodeNormal, EncodeMetallicSpecular(GBufferData.Reflactance, GBufferData.Specular));
}

void DecodeGBuffer_Normal20(float4 GBufferA, float4 GBufferB, out FGBufferData GBufferData)
{
    GBufferData.Roughness = GBufferA.a;
    GBufferData.Albedo = GBufferA.rgb;
    GBufferData.Normal = OctahedronToUnitVector((GBufferB.xy / 1023) * 2 - 1);
    DecodeMetallicSpecular(GBufferB.zw, GBufferData.Reflactance, GBufferData.Specular);
}

void EncodeGBuffer_Normal24(FGBufferData GBufferData, out float4 GBufferA, out float4 GBufferB)
{
    float3 PackedNormal = Pack1212To888(saturate(UnitVectorToOctahedron(GBufferData.Normal) * 0.5 + 0.5));              
    GBufferA = float4(GBufferData.Albedo, GBufferData.Roughness);
    GBufferB = float4(PackedNormal, GBufferData.Reflactance);
}

void DecodeGBuffer_Normal24(float4 GBufferA, float4 GBufferB, out FGBufferData GBufferData)
{
    GBufferData.Normal = OctahedronToUnitVector(Pack888To1212(GBufferB.xyz) * 2 - 1);
    GBufferData.Specular = 0.5;
    GBufferData.Roughness = GBufferA.a;
    GBufferData.Albedo = GBufferB.rgb;
    GBufferData.Reflactance = GBufferB.a;
}

void EncodeGBuffer_RayTrace(FGBufferData GBufferData, out int GBufferA, out int GBufferB)
{
    int2 EncodeNormal = int2(saturate(UnitVectorToOctahedron(GBufferData.Normal) * 0.5 + 0.5) * 0xFFF);
    int EncodeRoughness = int(saturate(GBufferData.Roughness) * 0xFF);
    int3 EncodeAlbedo = int3(saturate(GBufferData.Albedo) * 0xFF);
    int EncodeReflactance = int(saturate(GBufferData.Reflactance) * 0xFF);
                    
    GBufferA = (EncodeNormal.x << 20) + (EncodeNormal.y << 8) + EncodeRoughness;
    GBufferB = (EncodeAlbedo.x << 24) + (EncodeAlbedo.y << 16) + (EncodeAlbedo.z << 8) + EncodeReflactance;
}

void DecodeGBuffer_RayTrace(int GBufferA, int GBufferB, out FGBufferData GBufferData)
{
    GBufferData.Normal = OctahedronToUnitVector(2 * ((int2(GBufferA >> 20, GBufferA >> 8) & 0xFFF) / float(0xFFF)) - 1);
    GBufferData.Specular = 0.5;
    GBufferData.Roughness = ((GBufferA >> 32) & 0xFF) / float(0xFF);
    GBufferData.Albedo = (int3(GBufferB >> 24, GBufferB >> 16, GBufferB >> 8) & 0xFF) / float(0xFF);
    GBufferData.Reflactance = (GBufferB >> 32 & 0xFF) / float(0xFF);
}

#endif