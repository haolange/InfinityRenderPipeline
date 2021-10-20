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

    internal struct FRDGResourceHandle
    {
        bool m_IsValid;

        public int index { get; private set; }
        public ERDGResourceType type { get; private set; }
        public int iType { get { return (int)type; } }

        internal FRDGResourceHandle(int value, ERDGResourceType type)
        {
            index = value;
            this.type = type;
            m_IsValid = true;
        }

        public static implicit operator int(FRDGResourceHandle resourceRef) => resourceRef.index;
        public bool IsValid() => m_IsValid;
    }

    public struct FRDGBufferRef
    {
        internal FRDGResourceHandle handle;

        internal FRDGBufferRef(int handle) 
        { 
            this.handle = new FRDGResourceHandle(handle, ERDGResourceType.Buffer); 
        }

        public static implicit operator ComputeBuffer(FRDGBufferRef bufferRef) => bufferRef.IsValid() ? FRDGResourceFactory.current.GetBuffer(bufferRef) : null;
        public bool IsValid() => handle.IsValid();
    }

    public struct FRDGTextureRef
    {
        private static FRDGTextureRef s_NullHandle = new FRDGTextureRef();

        public static FRDGTextureRef nullHandle { get { return s_NullHandle; } }

        internal FRDGResourceHandle handle;

        internal FRDGTextureRef(int handle) 
        { 
            this.handle = new FRDGResourceHandle(handle, ERDGResourceType.Texture); 
        }

        public static implicit operator RenderTexture(FRDGTextureRef textureRef) => textureRef.IsValid() ? FRDGResourceFactory.current.GetTexture(textureRef) : null;
        public static implicit operator RenderTargetIdentifier(FRDGTextureRef textureRef) => textureRef.IsValid() ? FRDGResourceFactory.current.GetTexture(textureRef) : null;
        public bool IsValid() => handle.IsValid();
    }

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

    internal class FRDGResource<DescType, ResType> : IRDGResource where DescType : struct where ResType : class
    {
        public ResType resource;
        public DescType description;

        protected FRDGResource()
        {

        }

        public override void Reset()
        {
            base.Reset();
            resource = null;
        }
    }

    internal class FRDGBuffer : FRDGResource<FBufferDescription, ComputeBuffer>
    {
        public override string GetName()
        {
            return description.name;
        }
    }

    internal class FRDGTexture : FRDGResource<FTextureDescription, RTHandle>
    {
        public override string GetName()
        {
            return description.name;
        }
    }
}
