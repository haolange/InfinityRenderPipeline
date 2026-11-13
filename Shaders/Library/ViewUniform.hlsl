#ifndef INFINITY_VIEW_UNIFORM
#define INFINITY_VIEW_UNIFORM

// One camera's view constants. CameraUniform uploads the matching ViewUniform record into the camera's own constant
// buffer once per camera per frame and binds it globally, so raster and compute shaders read the same values. Every
// name states its variant: FlipY is the projection for addressing textures by pixel (compute and full-screen passes),
// Jitter includes this frame's TAA offset, Last is the previous committed frame. ShaderVariables.hlsl maps
// UNITY_MATRIX_P, _I_P, _VP and _I_VP onto Matrix_Proj, Matrix_InvProj, Matrix_ViewJitterProj and Matrix_InvViewJitterProj.
cbuffer ViewUniform
{
    float4x4 Matrix_WorldToView;
    float4x4 Matrix_ViewToWorld;
    float4x4 Matrix_Proj;
    float4x4 Matrix_InvProj;
    float4x4 Matrix_FlipYProj;
    float4x4 Matrix_FlipYJitterProj;
    float4x4 Matrix_InvFlipYJitterProj;
    float4x4 Matrix_ViewJitterProj;
    float4x4 Matrix_InvViewJitterProj;
    float4x4 Matrix_ViewFlipYProj;
    float4x4 Matrix_InvViewFlipYProj;
    float4x4 Matrix_ViewFlipYJitterProj;
    float4x4 Matrix_InvViewFlipYJitterProj;
    float4x4 Matrix_LastViewFlipYProj;
    float4x4 Matrix_LastViewFlipYJitterProj;
};

#endif
