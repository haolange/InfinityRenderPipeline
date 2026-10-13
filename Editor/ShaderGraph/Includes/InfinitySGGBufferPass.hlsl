#ifndef INFINITY_SG_GBUFFER_PASS
#define INFINITY_SG_GBUFFER_PASS
// GBufferPass of InfinityLit: thin GBuffer, emission into the lighting target, baked diffuse and occlusion.
void frag(PackedVaryings packedInput,
    out float4 GBufferA : SV_Target0, out float4 GBufferB : SV_Target1, out float4 GBufferC : SV_Target2,
    out float4 LightingBuffer : SV_Target3, out float4 BakedDiffuse : SV_Target4, out float4 BakedOcclusion : SV_Target5)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    FInfinitySurface surface = EvaluateInfinitySurface(input);

    float3 positionWS = input.positionWS;
    float3 normalWS = InfinitySurfaceNormalWS(input, surface.normalTS);
    float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
    float3 albedo = surface.baseColor;
    float roughness = surface.roughness;
    float metallic = surface.metallic;
#if defined(_DBUFFER)
    ApplyDBuffer(input.positionCS.xy * rcp(_ScreenParams.xy), albedo, normalWS, roughness, metallic);
#endif

    FGBufferData data;
    data.Normal = normalWS;
    data.Albedo = albedo;
    data.Specular = surface.specularLevel;
    data.Roughness = roughness;
    data.Reflactance = metallic;
    data.ShadingModel = _Subsurface > 0.5 ? GBUFFER_SHADING_MODEL_SUBSURFACE : GBUFFER_SHADING_MODEL_DEFAULT_LIT;
    data.Flags = (_Subsurface > 0.5 ? GBUFFER_FLAG_SUBSURFACE : 0) | (_SurfaceRoute == 1 ? GBUFFER_FLAG_FORWARD : 0);
    data.SSSProfileIndex = (uint)(_SSSProfileIndex + 0.5);
    data.Thickness = _SSSThickness;
    data.RenderingLayer = asuint(unity_RenderingLayer.x);
    EncodeGBuffer(data, input.positionCS.xy, GBufferA, GBufferB, GBufferC);
    LightingBuffer = float4(surface.emission, 0);

    float2 lightmapUV = input.texCoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
    float4 shadowMask;
    SampleBakedLighting(lightmapUV, positionWS, normalWS, viewDir, input.positionCS.xy, asuint(unity_RenderingLayer.x), BakedDiffuse, shadowMask);
    BakedOcclusion = 1 - shadowMask;
}
#endif
