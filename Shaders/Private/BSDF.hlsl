#ifndef _BSDF_
#define _BSDF_

#include "Common.hlsl"
#include "Montcalo.hlsl"


struct BSDFContext
{
	float NoL;
	float NoV;
	float NoH;
	float LoH;
	float VoL;
	float VoH;
};

void Init(inout BSDFContext LightData, float3 N, float3 V, float3 L, float3 H)
{
	LightData.NoL = max(dot(N, L), 0);
	LightData.NoV = max(dot(N, V), 0);
	LightData.NoH = max(dot(N, H), 0);
	LightData.LoH = max(dot(L, H), 0);
	LightData.VoL = max(dot(V, L), 0);
	LightData.VoH = max(dot(V, H), 0);
}

struct AnisoBSDFContext
{
    float ToH;
    float ToL; 
    float ToV; 
    float BoH;
    float BoL;
    float BoV; 
};

void Init_Aniso(inout AnisoBSDFContext LightData, float3 Tangent, float3 Bitangent, float3 H, float3 L, float3 V)
{
    LightData.ToH = dot(Tangent, H);
    LightData.ToL = dot(Tangent, L); 
    LightData.ToV = dot(Tangent, V); 

    LightData.BoH = dot(Bitangent, H);
    LightData.BoL = dot(Bitangent, L);
	LightData.BoV = dot(Bitangent, V);
}



/////////////////////////////////////////////////////////////////Specular F
float IorToFresnel(float transmittedIor, float incidentIor)
{
	return pow2(transmittedIor - incidentIor) / pow2(transmittedIor + incidentIor);
}

float3 IorToFresnel(float3 transmittedIor, float3 incidentIor)
{
	return pow2(transmittedIor - incidentIor) / pow2(transmittedIor + incidentIor);
}

float FresnelToIOR(float fresnel0)
{
	return ( 1 + pow2(fresnel0) ) / ( 1 - pow2(fresnel0) );
}

float3 FresnelToIOR(float3 fresnel0)
{
	return ( 1 + pow2(fresnel0) ) / ( 1 - pow2(fresnel0) );
}

float F_Schlick(float F0, float F90, float HoV)
{
	return F0 + (F90 - F0) * pow5(1 - HoV);
}

float3 F_Schlick(float3 F0, float3 F90, float HoV)
{
	float Fc = pow5( 1 - HoV );				
	return saturate( 50 * F0.g ) * Fc + (1 - Fc) * F0;
}

float3 F_Fresnel(float3 F0, float HoV)
{
	float3 F0Sqrt = sqrt(clamp(float3(0, 0, 0), float3(0.99, 0.99, 0.99), F0));
	float3 n = (1 + F0Sqrt) / (1 - F0Sqrt);
	float3 g = sqrt(n * n + HoV * HoV - 1);
	return 0.5 * Square((g - HoV) / (g + HoV)) * (1 + Square(((g + HoV) * HoV - 1) / ((g - HoV) * HoV + 1)));
}

float F_Hair(float CosTheta) {
	const float n = 1.55;
	const float F0 = Square((1 - n) / (1 + n));
	return F0 + (1 - F0) * pow5(1 - CosTheta);
}



/////////////////////////////////////////////////////////////////Diffuse
float3 Diffuse_Lambert(float3 DiffuseColor)
{
	return DiffuseColor * Inv_Pi;
}

float3 Diffuse_Fabric(float3 DiffuseColor, float3 Roughness)
{
    return Diffuse_Lambert(DiffuseColor) * lerp(1, 0.5, Roughness);
}

float Diffuse_Burley_NoPi(float LoH, float NoL, float NoV, float Roughness)
{
	float F90 = 0.5 + 2 * pow2(LoH) * Roughness;
	float ViewScatter = F_Schlick(1, F90, NoL);
	float LightScatter = F_Schlick(1, F90, NoV);
	return ViewScatter * LightScatter;
}

float3 Diffuse_Burley(float LoH, float NoL, float NoV, float Roughness, float3 DiffuseColor)
{
	return Diffuse_Burley_NoPi(LoH, NoL, NoV, Roughness) * (DiffuseColor * Inv_Pi);
}

float Diffuse_RenormalizeBurley_NoPi(float LoH, float NoL, float NoV, float Roughness)
{
	float EnergyBias = lerp(0, 0.5, Roughness);
	float EnergyFactor = lerp(1, 1 / 0.662, Roughness);

	float F90 = EnergyBias + 2 * pow2(LoH) * Roughness;
	float LightScatter = F_Schlick( 1, F90, NoL );
	float ViewScatter = F_Schlick( 1, F90, NoV );
	return LightScatter * ViewScatter * EnergyFactor;
}

