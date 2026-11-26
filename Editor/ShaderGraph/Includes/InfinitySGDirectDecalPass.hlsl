#ifndef INFINITY_SG_DIRECT_DECAL_PASS
#define INFINITY_SG_DIRECT_DECAL_PASS
// GBuffer-target blend. Coverage stays in alpha, so only channels outside alpha are blended: albedo and metallic.
#include "Packages/com.infinity.render-pipeline/Shaders/Library/DecalProjection.hlsl"
#if !defined(REQUIRE_DEPTH_TEXTURE)
Texture2D _DepthTexture;
#endif

void frag(PackedVaryings packedInput, out float4 gBufferA : SV_Target0, out float4 gBufferB : SV_Target1)
{
    Varyings input = UnpackVaryings(packedInput);
    float2 screenUV = input.positionCS.xy * rcp(_ScreenParams.xy);
    float sceneDepth = _DepthTexture.Load(int3(input.positionCS.xy, 0)).r;
    float3 positionWS = GetWorldSpacePos(GetNDCPos(screenUV, sceneDepth), Matrix_InvViewFlipYJitterProj);
    float3 objectPos = mul(UNITY_MATRIX_I_M, float4(positionWS, 1.0)).xyz;
    clip(0.5 - abs(objectPos));

    float3 normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
    Varyings projected = input;
#if defined(VARYINGS_NEED_TEXCOORD0)
    projected.texCoord0 = float4(DecalProjectedUV(objectPos), 0, 0);
#endif
    InfinitySurface surface = EvaluateInfinitySurface(projected);
    surface.alpha *= DecalAngleFade(positionWS, normalWS, _WorldSpaceCameraPos);
    clip(surface.alpha - 1e-4);
    float coverage = saturate(surface.alpha);
    gBufferA = float4(surface.baseColor * surface.occlusion, coverage);
    gBufferB = float4(0, 0, surface.metallic, coverage);
}
#endif
