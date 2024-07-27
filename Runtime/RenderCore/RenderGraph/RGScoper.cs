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
        public RGBufferRef QueryBuffer(in int handle)
        {
            return m_BufferMap.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterBuffer(int handle, in RGBufferRef bufferRef)
        {
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
        public RGTextureRef QueryTexture(in int handle)
        {
            return m_TextureMap.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterTexture(int handle, in RGTextureRef textureRef)
        {
            m_TextureMap.Set(handle, textureRef);
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
    }
}