float3 Diffuse_RenormalizeBurley(float LoH, float NoL, float NoV, float Roughness, float3 DiffuseColor)
{
	return Diffuse_RenormalizeBurley_NoPi(LoH, NoL, NoV, Roughness) * (DiffuseColor * Inv_Pi);
}

float Diffuse_OrenNayar_NoPi(float VoH, float NoL, float NoV, float Roughness)
{
	float Roughness4 = pow4(Roughness);
	float VoL = 2 * VoH * VoH - 1;		
	float Cosri = VoL - NoV * NoL;
	float C1 = 1 - 0.5 * Roughness4 / (Roughness4 + 0.33);
	float C2 = 0.45 * Roughness4 / (Roughness4 + 0.09) * Cosri * ( Cosri >= 0 ? rcp( max( NoL, NoV ) ) : 1 );
	return (C1 + C2) * (1 + Roughness * 0.5);
}

float3 Diffuse_OrenNayar(float VoH, float NoL, float NoV, float Roughness, float3 DiffuseColor)
{
	return Diffuse_OrenNayar_NoPi(VoH, NoL, NoV, Roughness) * (DiffuseColor * Inv_Pi);
}



/////////////////////////////////////////////////////////////////Specular D
float D_GGX_NoPi(float NoH, float Roughness)
{
	float Roughness2 = pow2(Roughness);
	float D = (NoH * Roughness - NoH) * NoH + 1;
	return Roughness2 / pow2(D);
}

float D_GGX(float NoH, float Roughness)
{
	return Inv_Pi * D_GGX_NoPi(NoH, Roughness);
}

float D_Beckmann_NoPi(float NoH, float Roughness)
{
	float Roughness2 = pow2(Roughness);
	float NoH2 = pow2(NoH);
	return exp( (NoH2 - 1) / (Roughness2 * NoH2) ) / (Roughness2 * NoH2);
}

float D_Beckmann(float NoH, float Roughness)
{
	return Inv_Pi * D_Beckmann_NoPi(NoH, Roughness);
}

float D_AnisotropyGGX_NoPi(float ToH, float BoH, float NoH, float RoughnessT, float RoughnessB) {
    float D = ToH * ToH / pow2(RoughnessT) + BoH * BoH / pow2(RoughnessB) + pow2(NoH);
    return rcp( RoughnessT * RoughnessB * pow2(D) );
}

float D_AnisotropyGGX(float ToH, float BoH, float NoH, float RoughnessT, float RoughnessB) {
    return Inv_Pi * D_AnisotropyGGX_NoPi(ToH, BoH, NoH, RoughnessT, RoughnessB);
}

float D_InvBlinn_NoPi(float NoH, float Roughness)
{
	float Roughness4 = pow4(Roughness);
	float Cos2h = NoH * NoH;
	float Sin2h = 1 - Cos2h;
	return rcp(5 * Roughness4) * ( 5 * exp(-Cos2h / Roughness4) );
}

float D_InvBlinn(float NoH, float Roughness)
{
	return Inv_Pi * D_InvBlinn_NoPi(NoH, Roughness);
}

float D_InvBeckmann_NoPi(float NoH, float Roughness)
{
	float Roughness4 = pow4(Roughness);
	float Cos2h = NoH * NoH;
	float Sin2h = 1 - Cos2h;
	float Sin4h = Sin2h * Sin2h;
	return rcp( (5 * Roughness4) * Sin4h ) * ( Sin4h + 4 * exp( -Cos2h / (Roughness4 * Sin2h) ) );
}

float D_InvBeckmann(float NoH, float Roughness)
{
	return Inv_Pi * D_InvBeckmann_NoPi(NoH, Roughness);
}

float D_Ashikhmin_NoPi(float NoH, float Roughness) {
	float a2 = pow4(Roughness);
	float d = (NoH - a2 * NoH) * NoH + a2;
	return rcp (1 + 4 * a2) * ( 1 + 4 * a2 * a2 / (d * d) );
}

float D_Ashikhmin(float NoH, float Roughness) {
	return Inv_Pi * D_Ashikhmin_NoPi(NoH, Roughness);
}

float D_Charlie_NoPi(float NoH, float Roughness)
{
    float invR = rcp(Roughness);
    float cos2h = pow2(NoH);
    float sin2h = 1 - cos2h;
    return (2 + invR) * pow(sin2h, invR * 0.5) / 2 ;
}

