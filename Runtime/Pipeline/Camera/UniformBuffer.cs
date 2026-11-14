using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    /// <summary>
    /// One camera's record that raster and compute passes read alike (<see cref="ViewUniform"/>, <see cref="ShadowUniform"/>,
    /// <see cref="LightLoopUniform"/>). It is written once per camera per frame on the graphics queue before the camera's
    /// graph and bound globally under its cbuffer name, which is the record's type name. Each camera owns its buffers;
    /// RenderGraph joins the async queue at the end of every graph, so a later upload never overtakes a reader.
    /// </summary>
    internal sealed class UniformBuffer<T> : IDisposable where T : unmanaged
    {
        static readonly int s_NameID = Shader.PropertyToID(typeof(T).Name);
        static readonly int s_Size = UnsafeUtility.SizeOf<T>();

        readonly GraphicsBuffer m_Buffer;
        readonly T[] m_Staging = new T[1];

        internal UniformBuffer()
        {
            if (s_Size % 16 != 0)
            {
                throw new ArgumentException($"{typeof(T).Name} is {s_Size} bytes; a constant buffer record is a multiple of 16 bytes.");
            }
            m_Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, s_Size);
        }

        internal void Upload(CommandBuffer commands, in T record)
        {
            m_Staging[0] = record;
            commands.SetBufferData(m_Buffer, m_Staging);
            commands.SetGlobalConstantBuffer(m_Buffer, s_NameID, 0, s_Size);
        }

        // Released directly, not behind the state's reuse fence: the fence guards pooled resources handed to a new
        // owner, and this buffer never enters the pool. Unity destroys a released buffer after the GPU is done with it.
        public void Dispose() => m_Buffer.Release();
    }
}
