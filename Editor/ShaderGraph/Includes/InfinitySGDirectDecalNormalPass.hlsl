#ifndef INFINITY_SG_DIRECT_DECAL_NORMAL_PASS
#define INFINITY_SG_DIRECT_DECAL_NORMAL_PASS
// Clip-replace of the GBuffer, including the octahedral normal. Soft edges stay on the DBuffer target.
#include "Packages/com.infinity.render-pipeline/Shaders/Library/DecalProjection.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/GBufferPack.hlsl"
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

    float3 tangentWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
    float3 normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
    float3 bitangentWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 0, 1)));
    Varyings projected = input;
#if defined(VARYINGS_NEED_TEXCOORD0)
    projected.texCoord0 = float4(DecalProjectedUV(objectPos), 0, 0);
#endif
    InfinitySurface surface = EvaluateInfinitySurface(projected);
    surface.alpha *= DecalAngleFade(positionWS, normalWS, _WorldSpaceCameraPos);
    clip(surface.alpha - 0.5);

    SurfaceData data;
    data.Albedo = surface.baseColor * surface.occlusion;
    data.Normal = normalize(mul(surface.normalTS, float3x3(tangentWS, bitangentWS, normalWS)));
    data.Roughness = surface.roughness;
    data.Metallic = surface.metallic;
    data.Specular = surface.specularLevel;
    EncodeSurfaceGBuffer(data, gBufferA, gBufferB);
}
#endif