float D_Charlie(float NoH, float Roughness)
{
    return Inv_Pi * D_Charlie_NoPi(NoH, Roughness);
}



/////////////////////////////////////////////////////////////////Specular V
float Vis_Neumann(float NoL, float NoV)
{
	return rcp( 4 * max(NoL, NoV) );
}

float Vis_Kelemen(float VoH)
{
	return rcp( 4 * VoH * VoH + 1e-5);
}

float Vis_Schlick(float NoL, float NoV, float Roughness)
{
	float k = pow2(Roughness) * 0.5;
	float Vis_SchlickV = NoV / (NoV * (1 - k) + k);
	float Vis_SchlickL = NoL / (NoL * (1 - k) + k);
	return Vis_SchlickV * Vis_SchlickL;
}

float Vis_Smith(float NoL, float NoV, float Roughness)
{
	float a2 = pow4(Roughness);
	float Vis_SmithV = NoV + sqrt( NoV * (NoV - NoV * a2) + a2 );
	float Vis_SmithL = NoL + sqrt( NoL * (NoL - NoL * a2) + a2 );
	return rcp(Vis_SmithV * Vis_SmithL);
}

float Vis_SmithJointApprox_NoPI(float NoL, float NoV, float Roughness)
{
	float a = pow2(Roughness);
	float LambdaL = NoV * (NoL * (1 - a) + a);
	float LambdaV = NoL * (NoV * (1 - a) + a);
	return 0.5 / rcp(LambdaV + LambdaL);
}

float Vis_SmithJointApprox(float NoL, float NoV, float Roughness)
{
	return Inv_Pi * Vis_SmithJointApprox_NoPI(NoL, NoV, Roughness);
}

float Vis_SmithJoint_NoPI(float NoL, float NoV, float Roughness)
{
	float a2 = pow4(Roughness);
	float LambdaV = NoL * sqrt(NoV * (NoV - NoV * a2) + a2);
	float LambdaL = NoV * sqrt(NoL * (NoL - NoL * a2) + a2);
	return 0.5 / rcp(LambdaL + LambdaV);
}

float Vis_SmithJoint(float NoL, float NoV, float Roughness)
{
	return Inv_Pi * Vis_SmithJoint_NoPI(NoL, NoV, Roughness);
}

float Vis_AnisotropyGGX_NoPi(float ToV, float BoV, float NoV, float ToL, float BoL, float NoL, float RoughnessT, float RoughnessB) 
{
	RoughnessT = pow2(RoughnessT);
	RoughnessB = pow2(RoughnessB);

	float LambdaV = NoL * sqrt(RoughnessT * pow2(ToV) + RoughnessB * pow2(BoV) + pow2(NoV));
	float LambdaL = NoV * sqrt(RoughnessT * pow2(ToL) + RoughnessB * pow2(BoL) + pow2(NoL));

    return 0.5 / rcp(LambdaV + LambdaL);
}

float Vis_AnisotropyGGX(float ToV, float BoV, float NoV, float ToL, float BoL, float NoL, float RoughnessT, float RoughnessB) 
{
	return Inv_Pi * Vis_AnisotropyGGX_NoPi(ToV, BoV, NoV, ToL, BoL, NoL, RoughnessT, RoughnessB);
}

float Vis_Ashikhmin(float NoL, float NoV) 
{
	return rcp(4 * (NoL + NoV - NoL * NoV));
}

float Vis_Charlie(float NoL, float NoV, float Roughness)
{
    float lambdaV = NoV < 0.5 ? exp(CharlieL(NoV, Roughness)) : exp(2 * CharlieL(0.5, Roughness) - CharlieL(1 - NoV, Roughness));
    float lambdaL = NoL < 0.5 ? exp(CharlieL(NoL, Roughness)) : exp(2 * CharlieL(0.5, Roughness) - CharlieL(1 - NoL, Roughness));

    return rcp( (1 + lambdaV + lambdaL) * (4 * NoV * NoL) );
}

float Vis_Hair(float B, float Theta) {
	return exp(-0.5 * Square(Theta) / (B * B)) / (sqrt(2 * Pi) * B);
}



/////////////////////////////////////////////////////////////////Utillity
float3 EvalSensitivity(float opd, float shift) {
    float phase = 2 * Pi * opd * 1e-6;
    float3 val = float3(5.4856e-13, 4.4201e-13, 5.2481e-13);
    float3 pos = float3(1.6810e+06, 1.7953e+06, 2.2084e+06);
    float3 var = float3(4.3278e+09, 9.3046e+09, 6.6121e+09);
    float3 xyz = val * sqrt(2 * Pi * var) * cos(pos * phase + shift) * exp(-var * phase * phase);
    xyz.x += 9.7470e-14 * sqrt(2 * Pi * 4.5282e+09) * cos(2.2399e+06 * phase + shift) * exp(-4.5282e+09 * phase * phase);
    return xyz / 1.0685e-7;
}

