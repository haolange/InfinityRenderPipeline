#ifndef INFINITY_SHADOW_CASTER
#define INFINITY_SHADOW_CASTER
float4 _ShadowCasterBias;
float4 _ShadowCasterLight;

float3 OffsetShadowCaster(float3 position, float3 normal)
{
    float3 toLight = _ShadowCasterLight.w != 0 ? _ShadowCasterLight.xyz - position : _ShadowCasterLight.xyz;
    float scale = _ShadowCasterLight.w != 0 ? length(toLight) : 1;
    float3 direction = normalize(toLight);
    float normalWeight = 1 - saturate(dot(normal, direction));
    return position + direction * (_ShadowCasterBias.x * scale) + normal * (_ShadowCasterBias.y * scale * normalWeight);
}
#endif
