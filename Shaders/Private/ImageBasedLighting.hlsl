#ifndef _ImageBasedLighting_
#define _ImageBasedLighting_

#include "BSDF.hlsl"
#include "ShadingModel.hlsl"

//////////////////////////Environment LUT 
float IBL_Defualt_DiffuseIntegrated(float Roughness, float NoV) {
    float r = 0; 
	const uint NumSamples = 1024;

    float3 V = float3(sqrt(1 - NoV * NoV), 0, NoV);

    for (uint i = 0; i < NumSamples; i++) {
		float2 E = Hammersley(i, NumSamples);
        float3 L = CosineSampleHemisphere(E).xyz;
        float3 H = normalize(V + L);

        float NoL = saturate(L.b);
        float LoH = saturate( dot(L, H) );
        if (NoL > 0) {
            r += Diffuse_RenormalizeBurley_NoPi(LoH, NoL, NoV, Roughness);
        }
    }
    return r / NumSamples;
}

float2 IBL_Defualt_SpecularIntegrated(float Roughness, float NoV) {
    float2 r = 0;
    const uint NumSamples = 1024;

	float3 N = float3(0, 0, 1);
    float3 V = float3(sqrt(1 - NoV * NoV), 0, NoV);

    for (uint i = 0; i < NumSamples; i++) {
        float2 E = Hammersley(i, NumSamples); 
        float3 H = ImportanceSampleGGX(E, Roughness).xyz;
        float3 L = 2 * dot(V, H) * H - V;

        float VoH = saturate( dot(V, H) );
        float NoL = saturate(L.z);
        float NoH = saturate(H.z);

        if (NoL > 0) {
            float G = Vis_Schlick(NoL, NoV, Roughness);
            float Gv = G * VoH / (NoH * NoV);
            float Fc = pow(1 - VoH, 5);

            r.x += Gv * (1 - Fc);
            //r.x += Gv;
            r.y += Gv * Fc;
        }
    }
    return r / NumSamples;
}

float2 IBL_Ashikhmin_SpecularIntegrated(float Roughness, float NoV)
{
    Roughness *= Roughness;
    float2 r = 0;
    const uint NumSamples = 1024;

    float3 N = float3(0, 0, 1);
    float3 V = float3(sqrt(1 - NoV * NoV), 0, NoV);

    for (uint i = 0; i < NumSamples; i++)
    {
        float2 E = Hammersley(i, NumSamples);
        float3 L = UniformSampleHemisphere(E).xyz;
        float3 H = normalize(V + L);

        float NoH = saturate( dot(N, H) );
        float VoH = saturate( dot(V, H) );
        float NoL = saturate( dot(N, L) );

        if (NoL > 0)
        {
            //float Gv = 2 * NoL * D_Ashikhmin_NoPi(NoH, Roughness) * Vis_Ashikhmin(NoL, NoV);
            float Gv = 2 * NoL * D_Charlie_NoPi(NoH, Roughness) * Vis_Charlie(NoL, NoV, Roughness);
            float Fc = pow(1 - VoH, 5);

            r.x += Gv;
            r.y += Gv * Fc;
        }
    }
    return r /= NumSamples;
}

float2 IBL_Defualt_SpecularIntegrated_Approx(float Roughness, float NoV) {
    const float4 c0 = float4(-1.0, -0.0275, -0.572,  0.022);
    const float4 c1 = float4( 1.0,  0.0425,  1.040, -0.040);
    float4 r = Roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    return float2(-1.04, 1.04) * a004 + r.zw;
}

float IBL_Defualt_SpecularIntegrated_Approx_Nonmetal(float Roughness, float NoV) {
	const float2 c0 = { -1, -0.0275 };
	const float2 c1 = { 1, 0.0425 };
	float2 r = Roughness * c0 + c1;
	return min( r.x * r.x, exp2( -9.28 * NoV ) ) * r.x + r.y;
}