float3 Flim_Iridescence(float eta_1, float cosTheta1, float iridescenceThickness, float3 baseLayerFresnel0, float iorOverBaseLayer = 0) {
    float Dinc = 3 * iridescenceThickness;
    float eta_2 = lerp(2, 1, iridescenceThickness);
    float sinTheta2 = Square(eta_1 / eta_2) * (1 - Square(cosTheta1));
    float cosTheta2 = sqrt(1 - sinTheta2);

    float R0 = IorToFresnel(eta_2, eta_1); 
	float R12 = F_Schlick(R0, 1, cosTheta1);
    float R21 = R12; float T121 = 1 - R12; 

    float OPD = Dinc * cosTheta2;
	float3 R23 = F_Schlick(baseLayerFresnel0, float3(1, 1, 1), cosTheta2);
	float3 R123 = R12 * R23; float3 r123 = sqrt(R123);
    float3 Rs = Square(T121) * R23 / (1 - R123);
    float3 C0 = R12 + Rs; 
	float3 I = C0;
    float3 Cm = Rs - T121;
	
    [roll]
    for (int m = 1; m <= 2; m++) {
        Cm *= r123;
		float3 Sm = 2 * EvalSensitivity(m * OPD, m * Pi);
		I += Cm * Sm;
    }
    return I;
}

void RefractionSphere(float3 V, float3 positionWS, float3 normalWS, float ior, float thickness, out float dist, out float3 position, out float3 rayWS) {
    float3 R1 = refract(-V, normalWS, 1 / ior);
    float3 C = positionWS - normalWS * thickness * 0.5;

    float NoR1 = dot(normalWS, R1);
    float distance = -NoR1 * thickness;
    float3 P1 = positionWS + R1 * distance;
    float3 N1 = normalize(C - P1);
    float3 R2 = refract(R1, N1, ior);
    float N1oR2 = dot(N1, R2);
    float VoR1 = dot(V, R1);

    dist = distance;
    position = P1;
    rayWS = R2;
}

void RefractionPlane(float3 V, float3 positionWS, float3 normalWS, float ior, float thickness, out float dist, out float3 position, out float3 rayWS) {
    float3 R = refract(-V, normalWS, 1 / ior);
    float distance = thickness / dot(R, -normalWS);

    dist = distance;
    position = positionWS + R * dist;
    rayWS = -V;
}

float3 TransmissionBRDF_Wrap(float3 L, float3 N, float3 W) {
    return saturate( ( dot(L, N) + W ) / ( (1 + W) * (1 + W) ) );
}

float3 TransmissionBRDF_UE4(float3 L, float3 V, float3 N, float3 H, float3 SSS_Color, float3 AO, float3 SSS_Thickness) {
	float3 InScatter = pow(saturate(dot(L, -V)), 12) * lerp(3, 0.1, SSS_Thickness);
	float3 NormalContribution = saturate(dot(N, H) * SSS_Thickness + 1 - SSS_Thickness);
	float3 BackScatter = AO * NormalContribution / (Pi * 2);
	return SSS_Color * lerp(BackScatter, 1, InScatter);
}

float3 TransmissionBRDF_Foliage(float3 SSS_Color, float3 L, float3 V, float3 N)
{
	float Wrap = 0.5;
	float NoL = saturate((dot(-N, L) + Wrap) / Square(1 + Wrap));

	float VoL = saturate(dot(V, -L));
	float a = 0.6;
	float a2 = a * a;
	float d = ( VoL * a2 - VoL ) * VoL + 1;	
	float GGX = (a2 / Pi) / (d * d);
	return NoL * GGX * SSS_Color;
}

float3 TransmissionBRDF_Frostbite(float3 L, float3 V, float3 N, float3 SSS_Color, float AO, float SSS_AmbientIntensity, float SSS_Distortion, float SSS_Power, float SSS_Scale, float SSS_Thickness) {
	float3 newLight = L + N * SSS_Distortion;
	float newNoL = pow(saturate(dot(V, -newLight)), SSS_Power) * SSS_Scale;
	float3 newAtten = (newNoL + (SSS_Color * SSS_AmbientIntensity)) * SSS_Thickness;
	return SSS_Color * newAtten * AO;
}

#endif