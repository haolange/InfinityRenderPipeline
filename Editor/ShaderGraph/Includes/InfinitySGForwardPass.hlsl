#ifndef INFINITY_SG_FORWARD_PASS
#define INFINITY_SG_FORWARD_PASS
#include "Packages/com.infinity.render-pipeline/Shaders/Library/ForwardOutput.hlsl"
// ForwardPass of InfinityLit (Forward surface route): shared light loop, baked / probe / sky diffuse, reflection probes
// blended with the sky, forward AO.
ForwardFragmentOutput frag(PackedVaryings packedInput)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    InfinitySurface surface = EvaluateInfinitySurface(input);

    float3 positionWS = input.positionWS;
    float3 normalWS = InfinitySurfaceNormalWS(input, surface.normalTS);
    float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
    MicrofaceContext microface = InitMicrofaceContext(surface.specularLevel, surface.roughness, surface.metallic, surface.baseColor);

    float2 lightmapUV = input.texCoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
    float4 baked, mask;
    SampleBakedLighting(lightmapUV, positionWS, normalWS, viewDir, input.positionCS.xy, asuint(unity_RenderingLayer.x), baked, mask);
    float viewDepth = -mul(unity_MatrixV, float4(positionWS, 1)).z;
    float3 diffuse = microface.AlbedoColor * (baked.a > 0 ? baked.rgb : EvaluateSH2Irradiance(normalWS, _AtmosphereSkySH));
    float3 reflectionWS = reflect(-viewDir, normalWS);
    float3 sky = SampleGGXCubemapArray(_AtmosphereGGXPrefilter, reflectionWS, microface.RoughnessClamp, _AtmosphereIBLMaxMip);
    float3 specular = BlendReflectionProbesWithSky(positionWS, reflectionWS, microface.RoughnessClamp, (uint2)input.positionCS.xy, viewDepth, sky)
        * EnvBRDFApprox(microface.SpecularColor, microface.RoughnessClamp, saturate(dot(normalWS, viewDir))).rgb;
#if defined(INFINITY_FORWARD_AO)
    float ao = SRV_ForwardOcclusion.Load(int3(input.positionCS.xy, 0));
    diffuse *= ao;
    specular *= ao;
#endif
#if defined(INFINITY_FORWARD_ANALYTIC_SHADOW)
    float analyticIndirectAO = EvaluateAnalyticIndirectOcclusion((uint2)input.positionCS.xy, viewDepth, positionWS, normalWS, asuint(unity_RenderingLayer.x));
    diffuse *= analyticIndirectAO;
    specular *= analyticIndirectAO;
#endif
    ForwardFragmentOutput output = (ForwardFragmentOutput)0;
#if defined(INFINITY_FORWARD_INDIRECT_DIFFUSE)
    output.indirectDiffuse = float4(diffuse, 1);
#endif
#if defined(INFINITY_FORWARD_INDIRECT_SPECULAR)
    output.indirectSpecular = float4(specular, 1);
#endif
    float3 directDiffuse;
    float3 totalLighting = EvaluateSurfaceLights(positionWS, normalWS, viewDir, microface, mask, viewDepth, asuint(unity_RenderingLayer.x), (uint2)input.positionCS.xy, directDiffuse)
        + diffuse + specular + surface.emission;
#if defined(INFINITY_SSS_ALPHA)
    float diffuseLuma = _Subsurface > 0.5 ? Luminance(max(0, directDiffuse + diffuse)) : 0.0;
    output.lightingBuffer = float4(totalLighting, diffuseLuma);
#else
    output.lightingBuffer = float4(totalLighting, 1);
#endif
#if defined(INFINITY_SSS_OUTPUT)
    output.scatterDiffuse = float4(directDiffuse + diffuse, 1);
#endif
    return output;
}
#endif
