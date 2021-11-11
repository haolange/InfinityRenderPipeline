using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

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
        private Dictionary<int, RTHandle> m_CacheTextures;

        public FHistoryCache() 
        {
            m_CacheTextures = new Dictionary<int, RTHandle>(); 
        }

        public RTHandle GetTexture(in int id, in FTextureDescription description)
        {
            RTHandle texture = null;
            if (m_CacheTextures.ContainsKey(id))
            {
                texture = m_CacheTextures[id];
            }

            if (texture == null)
            {
                if (texture != null)
                {
                    RTHandles.Release(texture);
                }
                texture = RTHandles.Alloc(description.width, description.height, description.slices, (DepthBits)description.depthBufferBits, description.colorFormat, description.filterMode, description.wrapMode, description.dimension, description.enableRandomWrite,
                                                             description.useMipMap, description.autoGenerateMips, description.isShadowMap, description.anisoLevel, description.mipMapBias, (MSAASamples)description.msaaSamples, description.bindTextureMS, false, RenderTextureMemoryless.None, description.name);
                m_CacheTextures[id] = texture;
            }

            RenderTextureDescriptor rtDescription = description;
            if (!rtDescription.Equal(texture.rt.descriptor))
            {
                RTHandles.Release(texture);
                texture = RTHandles.Alloc(description.width, description.height, description.slices, (DepthBits)description.depthBufferBits, description.colorFormat, description.filterMode, description.wrapMode, description.dimension, description.enableRandomWrite,
                                                             description.useMipMap, description.autoGenerateMips, description.isShadowMap, description.anisoLevel, description.mipMapBias, (MSAASamples)description.msaaSamples, description.bindTextureMS, false, RenderTextureMemoryless.None, description.name);
                m_CacheTextures[id] = texture;
            }
            return texture;
        }

        public void Release()
        {
            foreach (var pair in m_CacheTextures)
            {
                if (pair.Value != null)
                {
                    pair.Value.Release();
                }
            }
            m_CacheTextures.Clear();
        }
    }
}
