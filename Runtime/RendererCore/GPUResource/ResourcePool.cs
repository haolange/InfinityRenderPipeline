using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public class ResourcePool
    {
        BufferCache m_BufferPool;
        TextureCache m_TexturePool;

        public ResourcePool()
        {
            m_BufferPool = new BufferCache();
            m_TexturePool = new TextureCache();
        }

        public FBufferRef GetBuffer(in BufferDescriptor descriptor)
        {
            ComputeBuffer buffer;
            int handle = descriptor.GetHashCode();

            if (!m_BufferPool.Pull(handle, descriptor, out buffer))
            {
                buffer = new ComputeBuffer(descriptor.count, descriptor.stride, descriptor.type);
                buffer.name = descriptor.name;
            }

            return new FBufferRef(handle, descriptor, buffer);
        }

        public void ReleaseBuffer(in FBufferRef bufferRef)
        {
            m_BufferPool.Push(bufferRef.handle, bufferRef.descriptor, bufferRef.buffer);
        }

        public FTextureRef GetTexture(in TextureDescriptor descriptor)
        {
            RTHandle texture;
            int handle = descriptor.GetHashCode();

            if (!m_TexturePool.Pull(handle, descriptor, out texture))
            {
                texture = RTHandles.Alloc(descriptor.width, descriptor.height, descriptor.slices, (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, descriptor.filterMode, descriptor.wrapMode, descriptor.dimension, descriptor.enableRandomWrite,
                                          descriptor.useMipMap, descriptor.autoGenerateMips, descriptor.isShadowMap, descriptor.anisoLevel, descriptor.mipMapBias, (MSAASamples)descriptor.msaaSamples, descriptor.bindTextureMS, false, false, RenderTextureMemoryless.None, VRTextureUsage.None, descriptor.name);
            }

            return new FTextureRef(handle, descriptor, texture);
        }

        public void ReleaseTexture(in FTextureRef textureRef)
        {
            m_TexturePool.Push(textureRef.handle, textureRef.descriptor, textureRef.texture);
        }

        public void Dispose()
        {
            m_BufferPool.Dispose();
            m_TexturePool.Dispose();
        }
    }
}
