using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace InfinityTech.Rendering.GPUResource
{
    public class HistoryCache
    {
        Dictionary<int, FBufferRef> m_CacheBuffers;
        Dictionary<int, FTextureRef> m_CacheTextures;

        public HistoryCache() 
        {
            m_CacheBuffers = new Dictionary<int, FBufferRef>();
            m_CacheTextures = new Dictionary<int, FTextureRef>(); 
        }

        public FBufferRef GetBuffer(in int id, in BufferDescriptor descriptor)
        {
            FBufferRef bufferRef = new FBufferRef(-1, default, null);
            if (m_CacheBuffers.ContainsKey(id))
            {
                bufferRef = m_CacheBuffers[id];
            }

            if (bufferRef.buffer == null)
            {
                if (bufferRef.buffer != null)
                {
                    bufferRef.buffer.Release();
                }
                bufferRef.buffer = new ComputeBuffer(descriptor.count, descriptor.stride);
                bufferRef.descriptor = descriptor;
                m_CacheBuffers[id] = bufferRef;
            }

            BufferDescriptor bufferDescriptor = new BufferDescriptor(bufferRef.buffer.count, bufferRef.buffer.stride);
            if (!descriptor.Equals(bufferDescriptor))
            {
                bufferRef.buffer.Release();
                bufferRef.buffer = new ComputeBuffer(descriptor.count, descriptor.stride);
                bufferRef.descriptor = descriptor;
                m_CacheBuffers[id] = bufferRef;
            }
            return bufferRef;
        }

        public FTextureRef GetTexture(in int id, in TextureDescriptor descriptor)
        {
            return GetTexture(id, descriptor, out _);
        }

        public FTextureRef GetTexture(in int id, in TextureDescriptor descriptor, out bool created)
        {
            created = false;
            FTextureRef textureRef = new FTextureRef(-1, default, null);
            if (m_CacheTextures.ContainsKey(id))
            {
                textureRef = m_CacheTextures[id];
            }

            if (textureRef.texture == null)
            {
                textureRef.texture = RTHandles.Alloc(descriptor.width, descriptor.height, descriptor.slices, (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, descriptor.filterMode, descriptor.wrapMode, descriptor.dimension, descriptor.enableRandomWrite,
                                                             descriptor.useMipMap, descriptor.autoGenerateMips, descriptor.isShadowMap, descriptor.anisoLevel, descriptor.mipMapBias, (MSAASamples)descriptor.msaaSamples, descriptor.bindTextureMS, false, false, RenderTextureMemoryless.None, VRTextureUsage.None, descriptor.name);
                textureRef.descriptor = descriptor;
                m_CacheTextures[id] = textureRef;
                created = true;
                return textureRef;
            }

            if (!descriptor.Equals(textureRef.descriptor))
            {
                RTHandles.Release(textureRef.texture);
                textureRef.texture = RTHandles.Alloc(descriptor.width, descriptor.height, descriptor.slices, (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, descriptor.filterMode, descriptor.wrapMode, descriptor.dimension, descriptor.enableRandomWrite,
                                                             descriptor.useMipMap, descriptor.autoGenerateMips, descriptor.isShadowMap, descriptor.anisoLevel, descriptor.mipMapBias, (MSAASamples)descriptor.msaaSamples, descriptor.bindTextureMS, false, false,RenderTextureMemoryless.None, VRTextureUsage.None, descriptor.name);
                textureRef.descriptor = descriptor;
                m_CacheTextures[id] = textureRef;
                created = true;
            }
            return textureRef;
        }

        public void Release()
        {
            foreach (var pair in m_CacheBuffers)
            {
                if (pair.Value.buffer != null)
                {
                    pair.Value.buffer.Release();
                }
            }
            m_CacheBuffers.Clear();

            foreach (var pair in m_CacheTextures)
            {
                if (pair.Value.texture != null)
                {
                    pair.Value.texture.Release();
                }
            }
            m_CacheTextures.Clear();
        }
    }
}
