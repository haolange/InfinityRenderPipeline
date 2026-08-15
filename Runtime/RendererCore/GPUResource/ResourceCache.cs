using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public enum EDepthBits
    {
        None = 0,
        Depth8 = 8,
        Depth16 = 16,
        Depth24 = 24,
        Depth32 = 32
    }

    public enum EMSAASamples
    {
        None = 1,
        MSAA2x = 2,
        MSAA4x = 4,
        MSAA8x = 8
    }

    public struct BufferDescriptor : IEquatable<BufferDescriptor>
    {
        public string name;

        public int count;
        public int stride;
        public ComputeBufferType type;

        public BufferDescriptor(int count, int stride) : this()
        {
            this.count = count;
            this.stride = stride;
            type = ComputeBufferType.Default;
        }

        public BufferDescriptor(int count, int stride, ComputeBufferType type) : this()
        {
            this.type = type;
            this.count = count;
            this.stride = stride;
        }

        public bool Equals(BufferDescriptor target)
        {
            return count == target.count
                && stride == target.stride
                && type == target.type;
        }

        public override bool Equals(object target)
        {
            return target is BufferDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hashCode = 17;
            hashCode = hashCode * 23 + count;
            hashCode = hashCode * 23 + stride;
            hashCode = hashCode * 23 + (int)type;
            return hashCode;
        }
    }

    public struct TextureDescriptor : IEquatable<TextureDescriptor>
    {
        public string name;

        public int width;
        public int height;
        public int slices;
        public EDepthBits depthBufferBits;
        public GraphicsFormat colorFormat;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public TextureDimension dimension;
        public bool enableRandomWrite;
        public bool useMipMap;
        public bool autoGenerateMips;
        public bool isShadowMap;
        public int anisoLevel;
        public float mipMapBias;
        public bool enableMSAA;
        public bool bindTextureMS;
        public EMSAASamples msaaSamples;
        public bool clearBuffer;
        public Color clearColor;

        public TextureDescriptor(int Width, int Height) : this()
        {
            width = Width;
            height = Height;
            slices = 1;

            clearColor = Color.black;
            enableMSAA = false;
            bindTextureMS = false;
            clearBuffer = false;
            isShadowMap = false;
            enableRandomWrite = false;

            msaaSamples = EMSAASamples.None;
            depthBufferBits = EDepthBits.None;
            wrapMode = TextureWrapMode.Repeat;
        }

        public TextureDescriptor(int Width, int Height, int SliceOrDepth) : this()
        {
            width = Width;
            height = Height;
            slices = SliceOrDepth;

            clearColor = Color.black;
            enableMSAA = false;
            bindTextureMS = false;
            clearBuffer = false;
            isShadowMap = false;
            enableRandomWrite = false;

            msaaSamples = EMSAASamples.None;
            depthBufferBits = EDepthBits.None;
            wrapMode = TextureWrapMode.Repeat;
        }

        public bool Equals(TextureDescriptor target)
        {
            return width == target.width
                && height == target.height
                && slices == target.slices
                && mipMapBias.Equals(target.mipMapBias)
                && depthBufferBits == target.depthBufferBits
                && colorFormat == target.colorFormat
                && filterMode == target.filterMode
                && wrapMode == target.wrapMode
                && dimension == target.dimension
                && anisoLevel == target.anisoLevel
                && enableRandomWrite == target.enableRandomWrite
                && useMipMap == target.useMipMap
                && autoGenerateMips == target.autoGenerateMips
                && isShadowMap == target.isShadowMap
                && bindTextureMS == target.bindTextureMS;
        }

        public override bool Equals(object target)
        {
            return target is TextureDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hashCode = 17;
            hashCode = hashCode * 23 + width;
            hashCode = hashCode * 23 + height;
            hashCode = hashCode * 23 + slices;
            hashCode = hashCode * 23 + mipMapBias.GetHashCode();
            hashCode = hashCode * 23 + (int)depthBufferBits;
            hashCode = hashCode * 23 + (int)colorFormat;
            hashCode = hashCode * 23 + (int)filterMode;
            hashCode = hashCode * 23 + (int)wrapMode;
            hashCode = hashCode * 23 + (int)dimension;
            hashCode = hashCode * 23 + anisoLevel;
            hashCode = hashCode * 23 + (enableRandomWrite ? 1 : 0);
            hashCode = hashCode * 23 + (useMipMap ? 1 : 0);
            hashCode = hashCode * 23 + (autoGenerateMips ? 1 : 0);
            hashCode = hashCode * 23 + (isShadowMap ? 1 : 0);
            hashCode = hashCode * 23 + (bindTextureMS ? 1 : 0);
            return hashCode;
        }

        public static implicit operator RenderTextureDescriptor(in TextureDescriptor descriptor)
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
        }
    }

    public struct FBufferRef
    {
        internal int handle;
        internal BufferDescriptor descriptor;
        public ComputeBuffer buffer;

        public FBufferRef(in int handle, in BufferDescriptor descriptor, ComputeBuffer buffer)
        {
            this.handle = handle;
            this.descriptor = descriptor;
            this.buffer = buffer;
        }

        public static implicit operator ComputeBuffer(in FBufferRef bufferRef) => bufferRef.buffer;
    }

    public struct FTextureRef
    {
        internal int handle;
        internal TextureDescriptor descriptor;
        public RTHandle texture;

        internal FTextureRef(in int handle, in TextureDescriptor descriptor, RTHandle texture)
        {
            this.handle = handle;
            this.descriptor = descriptor;
            this.texture = texture;
        }

        public static implicit operator RTHandle(in FTextureRef textureRef) => textureRef.texture;
    }

    public abstract class FGPUResourceCache<TResource, TDescriptor>
        where TResource : class
        where TDescriptor : struct, IEquatable<TDescriptor>
    {
        protected struct Entry
        {
            public TDescriptor descriptor;
            public TResource resource;
        }

        protected Dictionary<int, List<Entry>> m_ResourcePool = new Dictionary<int, List<Entry>>(64);

        abstract protected void ReleaseInternalResource(TResource res);
        abstract protected string GetResourceName(TResource res);
        abstract protected string GetResourceTypeName();

        public bool Pull(in int hashCode, in TDescriptor descriptor, out TResource resource)
        {
            if (m_ResourcePool.TryGetValue(hashCode, out var list) && list.Count > 0)
            {
                for (int i = list.Count - 1; i >= 0; --i)
                {
                    Entry entry = list[i];
                    if (!entry.descriptor.Equals(descriptor))
                    {
                        continue;
                    }

                    resource = entry.resource;
                    list.RemoveAt(i);
                    return true;
                }
            }

            resource = null;
            return false;
        }

        public void Push(in int hash, in TDescriptor descriptor, TResource resource)
        {
            if (!m_ResourcePool.TryGetValue(hash, out var list))
            {
                list = new List<Entry>();
                m_ResourcePool.Add(hash, list);
            }

            list.Add(new Entry { descriptor = descriptor, resource = resource });
        }

        public void Dispose()
        {
            foreach (var kvp in m_ResourcePool)
            {
                foreach (Entry entry in kvp.Value)
                {
                    ReleaseInternalResource(entry.resource);
                }
            }
        }
    }

    public class BufferCache : FGPUResourceCache<ComputeBuffer, BufferDescriptor>
    {
        protected override void ReleaseInternalResource(ComputeBuffer res)
        {
            res.Release();
        }

        protected override string GetResourceName(ComputeBuffer res)
        {
            return "BufferNameNotAvailable";
        }

        override protected string GetResourceTypeName()
        {
            return "Buffer";
        }
    }

    public class TextureCache : FGPUResourceCache<RTHandle, TextureDescriptor>
    {
        protected override void ReleaseInternalResource(RTHandle res)
        {
            res.Release();
        }

        protected override string GetResourceName(RTHandle res)
        {
            return res.name;
        }

        override protected string GetResourceTypeName()
        {
            return "Texture";
        }
    }
}
