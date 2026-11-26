// Indirect composition

#ifndef INFINITY_INDIRECT_COMPOSITION
#define INFINITY_INDIRECT_COMPOSITION

#include "SphericalHarmonic.hlsl"

// Diffuse irradiance a surface receives indirectly: the baked source SampleBakedLighting found (alpha 1), else the dynamic
// sky. The geometry draw and forward shading both read it here, so a dynamic GI source replaces this one function.
float3 IndirectIrradiance(float4 bakedDiffuse, float3 normalWS, StructuredBuffer<float4> skySH)
{
    return bakedDiffuse.a > 0 ? bakedDiffuse.rgb : EvaluateSH2Irradiance(normalWS, skySH);
}

// SSGI replaces the occluded indirect diffuse by its confidence (alpha). Its radiance is premultiplied and already lit, so
// it is not occluded again; only the diffuse color scales it.
float3 ReplaceIndirectDiffuse(float3 indirectDiffuse, float3 diffuseColor, float4 ssgi)
{
    return indirectDiffuse * (1 - saturate(ssgi.a)) + diffuseColor * ssgi.rgb;
}

// SSR replaces the environment specular by its edge fade (alpha). Its radiance is premultiplied; the environment BRDF turns
// it into reflected light.
float3 ReplaceEnvironmentSpecular(float3 environmentSpecular, float3 environmentBRDF, float4 ssr)
{
    return environmentSpecular * (1 - saturate(ssr.a)) + ssr.rgb * environmentBRDF;
}

#endif
