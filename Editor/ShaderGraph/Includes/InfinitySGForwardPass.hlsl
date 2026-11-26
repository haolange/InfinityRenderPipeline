#ifndef INFINITY_SG_FORWARD_PASS
#define INFINITY_SG_FORWARD_PASS
#include "Packages/com.infinity.render-pipeline/Shaders/Library/ForwardOutput.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/DBuffer.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/DecalCluster.hlsl"
// ForwardPass of InfinityLit (Forward surface route): the complete shading in one execution. Shared light loop, baked /
// probe / sky indirect diffuse scaled by the material and screen occlusion and replaced by SSGI, reflection probes blended
// with the sky and replaced by SSR, emission.
ForwardFragmentOutput frag(PackedVaryings packedInput)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    InfinitySurface surface = EvaluateInfinitySurface(input);

    float3 positionWS = input.positionWS;
    float3 normalWS = InfinitySurfaceNormalWS(input, surface.normalTS);
    float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
    float3 albedo = surface.baseColor;
    float roughness = surface.roughness;
    float metallic = surface.metallic;
    float specular = surface.specularLevel;
#if defined(_DBUFFER)
    ApplyDBuffer(input.positionCS.xy * rcp(_ScreenParams.xy), albedo, normalWS, roughness, metallic, specular);
#endif
#if defined(_CLUSTERED_DECALS)
    float clusterViewDepth = -mul(unity_MatrixV, float4(positionWS, 1)).z;
    ApplyClusteredDecals((uint2)input.positionCS.xy, clusterViewDepth, positionWS, asuint(unity_RenderingLayer.x), albedo, normalWS, roughness, metallic, specular);
#endif
    MicrofaceContext microface = InitMicrofaceContext(specular, roughness, metallic, albedo);
    uint2 positionSS = (uint2)input.positionCS.xy;
    uint lightChannels = asuint(unity_RenderingLayer.x);

    float2 lightmapUV = input.texCoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
    float4 baked, mask;
    SampleBakedLighting(lightmapUV, positionWS, normalWS, viewDir, input.positionCS.xy, lightChannels, baked, mask);
    float viewDepth = -mul(unity_MatrixV, float4(positionWS, 1)).z;
    float occlusion = ForwardIndirectOcclusion(positionSS, viewDepth, positionWS, normalWS, lightChannels);
    float3 indirectDiffuse = ForwardIndirectDiffuse(positionSS, microface.AlbedoColor, IndirectIrradiance(baked, normalWS, _AtmosphereSkySH),
        occlusion * surface.occlusion);
    float3 reflectionWS = reflect(-viewDir, normalWS);
    float3 sky = SampleGGXCubemapArray(_AtmosphereGGXPrefilter, reflectionWS, microface.RoughnessClamp, _AtmosphereIBLMaxMip);
    float3 environmentBRDF = EnvBRDFApprox(microface.SpecularColor, microface.RoughnessClamp, saturate(dot(normalWS, viewDir))).rgb;
    float3 environmentSpecular = ForwardEnvironmentSpecular(positionSS,
        BlendReflectionProbesWithSky(positionWS, reflectionWS, microface.RoughnessClamp, positionSS, viewDepth, sky), environmentBRDF, occlusion);
    float3 directDiffuse;
    float3 direct = EvaluateSurfaceLights(positionWS, normalWS, viewDir, microface, SelectMixedLightVisibility(mask), viewDepth, lightChannels, positionSS, directDiffuse);
    return WriteForwardColor(surface.emission + indirectDiffuse + direct + environmentSpecular, indirectDiffuse + directDiffuse);
}
#endif
