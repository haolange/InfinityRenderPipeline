#ifndef INFINITY_DECAL_PROJECTION
#define INFINITY_DECAL_PROJECTION
// Decal projector parameters (DecalComponent, per-renderer property block) shared by InfinityDecal.shader and the
// Shader Graph decal pass. Unset values (all zero) mean identity UVs and no angle fade.
// _DecalUVScaleBias: xy = uv scale, zw = uv bias.
// _DecalAngleFade: x, y = fade = saturate(cos(angle) * x + y) between the start and end angles; z = 1 when enabled.
float4 _DecalUVScaleBias;
float4 _DecalAngleFade;

// Decal box: object space -0.5..0.5, projecting along -Y; uv = object xz + 0.5 before the projector's scale and bias.
float2 DecalProjectedUV(float3 objectPosition)
{
    float2 scale = any(_DecalUVScaleBias.xy != 0) ? _DecalUVScaleBias.xy : 1;
    return (objectPosition.xz + 0.5) * scale + _DecalUVScaleBias.zw;
}

// Fades the decal on surfaces turned away from the projector. The surface normal comes from the depth-reconstructed
// position (there is no GBuffer normal before the DBuffer pass), oriented toward the camera.
float DecalAngleFade(float3 positionWS, float3 decalUpWS, float3 cameraPositionWS)
{
    if (_DecalAngleFade.z <= 0)
        return 1;
    float3 normalWS = normalize(cross(ddy(positionWS), ddx(positionWS)));
    normalWS *= dot(normalWS, cameraPositionWS - positionWS) < 0 ? -1 : 1;
    return saturate(dot(normalWS, decalUpWS) * _DecalAngleFade.x + _DecalAngleFade.y);
}
#endif
