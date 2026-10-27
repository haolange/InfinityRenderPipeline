using System;
using Unity.Collections;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RenderGraph
{
    internal class FRGResourceMap<Type> where Type : unmanaged
    {
        internal NativeParallelHashMap<int, Type> m_ResourceMap;

        internal FRGResourceMap()
        {
            m_ResourceMap = new NativeParallelHashMap<int, Type>(64, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Set(in int key, in Type value)
        {
            m_ResourceMap[key] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGet(in int key, out Type value)
        {
            return m_ResourceMap.TryGetValue(key, out value);
        }

        internal bool Remove(in int key)
        {
            return m_ResourceMap.Remove(key);
        }

        internal void Clear()
        {
            m_ResourceMap.Clear();
        }

        internal void Dispose()
        {
            m_ResourceMap.Dispose();
        }
    }


    public class RGScoper
    {
        RGBuilder m_RGBuilder;
        FRGResourceMap<RGBufferHandle> m_BufferMap;
        FRGResourceMap<RGTextureHandle> m_TextureMap;

        public RGScoper(RGBuilder graphBuilder)
        {
            m_RGBuilder = graphBuilder;
            m_BufferMap = new FRGResourceMap<RGBufferHandle>();
            m_TextureMap = new FRGResourceMap<RGTextureHandle>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryQueryBuffer(in int handle, out RGBufferHandle bufferHandle)
        {
            if (m_BufferMap.TryGet(handle, out bufferHandle) && bufferHandle.IsValid())
            {
                return true;
            }

            bufferHandle = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferHandle QueryBuffer(in int handle)
        {
            if (!TryQueryBuffer(handle, out RGBufferHandle bufferHandle))
            {
                throw new InvalidOperationException($"RGScoper.QueryBuffer: {RGResourceKey.NameOf(handle)} is not registered this frame. Its producer did not record, or the consumer runs before it.");
            }

            return bufferHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterBuffer(int handle, in RGBufferHandle bufferHandle)
        {
            if (m_BufferMap.TryGet(handle, out RGBufferHandle existing) && existing.IsValid() && !SameBufferRef(existing, bufferHandle))
            {
                throw new InvalidOperationException($"RGScoper.RegisterBuffer: {RGResourceKey.NameOf(handle)} already has a different owner this frame.");
            }

            m_BufferMap.Set(handle, bufferHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferHandle CreateBuffer(in int handle, in BufferDescriptor descriptor)
        {
            RGBufferHandle bufferHandle = m_RGBuilder.CreateBuffer(descriptor);
            RegisterBuffer(handle, bufferHandle);
            return bufferHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryQueryTexture(in int handle, out RGTextureHandle textureHandle)
        {
            if (m_TextureMap.TryGet(handle, out textureHandle) && textureHandle.IsValid())
            {
                return true;
            }

            textureHandle = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureHandle QueryTexture(in int handle)
        {
            if (!TryQueryTexture(handle, out RGTextureHandle textureHandle))
            {
                throw new InvalidOperationException($"RGScoper.QueryTexture: {RGResourceKey.NameOf(handle)} is not registered this frame. Its producer did not record, or the consumer runs before it.");
            }

            return textureHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterTexture(int handle, in RGTextureHandle textureHandle)
        {
            if (m_TextureMap.TryGet(handle, out RGTextureHandle existing) && existing.IsValid() && !SameTextureRef(existing, textureHandle))
            {
                throw new InvalidOperationException($"RGScoper.RegisterTexture: {RGResourceKey.NameOf(handle)} already has a different owner this frame.");
            }

            m_TextureMap.Set(handle, textureHandle);
        }

        public void MoveTexture(int from, int to)
        {
            if (from == to)
            {
                return;
            }

            RGTextureHandle textureHandle = QueryTexture(from);
            RegisterTexture(to, textureHandle);
            m_TextureMap.Remove(from);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureHandle CreateAndRegisterTexture(in int handle, in TextureDescriptor descriptor)
        {
            RGTextureHandle textureHandle = m_RGBuilder.CreateTexture(descriptor);
            RegisterTexture(handle, textureHandle);
            return textureHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            m_BufferMap.Clear();
            m_TextureMap.Clear();
        }

        public void Dispose()
        {
            m_BufferMap.Dispose();
            m_TextureMap.Dispose();
        }

        static bool SameTextureRef(in RGTextureHandle a, in RGTextureHandle b)
        {
            return a.IsValid() && b.IsValid() && a.handle.index == b.handle.index && a.handle.type == b.handle.type;
        }

        static bool SameBufferRef(in RGBufferHandle a, in RGBufferHandle b)
        {
            return a.IsValid() && b.IsValid() && a.handle.index == b.handle.index && a.handle.type == b.handle.type;
        }
    }
}
