using Unity.Collections;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    internal class FRDGResourceMap<Type> where Type : struct
    {
        internal NativeHashMap<int, Type> m_ResourceMap;

        internal FRDGResourceMap()
        {
            m_ResourceMap = new NativeHashMap<int, Type>(64, Allocator.Persistent);
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


    public class FRDGScoper
    {
        FRDGBuilder m_GraphBuilder;
        FRDGResourceMap<FRDGBufferRef> m_BufferMap;
        FRDGResourceMap<FRDGTextureRef> m_TextureMap;

        public FRDGScoper(FRDGBuilder graphBuilder)
        {
            m_GraphBuilder = graphBuilder;
            m_BufferMap = new FRDGResourceMap<FRDGBufferRef>();
            m_TextureMap = new FRDGResourceMap<FRDGTextureRef>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef QueryBuffer(in int handle)
        {
            return m_BufferMap.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterBuffer(int handle, in FRDGBufferRef bufferRef)
        {
            m_BufferMap.Set(handle, bufferRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef CreateBuffer(in int handle, in FBufferDescriptor descriptor)
        {
            FRDGBufferRef bufferRef = m_GraphBuilder.CreateBuffer(descriptor);
            RegisterBuffer(handle, bufferRef);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef QueryTexture(in int handle)
        {
            return m_TextureMap.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterTexture(int handle, in FRDGTextureRef textureRef)
        {
            m_TextureMap.Set(handle, textureRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef CreateAndRegisterTexture(in int handle, in FTextureDescriptor descriptor)
        {
            FRDGTextureRef textureRef = m_GraphBuilder.CreateTexture(descriptor, handle);
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