float2 IBL_Ashikhmin_SpecularIntegrated_Approx(float Roughness, float NoV) {
    const float4 c0 = float4(0.24,  0.93, 0.01, 0.20);
    const float4 c1 = float4(2, -1.30, 0.40, 0.03);

    float s = 1 - NoV;
    float e = s - c0.y;
    float g = c0.x * exp2(-(e * e) / (2 * c0.z)) + s * c0.w;
    float n = Roughness * c1.x + c1.y;
    float r = max(1 - n * n, c1.z) * g;

    return float2(r, r * c1.w);
}

float2 IBL_Charlie_SpecularIntegrated_Approx(float Roughness, float NoV) {
    const float3 c0 = float3(0.95, 1250, 0.0095);
    const float4 c1 = float4(0.04, 0.2, 0.3, 0.2);

    float a = 1 - NoV;
    float b = 1 - (Roughness);

    float n = pow(c1.x + a, 64);
    float e = b - c0.x;
    float g = exp2(-(e * e) * c0.y);
    float f = b + c1.y;
    float a2 = a * a;
    float a3 = a2 * a;
    float c = n * g + c1.z * (a + c1.w) * Roughness + f * f * a3 * a3 * a2;
    float r = min(c, 18);

    return float2(r, r * c0.z);
}

float3 IBL_Hair_FullIntegrated(float3 V, float3 N, float3 SpecularColor, float Roughness, float Scatter)
{
	float3 Lighting = 0;
	uint NumSamples = 32;
	
	[loop]
	for( uint i = 0; i < NumSamples; i++ ) 
    {
        float2 E = Hammersley(i, NumSamples, Halton(i));
        float3 L = UniformSampleSphere(E).rgb;

        float PDF = 1 / (4 * Pi);
        float InvWeight = PDF * NumSamples;
        float Weight = rcp(InvWeight);

        Lighting += HairLit(L, V, N, SpecularColor, 0.5, Roughness, 0, Scatter, 0, 0) * Weight;
	}
	return Lighting;
}

float3 IBL_Hair_FullIntegrated(float3 V, float3 N, float3 SpecularColor, float Roughness, float Scatter, uint2 Random)
{
	float3 Lighting = 0;
	uint NumSamples = 32;

	[loop]
	for( uint i = 0; i < NumSamples; i++ ) 
    {
        float2 E = Hammersley16(i, NumSamples, Random);
        float3 L = UniformSampleSphere(E).rgb;

        float PDF = 1 / (4 * Pi);
        float InvWeight = PDF * NumSamples;
        float Weight = rcp(InvWeight);

        Lighting += HairLit(L, V, N, SpecularColor, 0.5, Roughness, 0, Scatter, 0, 0) * Weight;
	}
	return Lighting;
}

//////////Enviornment BRDF
float4 PreintegratedDGF_LUT(sampler2D PreintegratedLUT, inout float3 EnergyCompensation, float3 SpecularColor, float Roughness, float NoV)
{
    float3 Enviorfilter_GFD = tex2Dlod( PreintegratedLUT, float4(Roughness, NoV, 0, 0) ).rgb;
    float3 ReflectionGF = lerp( saturate(50 * SpecularColor.g) * Enviorfilter_GFD.ggg, Enviorfilter_GFD.rrr, SpecularColor );
    EnergyCompensation = 1 + SpecularColor * (1 / Enviorfilter_GFD.r - 1);
    return float4(ReflectionGF, Enviorfilter_GFD.b);
}

float4 EnvBRDFApprox(float3 SpecularColor, float Roughness, float NoV)
{
	float2 AB = IBL_Defualt_SpecularIntegrated_Approx(Roughness, NoV);
	AB.y *= saturate( 50 * SpecularColor.g );
	return float4(SpecularColor * AB.x + AB.y, 1);
}

float3 PreintegratedGF_ClothAshikhmin(float3 SpecularColor, float Roughness, float NoV)
{
    float2 AB = IBL_Ashikhmin_SpecularIntegrated_Approx(Roughness, NoV);
    return SpecularColor * AB.r + AB.g;
}

float3 PreintegratedGF_ClothCharlie(float3 SpecularColor, float Roughness, float NoV)
{
    float2 AB = IBL_Charlie_SpecularIntegrated_Approx(Roughness, NoV);
    return SpecularColor * AB.r + AB.g;
}

#endif
