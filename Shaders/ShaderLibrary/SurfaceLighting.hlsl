#ifndef INFINITY_SURFACE_LIGHTING
#define INFINITY_SURFACE_LIGHTING
#include "BSDF.hlsl"
#include "ShadingModel.hlsl"
#include "ShadowSampling.hlsl"
Texture2D<float> SRV_CascadeShadowMap;
Texture2D<float> SRV_LocalShadowMap;
float3 EvaluateDirectional(FLightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float contactShadow, float viewDepth, float4 bakedMask)
{
    float3 L = DirectionalLightL(light);
    float3 H = normalize(viewDir + L);
    BSDFContext bsdfCtx = InitBXDFContext(normal, viewDir, L, H);
    float NoL = saturate(bsdfCtx.NoL);
    if (NoL <= 0)
    {
        return 0;
    }

    float shadow = _CascadeCount > 0 && (light.flags & LIGHT_FLAG_SHADOW) != 0 && light.shadowSliceCount > 0 ? SampleCascadeShadow(SRV_CascadeShadowMap, worldPos, viewDepth, light.shadowFilter, normal) : 1.0;
    shadow = MixBakedAndRealtimeShadow(light, shadow, bakedMask, viewDepth);
    if ((light.flags & LIGHT_FLAG_CONTACT) != 0) shadow *= contactShadow;
    float3 brdf = DefultLit(bsdfCtx, microfaceCtx);
    return brdf * LightRadiance(light) * NoL * shadow * LightDiffuseWeight(light);
}

float3 EvaluateLocal(FLightRecord light, float3 worldPos, float3 normal, float3 viewDir, MicrofaceContext microfaceCtx, float4 bakedMask, float viewDepth)
{
    if (IsPlanarLight(light))
    {
        // One-sided emitter: nothing behind the emitting face.
        if (dot(worldPos - light.positionRange.xyz, light.forward.xyz) <= 0)
        {
            return 0;
        }
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
            spec = DefultLit(specCtx, specMicro) * LightRadiance(light) * saturate(specCtx.NoL) * LightSpecularWeight(light);
        }

        float form = FrostbiteRectFormFactor(worldPos, normal, light);
        float3 diff = microfaceCtx.AlbedoColor * LightRadiance(light) * form * LightDiffuseWeight(light) * (1.0 / 3.14159265);
        float dist = length(light.positionRange.xyz - worldPos);
        float att = DistanceAttenuation(dist, light.positionRange.w);
        return (diff + spec) * att * MixBakedAndRealtimeShadow(light, 1, bakedMask, viewDepth);
    }

    // Sphere, line and tube sources are evaluated at their center until the representative-point shapes land
    // (0.5.0 S8).
    float3 L = LocalLightL(light, worldPos);
    float att = LocalLightAttenuation(light, worldPos);

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
    return brdf * LightRadiance(light) * NoL * att * shadow * LightDiffuseWeight(light);
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
