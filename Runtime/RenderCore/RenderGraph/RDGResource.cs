using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

    internal class FRDGResourceFactory
    {
        static FRDGResourceFactory m_CurrentRegistry;
        internal static FRDGResourceFactory current
        {
            get
            {
                return m_CurrentRegistry;
            }
            set
            {
                m_CurrentRegistry = value;
            }
        }

        FBufferCache m_BufferPool = new FBufferCache();
        FTextureCache m_TexturePool = new FTextureCache();
        DynamicArray<IRDGResource>[] m_Resources = new DynamicArray<IRDGResource>[2];

        public FRDGResourceFactory()
        {
            for (int i = 0; i < 2; ++i)
            {
                m_Resources[i] = new DynamicArray<IRDGResource>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComputeBuffer GetBuffer(in FRDGBufferRef bufferRef)
        {
            return GetBufferResource(bufferRef.handle).resource;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RenderTexture GetTexture(in FRDGTextureRef textureRef)
        {
            return GetTextureResource(textureRef.handle).resource;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BeginRender()
        {
            current = this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EndRender()
        {
            current = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string GetResourceName(in FRDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].GetName();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsResourceImported(in FRDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].imported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetResourceTemporalIndex(in FRDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].temporalPassIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int AddNewResource<ResType>(DynamicArray<IRDGResource> resourceArray, out ResType outRes) where ResType : IRDGResource, new()
        {
            int result = resourceArray.size;
            resourceArray.Resize(resourceArray.size + 1, true);
            if (resourceArray[result] == null)
            {
                resourceArray[result] = new ResType();
            }

            outRes = resourceArray[result] as ResType;
            outRes.Reset();
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGBufferRef ImportBuffer(ComputeBuffer computeBuffer)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out FRDGBuffer rdgBuffer);
            rdgBuffer.resource = computeBuffer;
            rdgBuffer.imported = true;

            return new FRDGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGBufferRef CreateBuffer(in FBufferDescription description, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out FRDGBuffer rdgBuffer);
            rdgBuffer.description = description;
            rdgBuffer.temporalPassIndex = temporalPassIndex;

            return new FRDGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetBufferCount()
        {
            return m_Resources[(int)ERDGResourceType.Buffer].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        FRDGBuffer GetBufferResource(in FRDGResourceHandle handle)
        {
            return m_Resources[(int)ERDGResourceType.Buffer][handle] as FRDGBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FBufferDescription GetBufferDescription(in FRDGResourceHandle handle)
        {
            return (m_Resources[(int)ERDGResourceType.Buffer][handle] as FRDGBuffer).description;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGTextureRef ImportTexture(RTHandle rt, in int shaderProperty = 0)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out FRDGTexture rdgTexture);
            rdgTexture.resource = rt;
            rdgTexture.imported = true;
            rdgTexture.shaderProperty = shaderProperty;

            return new FRDGTextureRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FRDGTextureRef CreateTexture(in FTextureDescription description, in int shaderProperty = 0, in int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out FRDGTexture rdgTexture);
            rdgTexture.description = description;
            rdgTexture.shaderProperty = shaderProperty;
            rdgTexture.temporalPassIndex = temporalPassIndex;
            return new FRDGTextureRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetTextureCount()
        {
            return m_Resources[(int)ERDGResourceType.Texture].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        FRDGTexture GetTextureResource(in FRDGResourceHandle handle)
        {
            return m_Resources[(int)ERDGResourceType.Texture][handle] as FRDGTexture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FTextureDescription GetTextureDescription(in FRDGResourceHandle handle)
        {
            return (m_Resources[(int)ERDGResourceType.Texture][handle] as FRDGTexture).description;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateBufferResource(int index)
        {
            FRDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as FRDGBuffer;

            if (!resource.imported)
            {
                var desc = resource.description;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created Compute Buffer ({0}). Buffer was probably declared for writing more than once in the same pass.", resource.description.name));

                resource.resource = null;
                if (!m_BufferPool.Pull(hashCode, out resource.resource))
                {
                    resource.resource = new ComputeBuffer(resource.description.count, resource.description.stride, resource.description.type);
                }
                resource.cachedHash = hashCode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseBufferResource(int index)
        {
            FRDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as FRDGBuffer;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a compute buffer ({resource.description.name}) that was never created. Check that there is at least one pass writing to it first.");

                m_BufferPool.Push(resource.cachedHash, resource.resource);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateTextureResource(ref FRDGContext graphContext, int index)
        {
            FRDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as FRDGTexture;

            if (!resource.imported)
            {
                var desc = resource.description;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created texture ({0}). Texture was probably declared for writing more than once in the same pass.", resource.description.name));

                resource.resource = null;
                if (!m_TexturePool.Pull(hashCode, out resource.resource))
                {
                    resource.resource = RTHandles.Alloc(desc.width, desc.height, desc.slices, (DepthBits)desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                    desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, (MSAASamples)desc.msaaSamples, desc.bindTextureMS, false, RenderTextureMemoryless.None, desc.name);
                }
                resource.cachedHash = hashCode;

                if (resource.description.clearBuffer)
                {
                    bool debugClear = !resource.description.clearBuffer;
                    var clearFlag = resource.description.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    var clearColor = debugClear ? Color.magenta : resource.description.clearColor;
                    CoreUtils.SetRenderTarget(graphContext.cmdBuffer, resource.resource, clearFlag, clearColor);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseTextureResource(int index)
        {
            FRDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as FRDGTexture;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a texture ({resource.description.name}) that was never created. Check that there is at least one pass writing to it first.");

                /*using (new ProfilingScope(rgContext.CmdBuffer, ProfilingSampler.Get(ERGProfileId.RenderGraphClearDebug)))
                {
                    var clearFlag = resource.desc.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    CoreUtils.SetRenderTarget(rgContext.CmdBuffer, GetTexture(new RDGTextureHandle(index)), clearFlag, Color.magenta);
                }*/

                m_TexturePool.Push(resource.cachedHash, resource.resource);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetGlobalTextures(ref FRDGContext graphContext, List<FRDGResourceHandle> textures)
        {
            foreach (var resource in textures)
            {
                FRDGTexture Texture = GetTextureResource(resource);
                if (Texture.shaderProperty != 0)
                {
                    if (Texture.resource != null)
                    {
                        graphContext.cmdBuffer.SetGlobalTexture(Texture.shaderProperty, Texture.resource);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Clear()
        {
            for (int i = 0; i < 2; ++i)
            {
                m_Resources[i].Clear();
            }
        }

        internal void Dispose()
        {
            m_BufferPool.Dispose();
            m_TexturePool.Dispose();
        }
    }
}
