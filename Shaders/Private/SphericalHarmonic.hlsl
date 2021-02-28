#ifndef _SphericalHarmonic_
#define _SphericalHarmonic_

//#include "Common.hlsl"
//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

struct SH1Table
{
	float Coefficients[4];
};

SH1Table InitSH1Table(float3 Direction)
{
	SH1Table SHTable;

    SHTable.Coefficients[0] = 0.2820947917f;
    SHTable.Coefficients[1] = 0.4886025119f * Direction.y;
    SHTable.Coefficients[2] = 0.4886025119f * Direction.z;
    SHTable.Coefficients[3] = 0.4886025119f * Direction.x;

    return SHTable;
}

struct SH2Table
{
	float Coefficients[9];
};

SH2Table InitSH2Table(float3 Direction)
{
	SH2Table SHTable;

    SHTable.Coefficients[0] = 0.2820947917f;
    SHTable.Coefficients[1] = 0.4886025119f * Direction.y;
    SHTable.Coefficients[2] = 0.4886025119f * Direction.z;
    SHTable.Coefficients[3] = 0.4886025119f * Direction.x;
    SHTable.Coefficients[4] = 1.0925484306f * Direction.x * Direction.y;
    SHTable.Coefficients[5] = 1.0925484306f * Direction.y * Direction.z;
    SHTable.Coefficients[6] = 0.3153915652f * (3 * Direction.z * Direction.z - 1);
    SHTable.Coefficients[7] = 1.0925484306f * Direction.x * Direction.z;
    SHTable.Coefficients[8] = 0.5462742153f * (Direction.x * Direction.x - Direction.y * Direction.y);

    return SHTable;
}

struct SH1Basis
{
	float3 Basis[4];
};

SH1Basis InitSH1Basis(SH1Table SHTable, float3 Radiance)
{
	SH1Basis SHBasis;
    
    SHBasis.Basis[0] = SHTable.Coefficients[0] * Radiance;
    SHBasis.Basis[1] = SHTable.Coefficients[1] * Radiance;
    SHBasis.Basis[2] = SHTable.Coefficients[2] * Radiance;
    SHBasis.Basis[3] = SHTable.Coefficients[3] * Radiance;

    return SHBasis;
}

struct SH2Basis
{
	float3 Basis[9];
};

SH2Basis InitSH2Basis(SH2Table SHTable, float3 Radiance)
{
	SH2Basis SHBasis;
    
    SHBasis.Basis[0] = SHTable.Coefficients[0] * Radiance;
    SHBasis.Basis[1] = SHTable.Coefficients[1] * Radiance;
    SHBasis.Basis[2] = SHTable.Coefficients[2] * Radiance;
    SHBasis.Basis[3] = SHTable.Coefficients[3] * Radiance;
    SHBasis.Basis[4] = SHTable.Coefficients[4] * Radiance;
    SHBasis.Basis[5] = SHTable.Coefficients[5] * Radiance;
    SHBasis.Basis[6] = SHTable.Coefficients[6] * Radiance;
    SHBasis.Basis[7] = SHTable.Coefficients[7] * Radiance;
    SHBasis.Basis[8] = SHTable.Coefficients[8] * Radiance;

    return SHBasis;
}

#endif