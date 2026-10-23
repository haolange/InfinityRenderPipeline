#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/TextureView.hlsl"
#ifndef INFINITY_POST_COMBINE_INCLUDED
#define INFINITY_POST_COMBINE_INCLUDED
#include "Random.hlsl"
#include "ColorUtility.hlsl"

float4 PP_Resolution;
float PP_BloomIntensity;
float PP_VignetteIntensity;
float PP_VignetteSmoothness;
float PP_FilmGrainIntensity;
float PP_FilmGrainResponse;
int PP_FrameIndex;
float PP_ExposureMultiplier;
float PP_AutoExposure;
Texture2D<float4> SRV_SceneColorTexture;
Texture3D<float4> SRV_CombineLUT;
Texture2D<float> SRV_ExposureEV;
#if defined(INFINITY_POST_BLOOM)
Texture2D<float4> SRV_BloomTexture;
#endif

float3 InfinityPostCombine(float2 uv)
{
    float3 color = InfinitySampleLevel(SRV_SceneColorTexture,Global_point_clamp_sampler,uv,0).rgb;
    float exposure = PP_ExposureMultiplier;
    if (PP_AutoExposure > 0.5) exposure *= exp2(SRV_ExposureEV[uint2(0, 0)]);
    color *= exposure;
#if defined(INFINITY_POST_BLOOM)
    color += InfinitySampleLevel(SRV_BloomTexture,Global_bilinear_clamp_sampler,uv,0).rgb * PP_BloomIntensity;
#endif
    float3 logColor = saturate(log2(max(color, 1e-6)) / 14.0 - log2(0.18) / 14.0 + 444.0 / 1023.0);
    color = InfinitySampleLevelTrilinear(SRV_CombineLUT,Global_trilinear_clamp_sampler,logColor,0).rgb;
    float2 distance = abs(uv - 0.5) * 2.0;
    float falloff = saturate(1.0 - dot(distance, distance) * PP_VignetteIntensity);
    color *= pow(max(falloff, 1e-4), max(PP_VignetteSmoothness, 0.01) * 2.0);
    if (PP_FilmGrainIntensity > 0)
    {
        float noise = (Rand3DPCG16(int3(int2(uv * PP_Resolution.xy), PP_FrameIndex)).x + 0.5) / 65536.0;
        noise = (noise - 0.5) * 2.0 * PP_FilmGrainIntensity;
        float strength = lerp(1.0, 1.0 - saturate(Rec709Luminance(color)), PP_FilmGrainResponse);
        color *= 1.0 + noise * strength;
    }
    return max(0, color);
}
#endif
