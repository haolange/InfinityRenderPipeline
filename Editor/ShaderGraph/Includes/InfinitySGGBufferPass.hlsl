#ifndef INFINITY_SG_GBUFFER_PASS
#define INFINITY_SG_GBUFFER_PASS
#include "Packages/com.infinity.render-pipeline/Shaders/Library/DecalCluster.hlsl"
// GBufferPass of InfinityLit: the RGBA8 GBuffer, emission and occluded indirect diffuse into Scene Color, and the sideband.
GBufferFragmentOutput frag(PackedVaryings packedInput)
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

    float2 lightmapUV = input.texCoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
    float4 bakedDiffuse = 0;
    float4 shadowMask = 1;
    SampleBakedLighting(lightmapUV, positionWS, normalWS, viewDir, input.positionCS.xy, asuint(unity_RenderingLayer.x), bakedDiffuse, shadowMask);

    SurfaceData data;
    data.Normal = normalWS;
    data.Albedo = albedo;
    data.Specular = specular;
    data.Roughness = roughness;
    data.Metallic = metallic;

    GBufferFragmentOutput output = (GBufferFragmentOutput)0;
    EncodeSurfaceGBuffer(data, output.gBufferA, output.gBufferB);
    float3 diffuseColor = albedo * (1.0 - metallic);
    WriteGeometryColor(surface.emission, diffuseColor, surface.occlusion, bakedDiffuse, shadowMask, normalWS, _AtmosphereSkySH,
        asuint(unity_RenderingLayer.x), 0u, output);
    return output;
}
#endif
