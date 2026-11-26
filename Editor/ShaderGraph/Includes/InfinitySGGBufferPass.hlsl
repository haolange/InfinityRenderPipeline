#ifndef INFINITY_SG_GBUFFER_PASS
#define INFINITY_SG_GBUFFER_PASS
// GBufferPass of InfinityLit: thin GBuffer, emission into the lighting target, optional baked diffuse, shadowmask and custom data.
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
    data.Route = _SurfaceRoute > 0.5 ? SURFACE_ROUTE_FORWARD : SURFACE_ROUTE_DEFERRED;
    data.Flags = _Subsurface > 0.5 ? SURFACE_FLAG_SUBSURFACE : 0u;
    if (bakedDiffuse.a > 0)
    {
        data.Flags |= SURFACE_FLAG_HAS_BAKED_GI;
    }
    data.RenderingLayer = asuint(unity_RenderingLayer.x);

    GBufferFragmentOutput output = (GBufferFragmentOutput)0;
    float2 surfaceMetadata = 0;
    EncodeSurfaceGBuffer(data, input.positionCS.xy, output.gBufferA, output.gBufferB, surfaceMetadata);
    output.surfaceMetadata = float4(surfaceMetadata, 0, 0);

#if defined(INFINITY_GBUFFER_BAKED_DIFFUSE)
    output.lightingBuffer = float4(surface.emission, 0);
    output.bakedDiffuse = bakedDiffuse;
#else
    float3 diffuseColor = albedo * (1.0 - metallic);
    float3 envDiffuse = diffuseColor * (bakedDiffuse.a > 0 ? bakedDiffuse.rgb : EvaluateSH2Irradiance(normalWS, _AtmosphereSkySH));
    output.lightingBuffer = float4(surface.emission + envDiffuse, 0);
#endif
#if defined(SHADOWS_SHADOWMASK)
    output.bakedShadowmask = 1 - shadowMask;
#endif
#if defined(INFINITY_GBUFFER_CUSTOM_DATA_SSS)
    output.customData = _Subsurface > 0.5 ? float4(PackCustomDataSSS((uint)(_SSSProfileIndex + 0.5), _SSSThickness), 0, 0) : float4(0, 0, 0, 0);
#endif
    return output;
}
#endif
