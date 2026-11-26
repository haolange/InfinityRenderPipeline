#ifndef INFINITY_SURFACE_LIGHTING
#define INFINITY_SURFACE_LIGHTING
#include "BSDF.hlsl"
#if defined(INFINITY_SKIN_SPECULAR)
#include "ShadingModel.hlsl"
#endif
#include "ShadowSampling.hlsl"
#include "Lighting/LightShapes.hlsl"
#include "Lighting/LightCookie.hlsl"
#include "Lighting/ReflectionProbes.hlsl"
#if defined(DEFERRED_ANALYTIC_SHADOW) || defined(INFINITY_FORWARD_ANALYTIC_SHADOW)
#include "AnalyticShadow.hlsl"
#endif
Texture2D<float> SRV_CascadeShadowMap;
Texture2D<float> SRV_LocalShadowMap;
// Screen-space traced shadows (ContactShadowPass): sun visibility and the local-light contact mask. Deferred binds them
// only in enabled variants so Metal never validates an unbound integer texture in the off path.
#if defined(DEFERRED_CONTACT_SHADOW) || defined(INFINITY_FORWARD_CONTACT_SHADOW)
Texture2D<float> SRV_ContactShadowSun;
Texture2D<uint> SRV_ContactShadowMask;
#endif
// Specular microfacet term (height-correlated Smith, Schlick); unclamped so HDR highlights keep their energy. The
// distribution is GGX; a skin shader defines INFINITY_SKIN_SPECULAR for the dual-lobe Beckmann of ShadingModel.hlsl.
float3 SpecularBRDF(float3 N, float3 V, float3 L, MicrofaceContext microfaceCtx)
{
    float3 H = normalize(V + L);
    BSDFContext ctx = InitBXDFContext(N, V, L, H);
    float NoL = saturate(ctx.NoL);
    float NoV = max(saturate(ctx.NoV), 1e-4);
#if defined(INFINITY_SKIN_SPECULAR)
    float distribution = SkinSpecularDistribution(saturate(ctx.NoH), microfaceCtx);
#else
    float distribution = D_GGX(ctx.NoH, microfaceCtx.RoughnessPow4);
#endif
    return distribution * Vis_SmithJointApprox_NoPI(NoL, NoV, microfaceCtx.RoughnessPow4) * F_Schlick(microfaceCtx.SpecularColor, 1, ctx.LoH);
}

float3 EvaluateDirectional(LightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float contactShadow, float viewDepth, float2 bakedVisibility, uint2 positionSS, uint layer, out float3 diffuseRadiance)
{
    diffuseRadiance = 0;
    float3 L = DirectionalLightL(light);
    float NoL = saturate(dot(normal, L));
    float specularEnergy;
    float3 specularL = SunDiskSpecularL(light, reflect(-viewDir, normal), microfaceCtx.RoughnessClamp, specularEnergy);
    float specularNoL = saturate(dot(normal, specularL));
    float3 radiance = 0;
    if (NoL > 0 || specularNoL > 0)
    {
        float shadow = _CascadeCount > 0 && (light.flags & LIGHT_FLAG_SHADOW) != 0 && light.shadowSliceCount > 0 ? SampleCascadeShadow(SRV_CascadeShadowMap, light, worldPos, viewDepth, normal) : 1.0;
        shadow = MixBakedAndRealtimeShadow(light, shadow, bakedVisibility, viewDepth);
        if ((light.flags & LIGHT_FLAG_CONTACT) != 0) shadow *= contactShadow;
#if defined(DEFERRED_ANALYTIC_SHADOW) || defined(INFINITY_FORWARD_ANALYTIC_SHADOW)
        shadow *= EvaluateAnalyticDirectShadow(positionSS, viewDepth, worldPos, L, 1e4, light.shape.w, layer);
#endif
        float3 diffuse = Diffuse_Lambert(microfaceCtx.AlbedoColor) * NoL * LightDiffuseWeight(light);
        float3 specular = SpecularBRDF(normal, viewDir, specularL, microfaceCtx) * specularNoL * specularEnergy * LightSpecularWeight(light);
        float3 attenuation = LightRadiance(light) * LightCookieColor(light, worldPos) * shadow;
        diffuseRadiance = diffuse * attenuation;
        radiance = (diffuse + specular) * attenuation;
    }
    return radiance;
}

