#ifndef INFINITY_SURFACE_LIGHTING
#define INFINITY_SURFACE_LIGHTING
#include "BSDF.hlsl"
#include "ShadingModel.hlsl"
#include "ShadowSampling.hlsl"
Texture2D<float> SRV_CascadeShadowMap;
Texture2D<float> SRV_LocalShadowMap;
float3 EvaluateDirectional(FLightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float contactShadow, float viewDepth, float4 bakedMask)
{
    float3 L = light.directionSpot.xyz;
    float3 H = normalize(viewDir + L);
    BSDFContext bsdfCtx = InitBXDFContext(normal, viewDir, L, H);
    float NoL = saturate(bsdfCtx.NoL);
    if (NoL <= 0)
    {
        return 0;
    }

    float shadow = _CascadeCount > 0 && (light.flags & LIGHT_FLAG_SHADOW) != 0 && light.shadowSliceCount > 0 ? SampleCascadeShadow(SRV_CascadeShadowMap, worldPos, viewDepth, light.shadowType, normal) : 1.0;
    shadow = MixBakedAndRealtimeShadow(light, shadow, bakedMask, viewDepth);
    if ((light.flags & LIGHT_FLAG_CONTACT) != 0) shadow *= contactShadow;
    float3 brdf = DefultLit(bsdfCtx, microfaceCtx);
    return brdf * LightRadiance(light) * NoL * shadow * light.axisX.w;
}

float3 EvaluateLocal(FLightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float4 bakedMask, float viewDepth)
{
    if (light.lightType == LIGHT_TYPE_RECT)
    {
        float3 R = reflect(-viewDir, normal);
        float3 specPos = KarisRectRepresentativePoint(worldPos, R, light);
        float3 Lspec = normalize(specPos - worldPos);
        float3 H = normalize(viewDir + Lspec);
        BSDFContext specCtx = InitBXDFContext(normal, viewDir, Lspec, H);
        float3 spec = 0;
        if (specCtx.NoL > 0)
        {
            MicrofaceContext specMicro = microfaceCtx;
            specMicro.AlbedoColor = 0;
            spec = DefultLit(specCtx, specMicro) * LightRadiance(light) * saturate(specCtx.NoL) * light.axisY.w;
        }

        float form = FrostbiteRectFormFactor(worldPos, normal, light);
        float3 diff = microfaceCtx.AlbedoColor * LightRadiance(light) * form * light.axisX.w * (1.0 / 3.14159265);
        float dist = length(light.positionRange.xyz - worldPos);
        float att = DistanceAttenuation(dist, light.positionRange.w);
        return (diff + spec) * att * MixBakedAndRealtimeShadow(light, 1, bakedMask, viewDepth);
    }

    float3 toLight = light.positionRange.xyz - worldPos;
    float dist = length(toLight);
    float3 L = toLight / max(dist, 1e-4);
    float att = DistanceAttenuation(dist, light.positionRange.w);
    if (light.lightType == LIGHT_TYPE_SPOT)
    {
        att *= SpotAttenuation(L, light.directionSpot.xyz, light.shape.x, light.directionSpot.w);
    }

    if (att <= 0)
    {
        return 0;
    }

    float3 H = normalize(viewDir + L);
    BSDFContext bsdfCtx = InitBXDFContext(normal, viewDir, L, H);
    float NoL = saturate(bsdfCtx.NoL);
    if (NoL <= 0)
    {
        return 0;
    }

    float shadow = MixBakedAndRealtimeShadow(light, SampleLocalShadow(SRV_LocalShadowMap, light, worldPos, normal), bakedMask, viewDepth);
    float3 brdf = DefultLit(bsdfCtx, microfaceCtx);
    return brdf * LightRadiance(light) * NoL * att * shadow * light.axisX.w;
}


float3 EvaluateSurfaceLights(float3 position, float3 normal, float3 view, MicrofaceContext material, float4 mask, float viewDepth, uint layer)
{
    float3 direct = 0;
    for (int i = 0; i < g_DirectionalLightCount; i++)
    {
        FLightRecord light = g_LightRecordBuffer[i];
        if ((light.lightLayer & layer) != 0) direct += EvaluateDirectional(light, position, normal, view, material, 1, viewDepth, mask);
    }
    for (int i = 0; i < g_LocalLightCount; i++)
    {
        FLightRecord light = g_LightRecordBuffer[g_DirectionalLightCount + i];
        if ((light.lightLayer & layer) != 0) direct += EvaluateLocal(light, position, normal, view, material, mask, viewDepth);
    }
    return direct;
}
#endif
