using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.RenderGraph
{
    /// <summary>
    /// Constants one pass recorded: a GPU-RECORD in the graph's staging memory and the pass-local constant buffer the
    /// graph uploads it into when the pass begins. The buffer handle carries the graph generation, so a handle from an
    /// earlier graph does not resolve.
    /// </summary>
    public readonly struct RGConstantsHandle
    {
        internal readonly RGBufferHandle bufferHandle;
        internal readonly int offset;
        internal readonly int units;

        internal RGConstantsHandle(in RGBufferHandle bufferHandle, int offset, int units)
        {
            this.bufferHandle = bufferHandle;
            this.offset = offset;
            this.units = units;
        }

        internal int size => units * RGConstantStaging.Unit;
    }

    /// <summary>
    /// CPU copies of the constants every pass of one graph records, in 16-byte units, the unit of a constant buffer.
    /// Each record goes to its pass's own pooled constant buffer on that pass's queue, so the graph's resource
    /// lifetimes and queue fences cover it like any pass-local buffer.
    /// </summary>
    internal sealed class RGConstantStaging : IDisposable
    {
        internal const int Unit = 16;

        NativeList<float4> m_Units = new NativeList<float4>(256, Allocator.Persistent);

        internal unsafe int Write<T>(in T constants, out int units) where T : unmanaged
        {
            int size = UnsafeUtility.SizeOf<T>();
            if (size % Unit != 0)
            {
                throw new ArgumentException($"{typeof(T).Name} is {size} bytes; a constant buffer record is a multiple of {Unit} bytes.");
            }
            int offset = m_Units.Length;
            units = size / Unit;
            m_Units.Resize(offset + units, NativeArrayOptions.UninitializedMemory);
            T record = constants;
            UnsafeUtility.CopyStructureToPtr(ref record, (float4*)m_Units.GetUnsafePtr() + offset);
            return offset;
        }

        internal void Upload(CommandBuffer commands, GraphicsBuffer buffer, in RGConstantsHandle constantsHandle)
            => commands.SetBufferData(buffer, m_Units.AsArray(), constantsHandle.offset, 0, constantsHandle.units);

        internal void Clear() => m_Units.Clear();

        public void Dispose() => m_Units.Dispose();
    }

    // The debug name of a constant buffer is its record type, resolved once per type.
    static class RGConstantsName<T>
    {
        internal static readonly string value = typeof(T).Name;
    }
}