float3 EvaluateLocal(LightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float2 bakedVisibility, float viewDepth, uint contactMask, uint2 positionSS, uint layer, out float3 diffuseRadiance)
{
    diffuseRadiance = 0;
    float window = LocalLightWindow(light, worldPos);
    float3 radiance = 0;
    if (window > 0)
    {
        LightSourceShapeSample shape = EvaluateLocalLightSourceShape(light, worldPos, normal, viewDir, microfaceCtx.RoughnessClamp);
        float3 diffuse = Diffuse_Lambert(microfaceCtx.AlbedoColor) * shape.diffuseIrradiance * LightDiffuseWeight(light);
        float3 specular = SpecularBRDF(normal, viewDir, shape.specularL, microfaceCtx) * shape.specularNoL * shape.specularFalloff
            * shape.specularEnergy * LightSpecularWeight(light);
        if (LightHasCookie(light))
        {
            float3 cookie = LightCookieColor(light, worldPos);
            float3 planarDiffuse, planarSpecular;
            LightPlanarCookie(light, shape.emitterUV, microfaceCtx.RoughnessClamp, planarDiffuse, planarSpecular);
            diffuse *= cookie * planarDiffuse;
            specular *= cookie * planarSpecular;
        }
        float shadow = MixBakedAndRealtimeShadow(light, SampleLocalShadow(SRV_LocalShadowMap, light, worldPos, normal), bakedVisibility, viewDepth);
        shadow *= ContactShadowFromMask(light, contactMask);
#if defined(DEFERRED_ANALYTIC_SHADOW) || defined(INFINITY_FORWARD_ANALYTIC_SHADOW)
        float3 toLocalLight = light.positionRange.xyz - worldPos;
        float localDist = max(length(toLocalLight), 1e-4);
        shadow *= EvaluateAnalyticDirectShadow(positionSS, viewDepth, worldPos, toLocalLight / localDist, localDist, saturate(light.shape.x / localDist), layer);
#endif
        float3 attenuation = LightRadiance(light) * window * shadow;
        diffuseRadiance = diffuse * attenuation;
        radiance = (diffuse + specular) * attenuation;
    }
    return radiance;
}

// Every light that reaches positionSS (SV_Position.xy or the deferred pixel) at viewDepth and shares one of the receiver's
// light channels. bakedVisibility is the receiver's baked visibility of the two mixed directional lights.
float3 EvaluateSurfaceLights(float3 position, float3 normal, float3 view, MicrofaceContext material, float2 bakedVisibility, float viewDepth, uint layer, uint2 positionSS, out float3 diffuseRadiance)
{
    diffuseRadiance = 0;
    float3 direct = 0;
    float contactSun = 1;
    uint contactMask = 0;
#if defined(INFINITY_FORWARD_CONTACT_SHADOW) || defined(DEFERRED_CONTACT_SHADOW)
    contactSun = SRV_ContactShadowSun.Load(int3(positionSS, 0));
    contactMask = SRV_ContactShadowMask.Load(int3(positionSS, 0));
#endif
    for (int i = 0; i < g_DirectionalLightCount; i++)
    {
        LightRecord light = g_LightRecordBuffer[i];
        if ((light.lightLayer & layer) != 0)
        {
            float3 diffuse;
            direct += EvaluateDirectional(light, position, normal, view, material, i == g_SunRecordIndex ? contactSun : 1, viewDepth, bakedVisibility, positionSS, layer, diffuse);
            diffuseRadiance += diffuse;
        }
    }
    LightLoopState lightLoop;
    BeginLightLoop(positionSS, viewDepth, lightLoop);
    uint recordIndex;
    [loop]
    while (NextLight(lightLoop, recordIndex))
    {
        LightRecord light = g_LightRecordBuffer[recordIndex];
        if ((light.lightLayer & layer) != 0)
        {
            float3 diffuse;
            direct += EvaluateLocal(light, position, normal, view, material, bakedVisibility, viewDepth, contactMask, positionSS, layer, diffuse);
            diffuseRadiance += diffuse;
        }
    }
    return direct;
}
#endif
