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

        /*internal static RenderTextureDescriptor GetRTDescription(this TextureDescription description)
        {
            RenderTextureDescriptor rtDescription = new RenderTextureDescriptor(description.width, description.height, description.colorFormat, (int)description.depthBufferBits, -1);
            rtDescription.vrUsage = VRTextureUsage.None;
            rtDescription.volumeDepth = description.slices;
            rtDescription.useMipMap = description.useMipMap;
            rtDescription.dimension = description.dimension;
            rtDescription.stencilFormat = GraphicsFormat.None;
            rtDescription.bindMS = description.bindTextureMS;
            rtDescription.depthStencilFormat = GraphicsFormat.None;
            rtDescription.memoryless = RenderTextureMemoryless.None;
            rtDescription.msaaSamples = (int)description.msaaSamples;
            rtDescription.shadowSamplingMode = ShadowSamplingMode.None;
            rtDescription.autoGenerateMips = description.autoGenerateMips;
            rtDescription.autoGenerateMips = description.autoGenerateMips;
            rtDescription.enableRandomWrite = description.enableRandomWrite;
            return rtDescription;
        }*/
    }

    public class FHistoryCache
    {
        Dictionary<int, FBufferRef> m_CacheBuffers;
        Dictionary<int, FTextureRef> m_CacheTextures;

        public FHistoryCache() 
        {
            m_CacheBuffers = new Dictionary<int, FBufferRef>();
            m_CacheTextures = new Dictionary<int, FTextureRef>(); 
        }

        public FBufferRef GetBuffer(in int id, in FBufferDescription description)
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
                bufferRef.buffer = new ComputeBuffer(description.count, description.stride);
                m_CacheBuffers[id] = bufferRef;
            }

            FBufferDescription bufferDescription = new FBufferDescription(bufferRef.buffer.count, bufferRef.buffer.stride);
            if (!description.Equals(bufferDescription))
            {
                bufferRef.buffer.Release();
                bufferRef.buffer = new ComputeBuffer(description.count, description.stride); 
                m_CacheBuffers[id] = bufferRef;
            }
            return bufferRef;
        }

        public FTextureRef GetTexture(in int id, in FTextureDescription description)
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
                textureRef.texture = RTHandles.Alloc(description.width, description.height, description.slices, (DepthBits)description.depthBufferBits, description.colorFormat, description.filterMode, description.wrapMode, description.dimension, description.enableRandomWrite,
                                                             description.useMipMap, description.autoGenerateMips, description.isShadowMap, description.anisoLevel, description.mipMapBias, (MSAASamples)description.msaaSamples, description.bindTextureMS, false, RenderTextureMemoryless.None, description.name);
                m_CacheTextures[id] = textureRef;
            }

            RenderTextureDescriptor rtDescription = description;
            if (!rtDescription.Equal(textureRef.texture.rt.descriptor))
            {
                RTHandles.Release(textureRef.texture);
                textureRef.texture = RTHandles.Alloc(description.width, description.height, description.slices, (DepthBits)description.depthBufferBits, description.colorFormat, description.filterMode, description.wrapMode, description.dimension, description.enableRandomWrite,
                                                             description.useMipMap, description.autoGenerateMips, description.isShadowMap, description.anisoLevel, description.mipMapBias, (MSAASamples)description.msaaSamples, description.bindTextureMS, false, RenderTextureMemoryless.None, description.name);
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
