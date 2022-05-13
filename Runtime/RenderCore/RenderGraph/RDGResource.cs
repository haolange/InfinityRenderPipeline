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

    internal struct RDGResourceHandle
    {
        bool m_IsValid;

        public int index { get; private set; }
        public ERDGResourceType type { get; private set; }
        public int iType { get { return (int)type; } }

        internal RDGResourceHandle(int value, ERDGResourceType type)
        {
            index = value;
            this.type = type;
            m_IsValid = true;
        }

        public static implicit operator int(RDGResourceHandle resourceRef) => resourceRef.index;
        public bool IsValid() => m_IsValid;
    }

    public struct RDGBufferRef
    {
        internal RDGResourceHandle handle;

        internal RDGBufferRef(int handle) 
        { 
            this.handle = new RDGResourceHandle(handle, ERDGResourceType.Buffer); 
        }

        public static implicit operator ComputeBuffer(RDGBufferRef bufferRef) => bufferRef.IsValid() ? RDGResourceFactory.current.GetBuffer(bufferRef) : null;
        public bool IsValid() => handle.IsValid();
    }

    public struct RDGTextureRef
    {
        private static RDGTextureRef s_NullHandle = new RDGTextureRef();

        public static RDGTextureRef nullHandle { get { return s_NullHandle; } }

        internal RDGResourceHandle handle;

        internal RDGTextureRef(int handle) 
        { 
            this.handle = new RDGResourceHandle(handle, ERDGResourceType.Texture); 
        }

        public static implicit operator RenderTexture(RDGTextureRef textureRef) => textureRef.IsValid() ? RDGResourceFactory.current.GetTexture(textureRef) : null;
        public static implicit operator RenderTargetIdentifier(RDGTextureRef textureRef) => textureRef.IsValid() ? RDGResourceFactory.current.GetTexture(textureRef) : null;
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

    internal class RDGResource<DescType, ResType> : IRDGResource where DescType : struct where ResType : class
    {
        public ResType resource;
        public DescType descriptor;

        protected RDGResource()
        {

        }

        public override void Reset()
        {
            base.Reset();
            resource = null;
        }
    }

    internal class RDGBuffer : RDGResource<BufferDescriptor, ComputeBuffer>
    {
        public override string GetName()
        {
            return descriptor.name;
        }
    }

    internal class RDGTexture : RDGResource<TextureDescriptor, RTHandle>
    {
        public override string GetName()
        {
            return descriptor.name;
        }
    }

    internal class RDGResourceFactory
    {
        static RDGResourceFactory m_CurrentRegistry;
        internal static RDGResourceFactory current
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

        BufferCache m_BufferPool = new BufferCache();
        TextureCache m_TexturePool = new TextureCache();
        DynamicArray<IRDGResource>[] m_Resources = new DynamicArray<IRDGResource>[2];

        public RDGResourceFactory()
        {
            for (int i = 0; i < 2; ++i)
            {
                m_Resources[i] = new DynamicArray<IRDGResource>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComputeBuffer GetBuffer(in RDGBufferRef bufferRef)
        {
            return GetBufferResource(bufferRef.handle).resource;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RenderTexture GetTexture(in RDGTextureRef textureRef)
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
        internal string GetResourceName(in RDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].GetName();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsResourceImported(in RDGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].imported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetResourceTemporalIndex(in RDGResourceHandle handle)
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
        internal RDGBufferRef ImportBuffer(ComputeBuffer computeBuffer)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out RDGBuffer rdgBuffer);
            rdgBuffer.resource = computeBuffer;
            rdgBuffer.imported = true;

            return new RDGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RDGBufferRef CreateBuffer(in BufferDescriptor descriptor, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Buffer], out RDGBuffer rdgBuffer);
            rdgBuffer.descriptor = descriptor;
            rdgBuffer.temporalPassIndex = temporalPassIndex;

            return new RDGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetBufferCount()
        {
            return m_Resources[(int)ERDGResourceType.Buffer].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RDGBuffer GetBufferResource(in RDGResourceHandle handle)
        {
            return m_Resources[(int)ERDGResourceType.Buffer][handle] as RDGBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal BufferDescriptor GetBufferDescriptor(in RDGResourceHandle handle)
        {
            return (m_Resources[(int)ERDGResourceType.Buffer][handle] as RDGBuffer).descriptor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RDGTextureRef ImportTexture(RTHandle rt, in int shaderProperty = 0)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out RDGTexture rdgTexture);
            rdgTexture.resource = rt;
            rdgTexture.imported = true;
            rdgTexture.shaderProperty = shaderProperty;

            return new RDGTextureRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RDGTextureRef CreateTexture(in TextureDescriptor descriptor, in int shaderProperty = 0, in int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERDGResourceType.Texture], out RDGTexture rdgTexture);
            rdgTexture.descriptor = descriptor;
            rdgTexture.shaderProperty = shaderProperty;
            rdgTexture.temporalPassIndex = temporalPassIndex;
            return new RDGTextureRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetTextureCount()
        {
            return m_Resources[(int)ERDGResourceType.Texture].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RDGTexture GetTextureResource(in RDGResourceHandle handle)
        {
            return m_Resources[(int)ERDGResourceType.Texture][handle] as RDGTexture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TextureDescriptor GetTextureDescriptor(in RDGResourceHandle handle)
        {
            return (m_Resources[(int)ERDGResourceType.Texture][handle] as RDGTexture).descriptor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateBufferResource(in int index)
        {
            RDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as RDGBuffer;

            if (!resource.imported)
            {
                var desc = resource.descriptor;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created Compute Buffer ({0}). Buffer was probably declared for writing more than once in the same pass.", resource.descriptor.name));

                resource.resource = null;
                if (!m_BufferPool.Pull(hashCode, out resource.resource))
                {
                    resource.resource = new ComputeBuffer(resource.descriptor.count, resource.descriptor.stride, resource.descriptor.type);
                }
                resource.cachedHash = hashCode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseBufferResource(int index)
        {
            RDGBuffer resource = m_Resources[(int)ERDGResourceType.Buffer][index] as RDGBuffer;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a compute buffer ({resource.descriptor.name}) that was never created. Check that there is at least one pass writing to it first.");

                m_BufferPool.Push(resource.cachedHash, resource.resource);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateTextureResource(ref RDGContext graphContext, int index)
        {
            RDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as RDGTexture;

            if (!resource.imported)
            {
                var desc = resource.descriptor;
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created texture ({0}). Texture was probably declared for writing more than once in the same pass.", resource.descriptor.name));

                resource.resource = null;
                if (!m_TexturePool.Pull(hashCode, out resource.resource))
                {
                    resource.resource = RTHandles.Alloc(desc.width, desc.height, desc.slices, (DepthBits)desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                    desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, (MSAASamples)desc.msaaSamples, desc.bindTextureMS, false, RenderTextureMemoryless.None, VRTextureUsage.None, desc.name);
                }
                resource.cachedHash = hashCode;

                if (resource.descriptor.clearBuffer)
                {
                    bool debugClear = !resource.descriptor.clearBuffer;
                    var clearFlag = resource.descriptor.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    var clearColor = debugClear ? Color.magenta : resource.descriptor.clearColor;
                    CoreUtils.SetRenderTarget(graphContext.cmdBuffer, resource.resource, clearFlag, clearColor);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseTextureResource(int index)
        {
            RDGTexture resource = m_Resources[(int)ERDGResourceType.Texture][index] as RDGTexture;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a texture ({resource.descriptor.name}) that was never created. Check that there is at least one pass writing to it first.");

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
        internal void SetGlobalTextures(ref RDGContext graphContext, List<RDGResourceHandle> textures)
        {
            foreach (var resource in textures)
            {
                RDGTexture Texture = GetTextureResource(resource);
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
