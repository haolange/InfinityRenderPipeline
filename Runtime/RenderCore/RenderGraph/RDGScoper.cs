using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    public class FRDGScoper
    {
        FRDGBuilder m_GraphBuilder;
        FRDGResourceScoper<FRDGBufferRef> m_BufferScoper;
        FRDGResourceScoper<FRDGTextureRef> m_TextureScoper;

        public FRDGScoper(FRDGBuilder graphBuilder)
        {
            m_GraphBuilder = graphBuilder;
            m_BufferScoper = new FRDGResourceScoper<FRDGBufferRef>();
            m_TextureScoper = new FRDGResourceScoper<FRDGTextureRef>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef QueryBuffer(in int handle)
        {
            return m_BufferScoper.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterBuffer(int handle, in FRDGBufferRef bufferRef)
        {
            m_BufferScoper.Set(handle, bufferRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGBufferRef CreateBuffer(in int handle, in FBufferDescription description)
        {
            FRDGBufferRef bufferRef = m_GraphBuilder.CreateBuffer(description);
            RegisterBuffer(handle, bufferRef);
            return bufferRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef QueryTexture(in int handle)
        {
            return m_TextureScoper.Get(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterTexture(int handle, in FRDGTextureRef textureRef)
        {
            m_TextureScoper.Set(handle, textureRef);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FRDGTextureRef CreateAndRegisterTexture(in int handle, in FTextureDescription description)
        {
            FRDGTextureRef textureRef = m_GraphBuilder.CreateTexture(description, handle);
            RegisterTexture(handle, textureRef);
            return textureRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            m_BufferScoper.Clear();
            m_TextureScoper.Clear();
        }

        public void Dispose()
        {
            m_BufferScoper.Dispose();
            m_TextureScoper.Dispose();
        }
    }
}
