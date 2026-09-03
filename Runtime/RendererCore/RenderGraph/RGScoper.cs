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
        FRGResourceMap<RGBufferRef> m_BufferMap;
        FRGResourceMap<RGTextureRef> m_TextureMap;

        public RGScoper(RGBuilder graphBuilder)
        {
            m_RGBuilder = graphBuilder;
            m_BufferMap = new FRGResourceMap<RGBufferRef>();
            m_TextureMap = new FRGResourceMap<RGTextureRef>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryQueryBuffer(in int handle, out RGBufferRef bufferRef)
        {
            if (m_BufferMap.TryGet(handle, out bufferRef) && bufferRef.IsValid())
            {
                return true;
            }

            bufferRef = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef QueryBuffer(in int handle)
        {
            if (!TryQueryBuffer(handle, out RGBufferRef bufferRef))
            {
                throw new InvalidOperationException($"RGScoper.QueryBuffer: handle {handle} is not registered.");
            }

            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterBuffer(int handle, in RGBufferRef bufferRef)
        {
            if (m_BufferMap.TryGet(handle, out RGBufferRef existing) && existing.IsValid() && !SameBufferRef(existing, bufferRef))
            {
                throw new InvalidOperationException($"RGScoper.RegisterBuffer: handle {handle} already has a different owner this frame.");
            }

            m_BufferMap.Set(handle, bufferRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGBufferRef CreateBuffer(in int handle, in BufferDescriptor descriptor)
        {
            RGBufferRef bufferRef = m_RGBuilder.CreateBuffer(descriptor);
            RegisterBuffer(handle, bufferRef);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryQueryTexture(in int handle, out RGTextureRef textureRef)
        {
            if (m_TextureMap.TryGet(handle, out textureRef) && textureRef.IsValid())
            {
                return true;
            }

            textureRef = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef QueryTexture(in int handle)
        {
            if (!TryQueryTexture(handle, out RGTextureRef textureRef))
            {
                throw new InvalidOperationException($"RGScoper.QueryTexture: handle {handle} is not registered.");
            }

            return textureRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterTexture(int handle, in RGTextureRef textureRef)
        {
            if (m_TextureMap.TryGet(handle, out RGTextureRef existing) && existing.IsValid() && !SameTextureRef(existing, textureRef))
            {
                throw new InvalidOperationException($"RGScoper.RegisterTexture: handle {handle} already has a different owner this frame.");
            }

            m_TextureMap.Set(handle, textureRef);
        }

        public void MoveTexture(int from, int to)
        {
            if (from == to)
            {
                return;
            }

            RGTextureRef textureRef = QueryTexture(from);
            RegisterTexture(to, textureRef);
            m_TextureMap.Remove(from);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RGTextureRef CreateAndRegisterTexture(in int handle, in TextureDescriptor descriptor)
        {
            RGTextureRef textureRef = m_RGBuilder.CreateTexture(descriptor, handle);
            RegisterTexture(handle, textureRef);
            return textureRef;
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

        static bool SameTextureRef(in RGTextureRef a, in RGTextureRef b)
        {
            return a.IsValid() && b.IsValid() && a.handle.index == b.handle.index && a.handle.type == b.handle.type;
        }

        static bool SameBufferRef(in RGBufferRef a, in RGBufferRef b)
        {
            return a.IsValid() && b.IsValid() && a.handle.index == b.handle.index && a.handle.type == b.handle.type;
        }
    }
}
