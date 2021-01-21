using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.RDG
{
    [Flags]
    public enum EDepthAccess
    {
        Read = 1 << 0,
        Write = 1 << 1,
        ReadWrite = Read | Write,
    }

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

    internal enum RDGResourceType
    {
        Buffer,
        Texture
    }

    #region RDGResourceRef
    internal struct RDGResourceRef
    {
        bool m_IsValid;

        public int index { get; private set; }
        public RDGResourceType type { get; private set; }
        public int iType { get { return (int)type; } }

        internal RDGResourceRef(int value, RDGResourceType type)
        {
            index = value;
            this.type = type;
            m_IsValid = true;
        }

        public static implicit operator int(RDGResourceRef handle) => handle.index;
        public bool IsValid() => m_IsValid;
    }

    #region RDGBufferRef
    public struct RDGBufferDesc
    {
        public int count;
        public int stride;
        public ComputeBufferType type;
        public string name;

        public RDGBufferDesc(int count, int stride) : this()
        {
            this.count = count;
            this.stride = stride;
            type = ComputeBufferType.Default;
        }

        public RDGBufferDesc(int count, int stride, ComputeBufferType type) : this()
        {
            this.count = count;
            this.stride = stride;
            this.type = type;
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

    public struct RDGBufferRef
    {
        internal RDGResourceRef handle;

        internal RDGBufferRef(int handle) { this.handle = new RDGResourceRef(handle, RDGResourceType.Buffer); }
        public static implicit operator ComputeBuffer(RDGBufferRef bufferHandle) => bufferHandle.IsValid() ? RDGResourceFactory.current.GetBuffer(bufferHandle) : null;
        public bool IsValid() => handle.IsValid();
    }
    #endregion //RDGBufferRef

    #region RDGTextureRef
    public struct RDGTextureDesc
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

        public RDGTextureDesc(int InWidth, int InHeight) : this()
        {
            width = InWidth;
            height = InHeight;
            slices = 1;

            isShadowMap = false;
            enableRandomWrite = false;

            msaaSamples = EMSAASamples.None;
            depthBufferBits = EDepthBits.None;
            wrapMode = TextureWrapMode.Repeat;
        }

        public override int GetHashCode()
        {
            int hashCode = 17;

            unchecked
            {
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
            }

            return hashCode;
        }
    }

    public struct RDGTextureRef
    {
        private static RDGTextureRef s_NullHandle = new RDGTextureRef();

        public static RDGTextureRef nullHandle { get { return s_NullHandle; } }

        internal RDGResourceRef handle;

        internal RDGTextureRef(int handle) { this.handle = new RDGResourceRef(handle, RDGResourceType.Texture); }

        public static implicit operator RenderTexture(RDGTextureRef texture) => texture.IsValid() ? RDGResourceFactory.current.GetTexture(texture) : null;
        public static implicit operator RenderTargetIdentifier(RDGTextureRef texture) => texture.IsValid() ? RDGResourceFactory.current.GetTexture(texture) : null;
        public bool IsValid() => handle.IsValid();
    }
    #endregion //RDGTextureRef

    #endregion //RDGResourceRef


    #region RDGResource
    internal class IRDGResource
    {
        public bool imported;
        public int cachedHash;
        public int shaderProperty;
        public int temporalPassIndex;
        public bool wasReleased;

        public virtual void Reset()
        {
            imported = false;
            cachedHash = -1;
            shaderProperty = 0;
            temporalPassIndex = -1;
            wasReleased = false;
        }

        public virtual string GetName()
        {
            return "";
        }
    }

    internal class RDGResource<DescType, ResType> : IRDGResource where DescType : struct where ResType : class
    {
        public DescType desc;
        public ResType resource;

        protected RDGResource()
        {

        }

        public override void Reset()
        {
            base.Reset();
            resource = null;
        }
    }

    internal class RDGBuffer : RDGResource<RDGBufferDesc, ComputeBuffer>
    {
        public override string GetName()
        {
            return desc.name;
        }
    }

    internal class RDGTexture : RDGResource<RDGTextureDesc, RenderTexture>
    {
        public override string GetName()
        {
            return desc.name;
        }
    }

    #endregion //RDGResource
}
