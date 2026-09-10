#ifndef _LightmapInclude
#define _LightmapInclude

#include "ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"


TEXTURE2D(unity_Lightmap); SAMPLER(samplerunity_Lightmap);
TEXTURE2D(unity_LightmapInd);
TEXTURE2D(unity_ShadowMask); SAMPLER(samplerunity_ShadowMask);
int _InfinityHasProbes;


half3 SampleSH(half3 normalWS)
{
    real4 SHCoefficients[7];
    SHCoefficients[0] = unity_SHAr;
    SHCoefficients[1] = unity_SHAg;
    SHCoefficients[2] = unity_SHAb;
    SHCoefficients[3] = unity_SHBr;
    SHCoefficients[4] = unity_SHBg;
    SHCoefficients[5] = unity_SHBb;
    SHCoefficients[6] = unity_SHC;

    return max(half3(0, 0, 0), SampleSH9(SHCoefficients, normalWS));
}

// SH Vertex Evaluation. Depending on target SH sampling might be
// done completely per vertex or mixed with L2 term per vertex and L0, L1
// per pixel. See SampleSHPixel
half3 SampleSHVertex(half3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return max(half3(0, 0, 0), SampleSH(normalWS));
#elif defined(EVALUATE_SH_MIXED)
    // no max since this is only L2 contribution
    return SHEvalLinearL2(normalWS, unity_SHBr, unity_SHBg, unity_SHBb, unity_SHC);
#endif

    // Fully per-pixel. Nothing to compute.
    return half3(0.0, 0.0, 0.0);
}

half3 SampleSHPixel(half3 L2Term, half3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return L2Term;
#elif defined(EVALUATE_SH_MIXED)
    half3 L0L1Term = SHEvalLinearL0L1(normalWS, unity_SHAr, unity_SHAg, unity_SHAb);
    return max(half3(0, 0, 0), L2Term + L0L1Term);
#endif

    // Default: Evaluate SH fully per-pixel
    return SampleSH(normalWS);
}

half3 SampleLightmap(float2 lightmapUV, half3 normalWS)
{
    // Native vertex stage already applies the renderer UV scale and offset.
    half4 transformCoords = half4(1, 1, 0, 0);

#ifdef DIRLIGHTMAP_COMBINED
    return SampleDirectionalLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap),
        TEXTURE2D_ARGS(unity_LightmapInd, samplerunity_Lightmap),
        lightmapUV, transformCoords, normalWS, true);
#elif defined(LIGHTMAP_ON)
    return SampleSingleLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap), lightmapUV, transformCoords, true);
#else
    return half3(0.0, 0.0, 0.0);
#endif
}

// RGB is scene-linear baked/probe diffuse; alpha identifies an authored GI source.
void SampleBakedLighting(float2 uv, float3 normalWS, out float4 diffuse, out float4 mask)
{
    mask = 1;
#if defined(LIGHTMAP_ON)
    diffuse = float4(SampleLightmap(uv, normalWS), 1);
#if defined(SHADOWS_SHADOWMASK)
    mask = unity_ShadowMask.Sample(samplerunity_ShadowMask, uv);
#endif
#else
    diffuse = float4(_InfinityHasProbes != 0 ? SampleSH(normalWS) : 0, _InfinityHasProbes != 0 ? 1 : 0);
    if (_InfinityHasProbes != 0) mask = unity_ProbesOcclusion;
#endif
}

#endif
