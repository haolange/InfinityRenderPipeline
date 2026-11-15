using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // One camera's view constants as the GPU reads them. CameraUniform owns the values; this record is their upload.
    // GPU-RECORD: ViewUniform Shaders/Library/ViewUniform.hlsl
    internal struct ViewUniform
    {
        public Matrix4x4 matrix_WorldToView;
        public Matrix4x4 matrix_ViewToWorld;
        public Matrix4x4 matrix_Proj;
        public Matrix4x4 matrix_InvProj;
        public Matrix4x4 matrix_FlipYProj;
        public Matrix4x4 matrix_FlipYJitterProj;
        public Matrix4x4 matrix_InvFlipYJitterProj;
        public Matrix4x4 matrix_ViewJitterProj;
        public Matrix4x4 matrix_InvViewJitterProj;
        public Matrix4x4 matrix_ViewFlipYProj;
        public Matrix4x4 matrix_InvViewFlipYProj;
        public Matrix4x4 matrix_ViewFlipYJitterProj;
        public Matrix4x4 matrix_InvViewFlipYJitterProj;
        public Matrix4x4 matrix_LastViewFlipYProj;
        public Matrix4x4 matrix_LastViewFlipYJitterProj;
        // Near, far, orthographic (1 or 0), reversed Z (1 or 0).
        public Vector4 view_DepthParameters;
        // TAA offsets in sampled framebuffer UV: this frame in xy, the previous committed frame in zw.
        public Vector4 view_JitterUV;
    }
}
