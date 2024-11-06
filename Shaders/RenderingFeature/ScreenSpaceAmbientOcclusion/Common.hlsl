#ifndef _OcclusionCommon_
#define _OcclusionCommon_

//#define float half
//#define float2 half2
//#define float3 half3
//#define float4 half4
//#define float3x3 half3x3
//#define float4x4 half4x4
//#define float4x3 half4x3
#define PI 3.1415926
#define Inv_PI 0.3183091
#define Two_PI 6.2831852
#define Half_PI 1.5707963
#define Inv_Two_PI 0.15915494

cbuffer CBV_OcclusionUnifrom
{ 
    int NumRay; 
    int NumStep;
    float Power;
    float Radius;
    float Intensity;
    float Sharpeness;
    float HalfProjScale;
    float TemporalOffset;
    float TemporalDirection;
    float TemporalScale;
    float TemporalWeight;
    float4 Resolution;
    float4 UpsampleSize;
    float4x4 Matrix_Proj;
    float4x4 Matrix_InvProj; 
    float4x4 Matrix_ViewProj; 
    float4x4 Matrix_InvViewProj; 
    float4x4 Matrix_ViewToWorld; 
    float4x4 Matrix_WorldToView;
};

Texture2D SRV_DepthTexture, SRV_NormalTexture, SRV_OcclusionTexture, SRV_HistoryTexture, SRV_MotionTexture;
SamplerState Global_point_clamp_sampler, Global_bilinear_clamp_sampler, Global_trilinear_clamp_sampler, Global_point_repeat_sampler, Global_bilinear_repeat_sampler, Global_trilinear_repeat_sampler;

static const int2 SampleOffset[9] = { int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(0, 0), int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1) };


float Square(float x)
{
    return x * x;
}

float2 Square(float2 x)
{
    return x * x;
}

float3 Square(float3 x)
{
    return x * x;
}

float4 Square(float4 x)
{
    return x * x;
}

float pow2(float x)
{
    return x * x;
}

float2 pow2(float2 x)
{
    return x * x;
}

float3 pow2(float3 x)
{
    return x * x;
}

float4 pow2(float4 x)
{
    return x * x;
}

float pow3(float x)
{
    return x * x * x;
}

float2 pow3(float2 x)
{
    return x * x * x;
}

float3 pow3(float3 x)
{
    return x * x * x;
}

float4 pow3(float4 x)
{
    return x * x * x;
}

float pow4(float x)
{
    float xx = x * x;
    return xx * xx;
}

float2 pow4(float2 x)
{
    float2 xx = x * x;
    return xx * xx;
}

float3 pow4(float3 x)
{
    float3 xx = x * x;
    return xx * xx;
}

float4 pow4(float4 x)
{
    float4 xx = x * x;
    return xx * xx;
}

float pow5(float x)
{
    float xx = x * x;
    return xx * xx * x;
}

float2 pow5(float2 x)
{
    float2 xx = x * x;
    return xx * xx * x;
}

float3 pow5(float3 x)
{
    float3 xx = x * x;
    return xx * xx * x;
}

float4 pow5(float4 x)
{
    float4 xx = x * x;
    return xx * xx * x;
}

float pow6(float x)
{
    float xx = x * x;
    return xx * xx * xx;
}

float2 pow6(float2 x)
{
    float2 xx = x * x;
    return xx * xx * xx;
}

float3 pow6(float3 x)
{
    float3 xx = x * x;
    return xx * xx * xx;
}

float4 pow6(float4 x)
{
    float4 xx = x * x;
    return xx * xx * xx;
}
float min3(float a, float b, float c)
{
    return min(min(a, b), c);
}

float max3(float a, float b, float c)
{
    return max(a, max(b, c));
}

float4 min3(float4 a, float4 b, float4 c)
{
    return float4(
        min3(a.x, b.x, c.x),
        min3(a.y, b.y, c.y),
        min3(a.z, b.z, c.z),
        min3(a.w, b.w, c.w));
}

float4 max3(float4 a, float4 b, float4 c)
{
    return float4(
        max3(a.x, b.x, c.x),
        max3(a.y, b.y, c.y),
        max3(a.z, b.z, c.z),
        max3(a.w, b.w, c.w));
}

float Luma4(float3 Color)
{
    return (Color.g * 2) + (Color.r + Color.b);
}

float HdrWeight4(float3 Color, float Exposure)
{
    return rcp(Luma4(Color) * Exposure + 4);
}

float acosFast(float inX)
{
    float x = abs(inX);
    float res = -0.156583f * x + (0.5 * PI);
    res *= sqrt(1 - x);
    return (inX >= 0) ? res : PI - res;
}

float asinFast(float x)
{
    return (0.5 * PI) - acosFast(x);
}

float3 GetViewSpaceNormal(float3 normal, float4x4 matrix_WorldToView)
{
    float3 viewNormal = mul((float3x3)matrix_WorldToView, normal.rgb);
    return normalize(viewNormal);
}

float3 GetScreenSpacePos(float2 uv, float depth)
{
    return float3(uv * 2 - 1, depth);
}

float3 GetWorldSpacePos(float3 screenPos, float4x4 matrix_InvViewProj)
{
    float4 worldPos = mul( matrix_InvViewProj, float4(screenPos, 1) );
    return worldPos.xyz / worldPos.w;
}

float3 GetViewSpacePos(float3 screenPos, float4x4 matrix_InvProj)
{
    float4 viewPos = mul(matrix_InvProj, float4(screenPos, 1));
    return viewPos.xyz / viewPos.w;
}

float3 GetViewSpacePosInvZ(float3 screenPos, float4x4 matrix_InvProj)
{
    float4 viewPos = mul(matrix_InvProj, float4(screenPos, 1));
	viewPos.xyz /= viewPos.w;;
	viewPos.z = -viewPos.z;
    return viewPos.xyz;
}

float3 GetViewDir(float3 worldPos, float3 viewPivot)
{
    return normalize(worldPos - viewPivot);
}

float2 GetMotionVector(float sceneDepth, float2 screenUV, float4x4 matrix_InvViewProj, float4x4 matrix_ViewProj, float4x4 matrix_LastViewProj)
{
    float3 ScreenPos = GetScreenSpacePos(screenUV, sceneDepth);
    float4 worldPos = float4(GetWorldSpacePos(ScreenPos, matrix_InvViewProj), 1);

    float4 prevClipPos = mul(matrix_LastViewProj, worldPos);
    float4 curClipPos = mul(matrix_ViewProj, worldPos);

    float2 prevHPos = prevClipPos.xy / prevClipPos.w;
    float2 curHPos = curClipPos.xy / curClipPos.w;

    float2 vPosPrev = (prevHPos.xy + 1) / 2;
    float2 vPosCur = (curHPos.xy + 1) / 2;
    return vPosCur - vPosPrev;
}

#endif
