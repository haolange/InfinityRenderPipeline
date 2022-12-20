using Unity.Collections;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    internal class FRDGResourceMap<Type> where Type : unmanaged
    {
        internal NativeParallelHashMap<int, Type> m_ResourceMap;

        internal FRDGResourceMap()
        {
            m_ResourceMap = new NativeParallelHashMap<int, Type>(64, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Set(in int key, in Type value)
        {
            m_ResourceMap.TryAdd(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Type Get(in int key)
        {
            Type output;
            m_ResourceMap.TryGetValue(key, out output);
            return output;
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


    public class RDGScoper
    {
        RDGBuilder m_GraphBuilder;
        FRDGResourceMap<RDGBufferRef> m_BufferMap;
        FRDGResourceMap<RDGTextureRef> m_TextureMap;

        public RDGScoper(RDGBuilder graphBuilder)
        {
            m_GraphBuilder = graphBuilder;
            m_BufferMap = new FRDGResourceMap<RDGBufferRef>();
            m_TextureMap = new FRDGResourceMap<RDGTextureRef>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGBufferRef QueryBuffer(in int handle)
        {
            return m_BufferMap.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterBuffer(int handle, in RDGBufferRef bufferRef)
        {
            m_BufferMap.Set(handle, bufferRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGBufferRef CreateBuffer(in int handle, in BufferDescriptor descriptor)
        {
            RDGBufferRef bufferRef = m_GraphBuilder.CreateBuffer(descriptor);
            RegisterBuffer(handle, bufferRef);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGTextureRef QueryTexture(in int handle)
        {
            return m_TextureMap.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterTexture(int handle, in RDGTextureRef textureRef)
        {
            m_TextureMap.Set(handle, textureRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RDGTextureRef CreateAndRegisterTexture(in int handle, in TextureDescriptor descriptor)
        {
            RDGTextureRef textureRef = m_GraphBuilder.CreateTexture(descriptor, handle);
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
    }
}
