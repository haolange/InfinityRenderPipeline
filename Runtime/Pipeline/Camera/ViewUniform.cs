using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;

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
    }

    // The camera's ViewUniform constant buffer. It is written once per camera per frame before recording and bound
    // globally, so raster and compute passes, async ones included, read it without per-pass copies. Each camera owns
    // its buffer; RenderGraph joins the async queue at the end of every graph, so a later upload on the graphics
    // queue never overtakes a reader of an earlier one.
    internal sealed class ViewUniformBuffer : IDisposable
    {
        static readonly int s_ViewUniformID = Shader.PropertyToID("ViewUniform");
        static readonly int s_Size = UnsafeUtility.SizeOf<ViewUniform>();

        readonly GraphicsBuffer m_Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, s_Size);
        readonly ViewUniform[] m_Staging = new ViewUniform[1];

        internal void Upload(CommandBuffer commands, in ViewUniform record)
        {
            m_Staging[0] = record;
            commands.SetBufferData(m_Buffer, m_Staging);
            commands.SetGlobalConstantBuffer(m_Buffer, s_ViewUniformID, 0, s_Size);
        }

        // Released directly, not behind the state's reuse fence: the fence guards pooled resources handed to a new
        // owner, and this buffer never enters the pool. Unity destroys a released buffer after the GPU is done with it.
        public void Dispose() => m_Buffer.Release();
    }
}
