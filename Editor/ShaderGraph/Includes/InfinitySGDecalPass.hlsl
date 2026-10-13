#ifndef INFINITY_SG_DECAL_PASS
#define INFINITY_SG_DECAL_PASS
// DBufferPass of InfinityDecal: the fragment reconstructs the opaque surface behind the decal box and evaluates the
// graph there (position, uv0 = object xz + 0.5, the projector's frame; projection along -Y).
#if !defined(REQUIRE_DEPTH_TEXTURE)
Texture2D _DepthTexture;
#endif

void frag(PackedVaryings packedInput, out float4 DBufferA : SV_Target0, out float4 DBufferB : SV_Target1, out float4 DBufferC : SV_Target2)
{
    Varyings input = UnpackVaryings(packedInput);
    float2 screenUV = input.positionCS.xy * rcp(_ScreenParams.xy);
    float sceneDepth = _DepthTexture.Load(int3(input.positionCS.xy, 0)).r;
    float3 positionWS = GetWorldSpacePos(GetNDCPos(screenUV, sceneDepth), Matrix_InvViewJitterProj);
    float3 objectPos = mul(UNITY_MATRIX_I_M, float4(positionWS, 1.0)).xyz;
    clip(0.5 - abs(objectPos));

    float3 tangentWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
    float3 normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
    Varyings projected = input;
#if defined(VARYINGS_NEED_POSITION_WS)
    projected.positionWS = positionWS;
#endif
#if defined(VARYINGS_NEED_POSITIONPREDISPLACEMENT_WS)
    projected.positionPredisplacementWS = positionWS;
#endif
#if defined(VARYINGS_NEED_NORMAL_WS)
    projected.normalWS = normalWS;
#endif
#if defined(VARYINGS_NEED_TANGENT_WS)
    projected.tangentWS = float4(tangentWS, 1);
#endif
#if defined(VARYINGS_NEED_TEXCOORD0)
    projected.texCoord0 = float4(objectPos.xz + 0.5, 0, 0);
#endif
    FInfinitySurface surface = EvaluateInfinitySurface(projected);
    clip(surface.alpha - 1e-4);

    // Tangent frame of the projector: x = right, y = forward (object z), z = up.
    float3 bitangentWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 0, 1)));
    float3 decalNormalWS = normalize(mul(surface.normalTS, float3x3(tangentWS, bitangentWS, normalWS)));
    DBufferA = float4(surface.baseColor, surface.alpha);
    DBufferB = float4(decalNormalWS * 0.5 + 0.5, surface.alpha);
    DBufferC = float4(surface.roughness, surface.metallic, surface.occlusion, surface.alpha);
}
#endif
