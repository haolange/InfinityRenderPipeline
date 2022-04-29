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
            return this.GetHashCode().Equals(target.GetHashCode());
        }

        public override bool Equals(object target)
        {
            return Equals((BufferDescriptor)target);
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

        public bool Equals(TextureDescriptor target)
        {
            return this.GetHashCode().Equals(target.GetHashCode());
        }

        public override bool Equals(object target)
        {
            return Equals((TextureDescriptor)target);
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
        public ComputeBuffer buffer;

        public FBufferRef(in int handle, ComputeBuffer buffer) 
        { 
            this.handle = handle;
            this.buffer = buffer; 
        }

        public static implicit operator ComputeBuffer(in FBufferRef bufferRef) => bufferRef.buffer;
    }

    public struct FTextureRef
    {
        internal int handle;
        public RTHandle texture;

        internal FTextureRef(in int handle, RTHandle texture) 
        {
            this.handle = handle;
            this.texture = texture; 
        }

        public static implicit operator RTHandle(in FTextureRef textureRef) => textureRef.texture;
    }

    public abstract class FGPUResourceCache<Type> where Type : class
    {
        protected Dictionary<int, List<Type>> m_ResourcePool = new Dictionary<int, List<Type>>(64);

        abstract protected void ReleaseInternalResource(Type res);
        abstract protected string GetResourceName(Type res);
        abstract protected string GetResourceTypeName();

        public bool Pull(in int hashCode, out Type resource)
        {
            if (m_ResourcePool.TryGetValue(hashCode, out var list) && list.Count > 0)
            {
                //resource = list[0];
                //list.RemoveAt(0);
                resource = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                return true;
            }

            resource = null;
            return false;
        }

        public void Push(in int hash, Type resource)
        {
            if (!m_ResourcePool.TryGetValue(hash, out var list))
            {
                list = new List<Type>();
                m_ResourcePool.Add(hash, list);
            }

            list.Add(resource);
        }

        public void Dispose()
        {
            foreach (var kvp in m_ResourcePool)
            {
                foreach (Type resource in kvp.Value)
                {
                    ReleaseInternalResource(resource);
                }
            }
        }
    }

    public class BufferCache : FGPUResourceCache<ComputeBuffer>
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

    public class TextureCache : FGPUResourceCache<RTHandle>
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
