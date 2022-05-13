using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace InfinityTech.Rendering.GPUResource
{
    internal static class TextureExtension
    {
        internal static bool Equal(this RenderTextureDescriptor a, in RenderTextureDescriptor b)
        {
            bool dif = false;
            dif |= a.autoGenerateMips != b.autoGenerateMips;
            dif |= a.bindMS != b.bindMS;
            dif |= a.colorFormat != b.colorFormat;
            dif |= a.depthBufferBits != b.depthBufferBits;
            dif |= a.dimension != b.dimension;
            dif |= a.enableRandomWrite != b.enableRandomWrite;
            dif |= a.graphicsFormat != b.graphicsFormat;
            dif |= a.height != b.height;
            dif |= a.memoryless != b.memoryless;
            dif |= a.mipCount != b.mipCount;
            dif |= a.msaaSamples != b.msaaSamples;
            dif |= a.shadowSamplingMode != b.shadowSamplingMode;
            dif |= a.sRGB != b.sRGB;
            dif |= a.stencilFormat != b.stencilFormat;
            dif |= a.useDynamicScale != b.useDynamicScale;
            dif |= a.useMipMap != b.useMipMap;
            dif |= a.volumeDepth != b.volumeDepth;
            dif |= a.vrUsage != b.vrUsage;
            dif |= a.width != b.width;
            return !dif;
        }

        /*internal static RenderTextureDescriptor GetRTDescriptor(this TextureDescriptor descriptor)
        {
            RenderTextureDescriptor rtDescriptor = new RenderTextureDescriptor(descriptor.width, descriptor.height, descriptor.colorFormat, (int)descriptor.depthBufferBits, -1);
            rtDescriptor.vrUsage = VRTextureUsage.None;
            rtDescriptor.volumeDepth = descriptor.slices;
            rtDescriptor.useMipMap = descriptor.useMipMap;
            rtDescriptor.dimension = descriptor.dimension;
            rtDescriptor.stencilFormat = GraphicsFormat.None;
            rtDescriptor.bindMS = descriptor.bindTextureMS;
            rtDescriptor.depthStencilFormat = GraphicsFormat.None;
            rtDescriptor.memoryless = RenderTextureMemoryless.None;
            rtDescriptor.msaaSamples = (int)descriptor.msaaSamples;
            rtDescriptor.shadowSamplingMode = ShadowSamplingMode.None;
            rtDescriptor.autoGenerateMips = descriptor.autoGenerateMips;
            rtDescriptor.autoGenerateMips = descriptor.autoGenerateMips;
            rtDescriptor.enableRandomWrite = descriptor.enableRandomWrite;
            return rtDescriptor;
        }*/
    }

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
            FBufferRef bufferRef = new FBufferRef(-1, null);
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
                m_CacheBuffers[id] = bufferRef;
            }

            BufferDescriptor bufferDescriptor = new BufferDescriptor(bufferRef.buffer.count, bufferRef.buffer.stride);
            if (!descriptor.Equals(bufferDescriptor))
            {
                bufferRef.buffer.Release();
                bufferRef.buffer = new ComputeBuffer(descriptor.count, descriptor.stride); 
                m_CacheBuffers[id] = bufferRef;
            }
            return bufferRef;
        }

        public FTextureRef GetTexture(in int id, in TextureDescriptor descriptor)
        {
            FTextureRef textureRef = new FTextureRef(-1, null);
            if (m_CacheTextures.ContainsKey(id))
            {
                textureRef = m_CacheTextures[id];
            }

            if (textureRef.texture == null)
            {
                if (textureRef.texture != null)
                {
                    RTHandles.Release(textureRef.texture);
                }
                textureRef.texture = RTHandles.Alloc(descriptor.width, descriptor.height, descriptor.slices, (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, descriptor.filterMode, descriptor.wrapMode, descriptor.dimension, descriptor.enableRandomWrite,
                                                             descriptor.useMipMap, descriptor.autoGenerateMips, descriptor.isShadowMap, descriptor.anisoLevel, descriptor.mipMapBias, (MSAASamples)descriptor.msaaSamples, descriptor.bindTextureMS, false, RenderTextureMemoryless.None, VRTextureUsage.None, descriptor.name);
                m_CacheTextures[id] = textureRef;
            }

            RenderTextureDescriptor rtDescriptor = descriptor;
            if (!rtDescriptor.Equal(textureRef.texture.rt.descriptor))
            {
                RTHandles.Release(textureRef.texture);
                textureRef.texture = RTHandles.Alloc(descriptor.width, descriptor.height, descriptor.slices, (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, descriptor.filterMode, descriptor.wrapMode, descriptor.dimension, descriptor.enableRandomWrite,
                                                             descriptor.useMipMap, descriptor.autoGenerateMips, descriptor.isShadowMap, descriptor.anisoLevel, descriptor.mipMapBias, (MSAASamples)descriptor.msaaSamples, descriptor.bindTextureMS, false, RenderTextureMemoryless.None, VRTextureUsage.None, descriptor.name);
                m_CacheTextures[id] = textureRef;
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
