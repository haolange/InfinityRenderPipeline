#ifndef _PackData_
#define _PackData_

#include "../Private/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

// PackingData
float3 Pack1212To888(float2 x)
{
	// Pack 12:12 to 8:8:8
#if 1
	uint2 x1212 = (uint2)( x * 4095.0 );
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
#if 1
	uint3 x888 = (uint3)( x * 255.0 );
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

// CoordSpace
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

// CompressionNormal
float3 EncodeNormalDir_Octa24(float3 N)
{
	return Pack1212To888(saturate(UnitVectorToOctahedron(N) * 0.5 + 0.5));
}

float3 DecodeNormalDir_Octa24(float3 N)
{
	return OctahedronToUnitVector(Pack888To1212(N) * 2 - 1);
}

// CompressionReflectance&Specular
uint2 EncodeReflectanceSpecular(float Reflectance, float Specular)
{
        uint2 ReflectanceSpecular = 0;
        ReflectanceSpecular.x = (uint)(Reflectance * 255) * 4;
        
        float Specular4Bit = floor(Specular * 15);
        ReflectanceSpecular.x += Specular4Bit / 4;
        ReflectanceSpecular.y = Specular4Bit % 4;
        return ReflectanceSpecular;
}

void DecodeReflectanceSpecular(uint2 ReflectanceSpecular, out float Reflectance, out float Specular)
{
        Reflectance = float(ReflectanceSpecular.x * 0.25) / 255;        
        uint Specular4Bit = ReflectanceSpecular.x % 4;
        Specular4Bit = (Specular4Bit * 4) + ReflectanceSpecular.y;        
        Specular = Specular4Bit / 15.0f;
}

// Compression GBuffer
struct ThinGBufferData
{
    float Specular;
    float Roughness;
    float Reflactance;
	float3 WorldNormal;
	float3 BaseColor;
};

void EncodeGBuffer(ThinGBufferData GBufferData, out float4 EncodeData_GBufferA, out uint4 EncodeData_GBufferB)
{
    uint2 EncodeNormal = floor(UnitVectorToOctahedron(GBufferData.WorldNormal) * 511 + 512);

    EncodeData_GBufferA = float4(GBufferData.BaseColor, GBufferData.Roughness);
    EncodeData_GBufferB = uint4(EncodeNormal, EncodeReflectanceSpecular(GBufferData.Reflactance, GBufferData.Specular));
}

void DecodeGBuffer(float4 EncodeData_GBufferA, float4 EncodeData_GBufferB, out ThinGBufferData GBufferData)
{
    GBufferData.BaseColor = EncodeData_GBufferA.rgb;
    GBufferData.Roughness = EncodeData_GBufferA.a;

    GBufferData.WorldNormal = float3(OctahedronToUnitVector(((EncodeData_GBufferB.xy / 1023) - 0.5) * 2));
    DecodeReflectanceSpecular(EncodeData_GBufferB.zw, GBufferData.Reflactance, GBufferData.Specular);
}

void EncodeGBuffer_Normal24(ThinGBufferData GBufferData, out float4 EncodeData_GBufferA, out float4 EncodeData_GBufferB)
{
    float3 EncodeNormal = EncodeNormalDir_Octa24(GBufferData.WorldNormal);
    float EncodeRoughness = GBufferData.Roughness;
    float EncodeReflactance = GBufferData.Reflactance;
    float3 EncodeAlbedo = GBufferData.BaseColor;
                    
    EncodeData_GBufferA = float4(EncodeAlbedo, EncodeRoughness);
    EncodeData_GBufferB = float4(EncodeNormal, EncodeReflactance);
}

void DecodeGBuffer_Normal24(float4 EncodeData_GBufferA, float4 EncodeData_GBufferB, out ThinGBufferData GBufferData)
{
    GBufferData.WorldNormal = DecodeNormalDir_Octa24(EncodeData_GBufferB.xyz);
    GBufferData.Roughness = EncodeData_GBufferA.a;
    GBufferData.Specular = 0.5;
    GBufferData.Reflactance = EncodeData_GBufferB.a;
    GBufferData.BaseColor = EncodeData_GBufferB.rgb;
}

void EncodeGBuffer_RayTrace(ThinGBufferData GBufferData, out int EncodeData_GBufferA, out int EncodeData_GBufferB)
{
    int2 EncodeNormal = int2(saturate( UnitVectorToOctahedron(GBufferData.WorldNormal) * 0.5 + 0.5) * 0xFFF);
    int EncodeRoughness = int(saturate(GBufferData.Roughness) * 0xFF);
    int3 EncodeAlbedo = int3(saturate(GBufferData.BaseColor) * 0xFF);
    int EncodeReflactance = int(saturate(GBufferData.Reflactance) * 0xFF);
                    
    EncodeData_GBufferA = (EncodeNormal.x << 20) + (EncodeNormal.y << 8) + EncodeRoughness;
    EncodeData_GBufferB = (EncodeAlbedo.x << 24) + (EncodeAlbedo.y << 16) + (EncodeAlbedo.z << 8) + EncodeReflactance;
}

void DecodeGBuffer_RayTrace(int EncodeData_GBufferA, int EncodeData_GBufferB, out ThinGBufferData GBufferData)
{
    GBufferData.WorldNormal = OctahedronToUnitVector( (int2(EncodeData_GBufferA >> 20, EncodeData_GBufferA >> 8) & 0xFFF) / float(0xFFF)  * 2 - 1);
    GBufferData.Specular = 0.5;
    GBufferData.Roughness = ((EncodeData_GBufferA >> 32) & 0xFF) / 255.0f;
    GBufferData.BaseColor = (int3(EncodeData_GBufferB >> 24, EncodeData_GBufferB >> 16, EncodeData_GBufferB >> 8) & 0xFF) / 255.0f;
    GBufferData.Reflactance = (EncodeData_GBufferB >> 24 & 0xFF) / 255.0f;
}

#endif