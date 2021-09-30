using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RDG
{
    [Flags]
    public enum EDepthAccess
    {
        Read = 1 << 0,
        Write = 1 << 1,
        ReadWrite = Read | Write,
    }

    internal enum ERDGResourceType
    {
        Buffer,
        Texture
    }

    #region RDGResourceRef
    internal struct RDGResourceRef
    {
        bool m_IsValid;

        public int index { get; private set; }
        public ERDGResourceType type { get; private set; }
        public int iType { get { return (int)type; } }

        internal RDGResourceRef(int value, ERDGResourceType type)
        {
            index = value;
            this.type = type;
            m_IsValid = true;
        }

        public static implicit operator int(RDGResourceRef handle) => handle.index;
        public bool IsValid() => m_IsValid;
    }

    #region RDGBufferRef
    /*public struct RDGBufferDesc
    {
        public string name;

        public int count;
        public int stride;
        public ComputeBufferType type;

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
            int hashCode = 0;
            hashCode += count;
            hashCode += stride;
            hashCode += (int)type;

            return hashCode;
        }
    }*/

    public struct RDGBufferRef
    {
        internal RDGResourceRef handle;

        internal RDGBufferRef(int handle) { this.handle = new RDGResourceRef(handle, ERDGResourceType.Buffer); }
        public static implicit operator ComputeBuffer(RDGBufferRef bufferHandle) => bufferHandle.IsValid() ? RDGResourceFactory.current.GetBuffer(bufferHandle) : null;
        public bool IsValid() => handle.IsValid();
    }
    #endregion //RDGBufferRef

    #region RDGTextureRef
    /*public struct RDGTextureDesc
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
            int hashCode = 0;
            hashCode += width;
            hashCode += height;
            hashCode += slices;
            hashCode += mipMapBias.GetHashCode();
            hashCode += (int)depthBufferBits;
            hashCode += (int)colorFormat;
            hashCode += (int)filterMode;
            hashCode += (int)wrapMode;
            hashCode += (int)dimension;
            hashCode += anisoLevel;
            hashCode += (enableRandomWrite ? 1 : 0);
            hashCode += (useMipMap ? 1 : 0);
            hashCode += (autoGenerateMips ? 1 : 0);
            hashCode += (isShadowMap ? 1 : 0);
            hashCode += (bindTextureMS ? 1 : 0);

            return hashCode;
        }
    }*/

    public struct RDGTextureRef
    {
        private static RDGTextureRef s_NullHandle = new RDGTextureRef();

        public static RDGTextureRef nullHandle { get { return s_NullHandle; } }

        internal RDGResourceRef handle;

        internal RDGTextureRef(int handle) { this.handle = new RDGResourceRef(handle, ERDGResourceType.Texture); }

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

    internal class RDGBuffer : RDGResource<BufferDescription, ComputeBuffer>
    {
        public override string GetName()
        {
            return desc.name;
        }
    }

    internal class RDGTexture : RDGResource<TextureDescription, RTHandle>
    {
        public override string GetName()
        {
            return desc.name;
        }
    }

    #endregion //RDGResource
}
