#ifndef _SphericalHarmonicInclude
#define _SphericalHarmonicInclude

//#include "Common.hlsl"
//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

struct FSphericalHarmonicOneTable
{
	float coefficients[4];
};

FSphericalHarmonicOneTable InitSH1Table(float3 direction)
{
	FSphericalHarmonicOneTable shTable;
    shTable.coefficients[0] = 0.2820947917f;
    shTable.coefficients[1] = 0.4886025119f * direction.y;
    shTable.coefficients[2] = 0.4886025119f * direction.z;
    shTable.coefficients[3] = 0.4886025119f * direction.x;
    return shTable;
}

struct FSphericalHarmonicTwoTable
{
	float coefficients[9];
};

FSphericalHarmonicTwoTable InitSH2Table(float3 direction)
{
	FSphericalHarmonicTwoTable shTable;
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

struct FSphericalHarmonicOneBasis
{
	float3 basis[4];
};

FSphericalHarmonicOneBasis InitSH1Basis(FSphericalHarmonicOneTable shTable, float3 radiance)
{
	FSphericalHarmonicOneBasis shBasis;
    shBasis.basis[0] = shTable.coefficients[0] * radiance;
    shBasis.basis[1] = shTable.coefficients[1] * radiance;
    shBasis.basis[2] = shTable.coefficients[2] * radiance;
    shBasis.basis[3] = shTable.coefficients[3] * radiance;
    return shBasis;
}

struct FSphericalHarmonicTwoBasis
{
	float3 basis[9];
};

FSphericalHarmonicTwoBasis InitSH2Basis(FSphericalHarmonicTwoTable shTable, float3 radiance)
{
	FSphericalHarmonicTwoBasis shBasis;
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

#endif