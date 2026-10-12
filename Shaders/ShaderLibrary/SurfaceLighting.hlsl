#ifndef INFINITY_SURFACE_LIGHTING
#define INFINITY_SURFACE_LIGHTING
#include "BSDF.hlsl"
#include "ShadingModel.hlsl"
#include "ShadowSampling.hlsl"
#include "Lighting/LightShapes.hlsl"
Texture2D<float> SRV_CascadeShadowMap;
Texture2D<float> SRV_LocalShadowMap;
// Specular microfacet term (GGX, height-correlated Smith, Schlick); unclamped so HDR highlights keep their energy.
float3 SpecularBRDF(float3 N, float3 V, float3 L, MicrofaceContext microfaceCtx)
{
    float3 H = normalize(V + L);
    BSDFContext ctx = InitBXDFContext(N, V, L, H);
    float NoL = saturate(ctx.NoL);
    float NoV = max(saturate(ctx.NoV), 1e-4);
    return D_GGX(ctx.NoH, microfaceCtx.RoughnessPow4) * Vis_SmithJointApprox_NoPI(NoL, NoV, microfaceCtx.RoughnessPow4)
        * F_Schlick(microfaceCtx.SpecularColor, 1, ctx.LoH);
}

float3 EvaluateDirectional(FLightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float contactShadow, float viewDepth, float4 bakedMask)
{
    float3 L = DirectionalLightL(light);
    float NoL = saturate(dot(normal, L));
    float specularEnergy;
    float3 specularL = SunDiskSpecularL(light, reflect(-viewDir, normal), microfaceCtx.RoughnessClamp, specularEnergy);
    float specularNoL = saturate(dot(normal, specularL));
    if (NoL <= 0 && specularNoL <= 0)
    {
        return 0;
    }

    float shadow = _CascadeCount > 0 && (light.flags & LIGHT_FLAG_SHADOW) != 0 && light.shadowSliceCount > 0 ? SampleCascadeShadow(SRV_CascadeShadowMap, light, worldPos, viewDepth, normal) : 1.0;
    shadow = MixBakedAndRealtimeShadow(light, shadow, bakedMask, viewDepth);
    if ((light.flags & LIGHT_FLAG_CONTACT) != 0) shadow *= contactShadow;
    float3 diffuse = Diffuse_Lambert(microfaceCtx.AlbedoColor) * NoL * LightDiffuseWeight(light);
    float3 specular = SpecularBRDF(normal, viewDir, specularL, microfaceCtx) * specularNoL * specularEnergy * LightSpecularWeight(light);
    return (diffuse + specular) * LightRadiance(light) * shadow;
}

float3 EvaluateLocal(FLightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float4 bakedMask, float viewDepth)
{
    float window = LocalLightWindow(light, worldPos);
    if (window <= 0)
    {
        return 0;
    }

    FLightShapeSample shape = EvaluateLocalLightShape(light, worldPos, normal, viewDir, microfaceCtx.RoughnessClamp);
    float3 diffuse = Diffuse_Lambert(microfaceCtx.AlbedoColor) * shape.diffuseIrradiance * LightDiffuseWeight(light);
    float3 specular = SpecularBRDF(normal, viewDir, shape.specularL, microfaceCtx) * shape.specularNoL * shape.specularFalloff
        * shape.specularEnergy * LightSpecularWeight(light);
    float shadow = MixBakedAndRealtimeShadow(light, SampleLocalShadow(SRV_LocalShadowMap, light, worldPos, normal), bakedMask, viewDepth);
    return (diffuse + specular) * LightRadiance(light) * window * shadow;
}

// Forward / translucent evaluation of every light that reaches positionSS (SV_Position.xy) at viewDepth.
float3 EvaluateSurfaceLights(float3 position, float3 normal, float3 view, MicrofaceContext material, float4 mask, float viewDepth, uint layer, uint2 positionSS)
{
    float3 direct = 0;
    for (int i = 0; i < g_DirectionalLightCount; i++)
    {
        FLightRecord light = g_LightRecordBuffer[i];
        if ((light.lightLayer & layer) != 0) direct += EvaluateDirectional(light, position, normal, view, material, 1, viewDepth, mask);
    }
    FLightLoop lightLoop = BeginLightLoop(positionSS, viewDepth);
    uint recordIndex;
    [loop]
    while (NextLight(lightLoop, recordIndex))
    {
        FLightRecord light = g_LightRecordBuffer[recordIndex];
        if ((light.lightLayer & layer) != 0) direct += EvaluateLocal(light, position, normal, view, material, mask, viewDepth);
    }
    return direct;
}
#endif
