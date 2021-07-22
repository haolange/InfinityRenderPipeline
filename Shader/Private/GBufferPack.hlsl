#ifndef _PackDataInclude
#define _PackDataInclude

#include "Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

Texture2D g_NormalScaleTable;

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
    float3 BaseColor;
	float3 WorldNormal;
};

/*float3 EncodeNormalBestFit(float3 n)
{
    float3 nU = abs(n);
    float maxNAbs = max(nU.z, max(nU.x, nU.y));
    float2 TC = nU.z<maxNAbs? (nU.y<maxNAbs? nU.yz : nU.xz) : nU.xy;
    TC = TC.x<TC.y? TC.yx : TC.xy;
    TC.y /= TC.x;

    n /= maxNAbs;
    n *= g_NormalScaleTable.Load(int3(TC.x * 1023, TC.y * 1023, 0)).a;
    return n * 0.5 + 0.5;
}*/

float3 EncodeNormalBestFit(float3 N)
{
    float3 uN = abs(N);
    float maxNAbs = max(uN.z, max(uN.x, uN.y));
    float2 texcoord = uN.z < maxNAbs ? (uN.y < maxNAbs ? uN.yz : uN.xz) : uN.xy;
    texcoord = texcoord.x < texcoord.y ? texcoord.yx : texcoord.xy;
    texcoord.y /= texcoord.x;
    N /= maxNAbs;
    N *= g_NormalScaleTable.Sample(Global_point_clamp_sampler, texcoord).a;
    return N * 0.5 + 0.5;
}

void EncodeGBuffer(FGBufferData GBufferData, out float4 GBufferA, out float4 GBufferB, out float4 GBufferC)
{
    GBufferA = float4(GBufferData.BaseColor, 1);
    GBufferC = float4(GBufferData.Roughness, GBufferData.Reflactance, 1, 1);
    GBufferB = float4(EncodeNormalBestFit(GBufferData.WorldNormal), GBufferData.Specular);
}

void DecodeGBuffer(float4 GBufferA, float4 GBufferB, float4 GBufferC, out FGBufferData GBufferData)
{
    GBufferData.Specular = GBufferB.a;
    GBufferData.Roughness = GBufferC.r;
    GBufferData.BaseColor = GBufferA.rgb;
    GBufferData.Reflactance = GBufferC.g;
    GBufferData.WorldNormal = normalize(GBufferB.xyz * 2 - 1);
}

uint2 EncodeMetallicSpecular(float Metallic, float Specular)
{
    uint2 MetallicSpecular = 0;
    float Specular4Bit = floor(Specular * 15);

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

void EncodeGBuffer_Normal20(FGBufferData GBufferData, out float4 PackedGBufferA, out uint4 PackedGBufferB)
{
    uint2 EncodeNormal = floor(UnitVectorToOctahedron(GBufferData.WorldNormal) * 511 + 512);
    PackedGBufferA = float4(GBufferData.BaseColor, GBufferData.Roughness);
    PackedGBufferB = uint4(EncodeNormal, EncodeMetallicSpecular(GBufferData.Reflactance, GBufferData.Specular));
}

void DecodeGBuffer_Normal20(float4 PackedGBufferA, float4 PackedGBufferB, out FGBufferData GBufferData)
{
    GBufferData.Roughness = PackedGBufferA.a;
    GBufferData.BaseColor = PackedGBufferA.rgb;
    GBufferData.WorldNormal = float3(OctahedronToUnitVector(((PackedGBufferB.xy / 1023) - 0.5) * 2));
    DecodeMetallicSpecular(PackedGBufferB.zw, GBufferData.Reflactance, GBufferData.Specular);
}

float3 EncodeNormalDir_Octa24(float3 N)
{
	return Pack1212To888(saturate(UnitVectorToOctahedron(N) * 0.5 + 0.5));
}

float3 DecodeNormalDir_Octa24(float3 N)
{
	return OctahedronToUnitVector(Pack888To1212(N) * 2 - 1);
}

void EncodeGBuffer_Normal24(FGBufferData GBufferData, out float4 PackedGBufferA, out float4 PackedGBufferB)
{
    float3 PackedNormal = EncodeNormalDir_Octa24(GBufferData.WorldNormal);              
    PackedGBufferA = float4(GBufferData.BaseColor, GBufferData.Roughness);
    PackedGBufferB = float4(PackedNormal, GBufferData.Reflactance);
}

void DecodeGBuffer_Normal24(float4 PackedGBufferA, float4 PackedGBufferB, out FGBufferData GBufferData)
{
    GBufferData.WorldNormal = DecodeNormalDir_Octa24(PackedGBufferB.xyz);
    GBufferData.Roughness = PackedGBufferA.a;
    GBufferData.Specular = 0.5;
    GBufferData.Reflactance = PackedGBufferB.a;
    GBufferData.BaseColor = PackedGBufferB.rgb;
}

void EncodeGBuffer_RayTrace(FGBufferData GBufferData, out int PackedGBufferA, out int PackedGBufferB)
{
    int2 EncodeNormal = int2(saturate( UnitVectorToOctahedron(GBufferData.WorldNormal) * 0.5 + 0.5) * 0xFFF);
    int EncodeRoughness = int(saturate(GBufferData.Roughness) * 0xFF);
    int3 EncodeAlbedo = int3(saturate(GBufferData.BaseColor) * 0xFF);
    int EncodeReflactance = int(saturate(GBufferData.Reflactance) * 0xFF);
                    
    PackedGBufferA = (EncodeNormal.x << 20) + (EncodeNormal.y << 8) + EncodeRoughness;
    PackedGBufferB = (EncodeAlbedo.x << 24) + (EncodeAlbedo.y << 16) + (EncodeAlbedo.z << 8) + EncodeReflactance;
}

void DecodeGBuffer_RayTrace(int PackedGBufferA, int PackedGBufferB, out FGBufferData GBufferData)
{
    GBufferData.WorldNormal = OctahedronToUnitVector( (int2(PackedGBufferA >> 20, PackedGBufferA >> 8) & 0xFFF) / float(0xFFF)  * 2 - 1);
    GBufferData.Specular = 0.5;
    GBufferData.Roughness = ((PackedGBufferA >> 32) & 0xFF) / 255.0f;
    GBufferData.BaseColor = (int3(PackedGBufferB >> 24, PackedGBufferB >> 16, PackedGBufferB >> 8) & 0xFF) / 255.0f;
    GBufferData.Reflactance = (PackedGBufferB >> 24 & 0xFF) / 255.0f;
}

#endif