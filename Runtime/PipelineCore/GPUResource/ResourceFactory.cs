using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public class FResourceFactory
    {
        FBufferPool m_BufferPool;
        FTexturePool m_TexturePool;

        public FResourceFactory()
        {
            m_BufferPool = new FBufferPool();
            m_TexturePool = new FTexturePool();
        }

        public BufferRef AllocateBuffer(in BufferDescription description)
        {
            ComputeBuffer buffer;
            int handle = description.GetHashCode();

            if (!m_BufferPool.Pull(handle, out buffer))
            {
                buffer = new ComputeBuffer(description.count, description.stride, description.type);
                buffer.name = description.name;
            }

            return new BufferRef(handle, buffer);
        }

        public void ReleaseBuffer(in BufferRef bufferHandle)
        {
            m_BufferPool.Push(bufferHandle.handle, bufferHandle.buffer);
        }

        public TextureRef AllocateTexture(in TextureDescription description)
        {
            RTHandle texture;
            int handle = description.GetHashCode();

            if (!m_TexturePool.Pull(handle, out texture))
            {
                texture = RTHandles.Alloc(description.width, description.height, description.slices, (DepthBits)description.depthBufferBits, description.colorFormat, description.filterMode, description.wrapMode, description.dimension, description.enableRandomWrite,
                                          description.useMipMap, description.autoGenerateMips, description.isShadowMap, description.anisoLevel, description.mipMapBias, (MSAASamples)description.msaaSamples, description.bindTextureMS, false, RenderTextureMemoryless.None, description.name);
            }

            return new TextureRef(handle, texture);
        }

        public void ReleaseTexture(in TextureRef textureHandle)
        {
            m_TexturePool.Push(textureHandle.handle, textureHandle.texture);
        }

        public void Disposed()
        {
            m_BufferPool.Disposed();
            m_TexturePool.Disposed();
        }
    }
}
