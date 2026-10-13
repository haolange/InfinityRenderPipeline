#ifndef INFINITY_SG_VARYINGS
#define INFINITY_SG_VARYINGS
// First post-graph include: builds Varyings from the vertex graph, the surface helpers shared by the pass fragments,
// and motion reconstruction. Clip position conventions follow InfinityLit: UNITY_MATRIX_VP (jittered) for raster,
// Matrix_ViewFlipYProj for shadow casters (the shadow passes set it to the light matrix) and for motion.

#if SHADERPASS == SHADERPASS_SHADOWS
#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/ShadowCaster.hlsl"
#endif

#if SHADERPASS == SHADERPASS_META
CBUFFER_START(UnityMetaPass)
    bool4 unity_MetaVertexControl;
    bool4 unity_MetaFragmentControl;
CBUFFER_END
float unity_OneOverOutputBoost;
float unity_MaxOutputValue;
float unity_UseLinearSpace;

float4 MetaVertexPosition(float4 positionOS, float2 uv1, float2 uv2, float4 uv1ST, float4 uv2ST)
{
    if (unity_MetaVertexControl.x)
    {
        positionOS.xy = uv1 * uv1ST.xy + uv1ST.zw;
        positionOS.z = positionOS.z > 0 ? REAL_MIN : 0.0f;
    }
    if (unity_MetaVertexControl.y)
    {
        positionOS.xy = uv2 * uv2ST.xy + uv2ST.zw;
        positionOS.z = positionOS.z > 0 ? REAL_MIN : 0.0f;
    }
    return mul(unity_MatrixVP, float4(positionOS.xyz, 1.0));
}
#endif

Varyings BuildVaryings(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

#if defined(FEATURES_GRAPH_VERTEX)
    VertexDescription vertexDescription = VertexDescriptionFunction(BuildVertexDescriptionInputs(input));
    #if defined(CUSTOMINTERPOLATOR_VARYPASSTHROUGH_FUNC)
        CustomInterpolatorPassThroughFunc(output, vertexDescription);
    #endif
    input.positionOS = vertexDescription.Position;
    #if defined(ATTRIBUTES_NEED_NORMAL)
        input.normalOS = vertexDescription.Normal;
    #endif
    #if defined(ATTRIBUTES_NEED_TANGENT)
        input.tangentOS.xyz = vertexDescription.Tangent.xyz;
    #endif
#endif

    float3 positionWS = TransformObjectToWorld(input.positionOS);
#if defined(ATTRIBUTES_NEED_NORMAL)
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#else
    float3 normalWS = float3(0, 0, 1);
#endif

#if defined(VARYINGS_NEED_POSITION_WS)
    output.positionWS = positionWS;
#endif
#if defined(VARYINGS_NEED_POSITIONPREDISPLACEMENT_WS)
    output.positionPredisplacementWS = positionWS;
#endif
#if defined(VARYINGS_NEED_NORMAL_WS)
    output.normalWS = normalWS;
#endif
#if defined(VARYINGS_NEED_TANGENT_WS) && defined(ATTRIBUTES_NEED_TANGENT)
    output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
#endif

#if SHADERPASS == SHADERPASS_SHADOWS
    output.positionCS = mul(Matrix_ViewFlipYProj, float4(OffsetShadowCaster(positionWS, normalWS), 1.0));
#elif SHADERPASS == SHADERPASS_META
    output.positionCS = MetaVertexPosition(float4(input.positionOS, 1.0), input.uv1.xy, input.uv2.xy, unity_LightmapST, unity_DynamicLightmapST);
#else
    output.positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
#endif

#if defined(VARYINGS_NEED_TEXCOORD0) && defined(ATTRIBUTES_NEED_TEXCOORD0)
    output.texCoord0 = input.uv0;
#endif
#if defined(VARYINGS_NEED_TEXCOORD1) && defined(ATTRIBUTES_NEED_TEXCOORD1)
    output.texCoord1 = input.uv1;
#endif
#if defined(VARYINGS_NEED_TEXCOORD2) && defined(ATTRIBUTES_NEED_TEXCOORD2)
    output.texCoord2 = input.uv2;
#endif
#if defined(VARYINGS_NEED_TEXCOORD3) && defined(ATTRIBUTES_NEED_TEXCOORD3)
    output.texCoord3 = input.uv3;
#endif
#if defined(VARYINGS_NEED_TEXCOORD4) && defined(ATTRIBUTES_NEED_TEXCOORD4)
    output.texCoord4 = input.uv4;
#endif
#if defined(VARYINGS_NEED_TEXCOORD5) && defined(ATTRIBUTES_NEED_TEXCOORD5)
    output.texCoord5 = input.uv5;
#endif
#if defined(VARYINGS_NEED_TEXCOORD6) && defined(ATTRIBUTES_NEED_TEXCOORD6)
    output.texCoord6 = input.uv6;
#endif
#if defined(VARYINGS_NEED_TEXCOORD7) && defined(ATTRIBUTES_NEED_TEXCOORD7)
    output.texCoord7 = input.uv7;
#endif
#if defined(VARYINGS_NEED_COLOR) && defined(ATTRIBUTES_NEED_COLOR)
    output.color = input.color;
#endif
#if defined(VARYINGS_NEED_SCREENPOSITION)
    output.screenPosition = ComputeScreenPos(output.positionCS, _ProjectionParams.x);
#endif
#if defined(VARYINGS_NEED_VERTEXID) && defined(ATTRIBUTES_NEED_VERTEXID)
    output.vertexID = input.vertexID;
#endif
#if defined(VARYINGS_NEED_INSTANCEID) && defined(ATTRIBUTES_NEED_INSTANCEID)
    output.instanceID = input.instanceID;
#endif
    return output;
}

