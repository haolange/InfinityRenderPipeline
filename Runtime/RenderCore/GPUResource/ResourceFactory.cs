using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public enum EAllocator
    {
        //in 4 frame if not use will be dispose
        Tem = 0,

        //release by Menu
        Persistent = 1
    }

    public class FResourceFactory
    {
        FBufferPool m_BufferPool;
        FTexturePool m_TexturePool;

        public FResourceFactory()
        {
            m_BufferPool = new FBufferPool();
            m_TexturePool = new FTexturePool();
        }

        internal void Reset()
        {

        }

        public BufferRef AllocateBuffer(in BufferDescription Description, EAllocator Allocator = EAllocator.Persistent)
        {
            ComputeBuffer Buffer;
            int Handle = Description.GetHashCode();

            if (!m_BufferPool.Pull(Handle, out Buffer))
            {
                Buffer = new ComputeBuffer(Description.count, Description.stride, Description.type);
            }

            return new BufferRef(Handle, Buffer);
        }

        internal void ReleaseBuffer(in BufferRef BufferHandle)
        {
            m_BufferPool.Push(BufferHandle.Handle, BufferHandle.Buffer);
        }

        public TextureRef AllocateTexture(in TextureDescription Description)
        {
            RTHandle Texture;
            int Handle = Description.GetHashCode();

            if (!m_TexturePool.Pull(Handle, out Texture))
            {
                Texture = RTHandles.Alloc(Description.width, Description.height, Description.slices, (DepthBits)Description.depthBufferBits, Description.colorFormat, Description.filterMode, Description.wrapMode, Description.dimension, Description.enableRandomWrite,
                                          Description.useMipMap, Description.autoGenerateMips, Description.isShadowMap, Description.anisoLevel, Description.mipMapBias, (MSAASamples)Description.msaaSamples, Description.bindTextureMS, false, RenderTextureMemoryless.None, Description.name);
            }

            return new TextureRef(Handle, Texture);
        }

        public void ReleaseTexture(in TextureRef TextureHandle)
        {
            m_TexturePool.Push(TextureHandle.Handle, TextureHandle.Texture);
        }

        public void Disposed()
        {
            m_BufferPool.Disposed();
            m_TexturePool.Disposed();
        }
    }
}
