#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/TextureView.hlsl"
#ifndef INFINITY_SG_GRAPH_FUNCTIONS
#define INFINITY_SG_GRAPH_FUNCTIONS
// Last pre-graph include: binds the Shader Graph scene / lighting nodes to Infinity resources where the pass has them,
// then includes Shader Graph's node library (its fallbacks apply to the rest):
//   Scene Color  the opaque color pyramid; bound in the T1 translucent stage (refraction).
//   Scene Depth  the opaque depth buffer; bound in the decal (DBuffer) pass.
//   Baked GI     lightmap -> APV -> LPPV / probe SH (Lightmap.hlsl) in the GBuffer and Forward passes.
//   Reflection   the sky GGX prefilter in the Forward pass.

#if defined(REQUIRE_OPAQUE_TEXTURE)
#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/TranslucentCommon.hlsl"
float3 InfinitySampleSceneColor(float2 uv)
{
    return InfinitySampleLevel(_ColorPyramidTexture,Global_bilinear_clamp_sampler,uv,0).rgb;
}
#define SHADERGRAPH_SAMPLE_SCENE_COLOR(uv) InfinitySampleSceneColor(uv)
#endif

#if defined(REQUIRE_DEPTH_TEXTURE) && (SHADERPASS == SHADERPASS_DBUFFER)
Texture2D _DepthTexture;
float InfinitySampleSceneDepth(float2 uv)
{
    return InfinitySampleLevel(_DepthTexture,Global_point_clamp_sampler,uv,0).r;
}
#define SHADERGRAPH_SAMPLE_SCENE_DEPTH(uv) InfinitySampleSceneDepth(uv)
#endif

#if defined(INFINITY_SG_BAKED_LIGHTING)
float3 InfinityBakedGI(float3 positionWS, float3 normalWS, uint2 positionSS, float2 uvStaticLightmap, float2 uvDynamicLightmap, bool applyScaling)
{
    float2 uv = applyScaling ? uvStaticLightmap * unity_LightmapST.xy + unity_LightmapST.zw : uvStaticLightmap;
    float4 diffuse, mask;
    SampleBakedLighting(uv, positionWS, normalWS, GetWorldSpaceNormalizeViewDir(positionWS), positionSS, asuint(unity_RenderingLayer.x), diffuse, mask);
    return diffuse.rgb;
}
#define SHADERGRAPH_BAKED_GI(positionWS, normalWS, positionSS, uvStaticLightmap, uvDynamicLightmap, applyScaling) InfinityBakedGI(positionWS, normalWS, positionSS, uvStaticLightmap, uvDynamicLightmap, applyScaling)
#endif

#if SHADERPASS == SHADERPASS_FORWARD
float3 InfinityReflectionProbe(float3 viewDir, float3 normalOS, float lod)
{
    float3 reflection = reflect(-viewDir, normalOS);
    return SampleGGXCubemapArray(_AtmosphereGGXPrefilter, reflection, saturate(lod / max(_AtmosphereIBLMaxMip, 1)), _AtmosphereIBLMaxMip);
}
#define SHADERGRAPH_REFLECTION_PROBE(viewDir, normalOS, lod) InfinityReflectionProbe(viewDir, normalOS, lod)
#endif

#define SHADERGRAPH_OBJECT_POSITION UNITY_MATRIX_M._m03_m13_m23

#include "Packages/com.unity.shadergraph/ShaderGraphLibrary/Functions.hlsl"
#endif
