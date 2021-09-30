using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public class FResourcePool
    {
        FBufferCache m_BufferPool;
        FTextureCache m_TexturePool;

        public FResourcePool()
        {
            m_BufferPool = new FBufferCache();
            m_TexturePool = new FTextureCache();
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

        public void ReleaseBuffer(in BufferRef bufferRef)
        {
            m_BufferPool.Push(bufferRef.handle, bufferRef.buffer);
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

        public void ReleaseTexture(in TextureRef textureRef)
        {
            m_TexturePool.Push(textureRef.handle, textureRef.texture);
        }

        public void Dispose()
        {
            m_BufferPool.Dispose();
            m_TexturePool.Dispose();
        }
    }
}