PackedVaryings vert(Attributes input)
{
    Varyings output = BuildVaryings(input);
    return PackVaryings(output);
}

// Surface outputs with InfinityLit's defaults for blocks a pass or sub-target does not have.
struct FInfinitySurface
{
    float3 baseColor;
    float3 normalTS;
    float metallic;
    float roughness;
    float specularLevel;
    float occlusion;
    float3 emission;
    float alpha;
};

FInfinitySurface GetInfinitySurface(SurfaceDescription description)
{
    FInfinitySurface surface;
#if defined(INFINITY_SG_BASECOLOR)
    surface.baseColor = description.BaseColor;
#else
    surface.baseColor = 1;
#endif
#if defined(INFINITY_SG_NORMALTS)
    surface.normalTS = description.NormalTS;
#else
    surface.normalTS = float3(0, 0, 1);
#endif
#if defined(INFINITY_SG_METALLIC)
    surface.metallic = description.Metallic;
#else
    surface.metallic = 0;
#endif
#if defined(INFINITY_SG_ROUGHNESS)
    surface.roughness = description.Roughness;
#else
    surface.roughness = 0.5;
#endif
#if defined(INFINITY_SG_SPECULARLEVEL)
    surface.specularLevel = description.SpecularLevel;
#else
    surface.specularLevel = 0.5;
#endif
#if defined(INFINITY_SG_OCCLUSION)
    surface.occlusion = description.Occlusion;
#else
    surface.occlusion = 1;
#endif
#if defined(INFINITY_SG_EMISSION)
    surface.emission = description.Emission;
#else
    surface.emission = 0;
#endif
#if defined(INFINITY_SG_ALPHA)
    surface.alpha = description.Alpha;
#else
    surface.alpha = 1;
#endif
#if defined(_ALPHATEST_ON) && defined(INFINITY_SG_ALPHA) && defined(INFINITY_SG_ALPHACLIPTHRESHOLD)
    clip(description.Alpha - description.AlphaClipThreshold);
#endif
    return surface;
}

FInfinitySurface EvaluateInfinitySurface(Varyings input)
{
    return GetInfinitySurface(SurfaceDescriptionFunction(BuildSurfaceDescriptionInputs(input)));
}

// Tangent-space normal to world space with the interpolated (MikkTSpace) frame.
float3 InfinitySurfaceNormalWS(Varyings input, float3 normalTS)
{
#if defined(VARYINGS_NEED_NORMAL_WS)
    float3 normalWS = input.normalWS;
    #if defined(VARYINGS_NEED_TANGENT_WS)
        float sign = (input.tangentWS.w > 0.0 ? 1.0 : -1.0) * GetOddNegativeScale();
        float3 bitangentWS = sign * cross(normalWS, input.tangentWS.xyz);
        normalWS = mul(normalTS, float3x3(input.tangentWS.xyz, bitangentWS, normalWS));
    #endif
    return normalize(normalWS);
#else
    return float3(0, 0, 1);
#endif
}

// Current and previous clip positions of a rigid object from its world position (unity_MatrixPreviousM).
void InfinityMotionClip(float3 positionWS, out float4 currentClip, out float4 previousClip)
{
    float4 positionOS = mul(UNITY_MATRIX_I_M, float4(positionWS, 1.0));
    currentClip = mul(Matrix_ViewFlipYProj, float4(positionWS, 1.0));
    previousClip = mul(Matrix_LastViewFlipYProj, mul(unity_MatrixPreviousM, float4(positionOS.xyz, 1.0)));
}
#endif
