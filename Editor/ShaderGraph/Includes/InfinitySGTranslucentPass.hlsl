#ifndef INFINITY_SG_TRANSLUCENT_PASS
#define INFINITY_SG_TRANSLUCENT_PASS
// Translucent stages of InfinityLit / InfinityUnlit: T0 fogged, T1 refracted through the color pyramid, T2 plain. All
// write the reactive mask, motion and motion metadata like the hand-written shaders.
struct InfinityTranslucentOutput
{
    float4 color : SV_Target0;
    float reactive : SV_Target1;
    float2 motion : SV_Target2;
    float4 motionMetadata : SV_Target3;
};

InfinityTranslucentOutput frag(PackedVaryings packedInput)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    FInfinitySurface surface = EvaluateInfinitySurface(input);

    float3 positionWS = input.positionWS;
    float2 screenUV = input.positionCS.xy / _ScreenParams.xy;
    float3 color = surface.baseColor + surface.emission;
    InfinityTranslucentOutput output;
#if SHADERPASS == SHADERPASS_TRANSLUCENT_T0
    output.color = ApplyT0Fog(color, surface.alpha, screenUV, length(positionWS - _WorldSpaceCameraPos));
#elif SHADERPASS == SHADERPASS_TRANSLUCENT_T1
    float glassDepth = _TranslucentDepthTexture.SampleLevel(Global_point_clamp_sampler, screenUV, 0).r;
    float thickness = saturate(abs(LinearEyeDepth(input.positionCS.z, _ZBufferParams) - LinearEyeDepth(glassDepth, _ZBufferParams)) * 0.05);
    float4 refracted = SampleRefractionPyramid(screenUV, InfinitySurfaceNormalWS(input, surface.normalTS), _RefractionStrength * (0.25 + thickness), surface.roughness);
    output.color = float4(lerp(color, refracted.rgb * color, saturate(surface.alpha)), surface.alpha);
#else
    output.color = float4(color, surface.alpha);
#endif
    output.reactive = TranslucentReactive(surface.alpha);
    float4 currentClip, previousClip;
    InfinityMotionClip(positionWS, currentClip, previousClip);
    output.motion = SurfaceVelocity(currentClip, previousClip);
    output.motionMetadata = SurfaceMotionMetadata(currentClip, previousClip, input.positionCS.z);
    return output;
}
#endif
