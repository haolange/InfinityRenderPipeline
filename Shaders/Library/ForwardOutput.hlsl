// Forward output

#ifndef INFINITY_FORWARD_OUTPUT_INCLUDED
#define INFINITY_FORWARD_OUTPUT_INCLUDED

#include "Sideband.hlsl"
#include "IndirectComposition.hlsl"
#if defined(INFINITY_FORWARD_ANALYTIC_SHADOW)
#include "AnalyticShadow.hlsl"
#endif

// RasterForwardPass writes the whole shading to Scene Color with blending off. With a sideband it also writes the diffuse
// share; the pass masks that target to R, so G, B and A keep what the geometry draw wrote.
struct ForwardFragmentOutput
{
    float4 sceneColor : SV_Target0;
#if defined(INFINITY_SIDEBAND)
    float4 sideband : SV_Target1;
#endif
};

#if defined(INFINITY_FORWARD_AO)
Texture2D<float> SRV_ForwardOcclusion;
#endif
#if defined(INFINITY_FORWARD_SSGI)
// Premultiplied indirect irradiance, alpha = confidence.
Texture2D<float4> SRV_SSGITexture;
Texture2D<float> SRV_SSGIConfidenceTexture;
#endif
#if defined(INFINITY_FORWARD_SSR)
// Premultiplied reflected radiance, alpha = edge fade.
#include "ScreenSpaceRadiance.hlsl"
Texture2DArray<float2> SRV_SSRTexture;
#endif

// Indirect occlusion of a forward pixel: screen-space occlusion times analytic indirect occlusion. It scales indirect diffuse
// and environment specular, never emission or direct light.
float ForwardIndirectOcclusion(uint2 positionSS, float viewDepth, float3 positionWS, float3 normalWS, uint lightChannels)
{
    float occlusion = 1;
#if defined(INFINITY_FORWARD_AO)
    occlusion = SRV_ForwardOcclusion.Load(int3(positionSS, 0));
#endif
#if defined(INFINITY_FORWARD_ANALYTIC_SHADOW)
    occlusion *= EvaluateAnalyticIndirectOcclusion(positionSS, viewDepth, positionWS, normalWS, lightChannels);
#endif
    return occlusion;
}

// Occluded indirect diffuse, replaced by SSGI the same way deferred shading replaces it.
float3 ForwardIndirectDiffuse(uint2 positionSS, float3 diffuseColor, float3 irradiance, float occlusion)
{
    float3 indirectDiffuse = diffuseColor * irradiance * occlusion;
#if defined(INFINITY_FORWARD_SSGI)
    float4 ssgi = float4(SRV_SSGITexture.Load(int3(positionSS, 0)).rgb,
        SRV_SSGIConfidenceTexture.Load(int3(positionSS, 0)));
    indirectDiffuse = ReplaceIndirectDiffuse(indirectDiffuse, diffuseColor, ssgi);
#endif
    return indirectDiffuse;
}

// Occluded environment specular, replaced by SSR the same way deferred shading replaces it.
float3 ForwardEnvironmentSpecular(uint2 positionSS, float3 environment, float3 environmentBRDF, float occlusion)
{
    float3 environmentSpecular = environment * environmentBRDF * occlusion;
#if defined(INFINITY_FORWARD_SSR)
    environmentSpecular = ReplaceEnvironmentSpecular(environmentSpecular, environmentBRDF, LoadScreenSpaceRadiance(SRV_SSRTexture, positionSS, 0));
#endif
    return environmentSpecular;
}

// The complete forward shading and, with a sideband, its diffuse share.
ForwardFragmentOutput WriteForwardColor(float3 color, float3 diffuse)
{
    ForwardFragmentOutput output;
    color = max(color, 0);
    output.sceneColor = float4(color, 1);
#if defined(INFINITY_SIDEBAND)
    output.sideband = float4(DiffuseRatio(diffuse, color), 0, 0, 0);
#endif
    return output;
}

#endif
