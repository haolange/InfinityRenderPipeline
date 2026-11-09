#ifndef _SphericalHarmonicInclude
#define _SphericalHarmonicInclude

//#include "Common.hlsl"
//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

struct SphericalHarmonicOneTable
{
	float coefficients[4];
};

SphericalHarmonicOneTable InitSH1Table(float3 direction)
{
	SphericalHarmonicOneTable shTable;
    shTable.coefficients[0] = 0.2820947917f;
    shTable.coefficients[1] = 0.4886025119f * direction.y;
    shTable.coefficients[2] = 0.4886025119f * direction.z;
    shTable.coefficients[3] = 0.4886025119f * direction.x;
    return shTable;
}

struct SphericalHarmonicTwoTable
{
	float coefficients[9];
};

SphericalHarmonicTwoTable InitSH2Table(float3 direction)
{
	SphericalHarmonicTwoTable shTable;
    shTable.coefficients[0] = 0.2820947917f;
    shTable.coefficients[1] = 0.4886025119f * direction.y;
    shTable.coefficients[2] = 0.4886025119f * direction.z;
    shTable.coefficients[3] = 0.4886025119f * direction.x;
    shTable.coefficients[4] = 1.0925484306f * direction.x * direction.y;
    shTable.coefficients[5] = 1.0925484306f * direction.y * direction.z;
    shTable.coefficients[6] = 0.3153915652f * (3 * direction.z * direction.z - 1);
    shTable.coefficients[7] = 1.0925484306f * direction.x * direction.z;
    shTable.coefficients[8] = 0.5462742153f * (direction.x * direction.x - direction.y * direction.y);
    return shTable;
}

struct SphericalHarmonicOneBasis
{
	float3 basis[4];
};

SphericalHarmonicOneBasis InitSH1Basis(SphericalHarmonicOneTable shTable, float3 radiance)
{
	SphericalHarmonicOneBasis shBasis;
    shBasis.basis[0] = shTable.coefficients[0] * radiance;
    shBasis.basis[1] = shTable.coefficients[1] * radiance;
    shBasis.basis[2] = shTable.coefficients[2] * radiance;
    shBasis.basis[3] = shTable.coefficients[3] * radiance;
    return shBasis;
}

struct SphericalHarmonicTwoBasis
{
	float3 basis[9];
};

SphericalHarmonicTwoBasis InitSH2Basis(SphericalHarmonicTwoTable shTable, float3 radiance)
{
	SphericalHarmonicTwoBasis shBasis;
    shBasis.basis[0] = shTable.coefficients[0] * radiance;
    shBasis.basis[1] = shTable.coefficients[1] * radiance;
    shBasis.basis[2] = shTable.coefficients[2] * radiance;
    shBasis.basis[3] = shTable.coefficients[3] * radiance;
    shBasis.basis[4] = shTable.coefficients[4] * radiance;
    shBasis.basis[5] = shTable.coefficients[5] * radiance;
    shBasis.basis[6] = shTable.coefficients[6] * radiance;
    shBasis.basis[7] = shTable.coefficients[7] * radiance;
    shBasis.basis[8] = shTable.coefficients[8] * radiance;
    return shBasis;
}

float3 EvaluateSH2(float3 direction, StructuredBuffer<float4> coefficients)
{
    SphericalHarmonicTwoTable shTable = InitSH2Table(direction);
    float3 result = 0;
    [unroll]
    for (int i = 0; i < 9; ++i)
    {
        result += shTable.coefficients[i] * coefficients[i].rgb;
    }
    return max(result, 0);
}

float3 EvaluateSH2Irradiance(float3 normal, StructuredBuffer<float4> coefficients)
{
    return EvaluateSH2(normal, coefficients);
}

#endif