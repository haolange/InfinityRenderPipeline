#ifndef _LightmapInclude
#define _LightmapInclude

#include "ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"


TEXTURE2D(unity_Lightmap); SAMPLER(samplerunity_Lightmap);
TEXTURE2D(unity_LightmapInd);
TEXTURE2D(unity_ShadowMask); SAMPLER(samplerunity_ShadowMask);
// Light Probe Proxy Volume of the renderer (PerObjectData.LightProbeProxyVolume; unity_ProbeVolumeParams.x == 1).
TEXTURE3D_FLOAT(unity_ProbeVolumeSH); SAMPLER(samplerunity_ProbeVolumeSH);
int _InfinityHasProbes;
// Adaptive Probe Volumes hold data for this camera (ProbeVolumeIntegration.SetupCamera).
int _EnableProbeVolumes;

#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
#endif


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

// Adaptive Probe Volumes, when the project uses them and this camera has data. Returns false otherwise.
bool SampleAdaptiveProbeVolume(float3 positionWS, float3 normalWS, float3 viewDir, float2 positionSS, uint renderingLayer, out float3 diffuse, out float4 occlusion)
{
    bool sampled = false;
    diffuse = 0;
    occlusion = 1;
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    if (_EnableProbeVolumes != 0)
    {
        EvaluateAdaptiveProbeVolume(positionWS, normalWS, viewDir, positionSS, renderingLayer, diffuse, occlusion);
        sampled = true;
    }
#endif
    return sampled;
}

// Probe lighting of a renderer without a lightmap: Adaptive Probe Volumes when enabled, else the renderer's Light Probe
// Proxy Volume, else its interpolated light probe SH. Returns false when no probe data exists (the sky SH applies).
bool SampleRendererProbes(float3 positionWS, float3 normalWS, float3 viewDir, float2 positionSS, uint renderingLayer, out float3 diffuse, out float4 occlusion)
{
    float3 probeDiffuse = 0;
    float4 probeOcclusion = 1;
    bool sampled = SampleAdaptiveProbeVolume(positionWS, normalWS, viewDir, positionSS, renderingLayer, probeDiffuse, probeOcclusion);
    if (!sampled)
    {
        if (unity_ProbeVolumeParams.x == 1.0)
        {
            probeDiffuse = max(0, SampleProbeVolumeSH4(TEXTURE3D_ARGS(unity_ProbeVolumeSH, samplerunity_ProbeVolumeSH), positionWS, normalWS,
                unity_ProbeVolumeWorldToObject, unity_ProbeVolumeParams.y, unity_ProbeVolumeParams.z, unity_ProbeVolumeMin.xyz, unity_ProbeVolumeSizeInv.xyz));
            probeOcclusion = SampleProbeOcclusion(TEXTURE3D_ARGS(unity_ProbeVolumeSH, samplerunity_ProbeVolumeSH), positionWS,
                unity_ProbeVolumeWorldToObject, unity_ProbeVolumeParams.y, unity_ProbeVolumeParams.z, unity_ProbeVolumeMin.xyz, unity_ProbeVolumeSizeInv.xyz);
            sampled = true;
        }
        else if (_InfinityHasProbes != 0)
        {
            probeDiffuse = SampleSH(normalWS);
            probeOcclusion = unity_ProbesOcclusion;
            sampled = true;
        }
    }
    diffuse = probeDiffuse;
    occlusion = probeOcclusion;
    return sampled;
}

// RGB is scene-linear baked/probe diffuse; alpha = 1 when a baked source exists (0 lets the sky SH apply).
// Priority: lightmap -> Adaptive Probe Volumes -> Light Probe Proxy Volume -> light probe SH.
void SampleBakedLighting(float2 uv, float3 positionWS, float3 normalWS, float3 viewDir, float2 positionSS, uint renderingLayer, out float4 diffuse, out float4 mask)
{
    mask = 1;
#if defined(LIGHTMAP_ON)
    diffuse = float4(SampleLightmap(uv, normalWS), 1);
#if defined(SHADOWS_SHADOWMASK)
    mask = unity_ShadowMask.Sample(samplerunity_ShadowMask, uv);
#endif
#else
    float3 probe;
    bool hasProbe = SampleRendererProbes(positionWS, normalWS, viewDir, positionSS, renderingLayer, probe, mask);
    diffuse = float4(probe, hasProbe ? 1 : 0);
#endif
}

#endif
