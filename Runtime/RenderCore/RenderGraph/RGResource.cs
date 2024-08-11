using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RenderGraph
{
    [Flags]
    public enum EDepthAccess : byte
    {
        ReadOnly = 0x01,
        Write = 0x02
    }

    [Flags]
    internal enum ERGResourceType : byte
    {
        Buffer = 0,
        Texture = 1,
        AccelerationStructure = 2,
        Max = 3
    }

    internal struct RGResourceHandle
    {
        bool m_IsValid;

        public int index { get; private set; }
        public ERGResourceType type { get; private set; }
        public int iType { get { return (int)type; } }

        internal RGResourceHandle(int value, ERGResourceType type)
        {
            index = value;
            this.type = type;
            m_IsValid = true;
        }

        public static implicit operator int(RGResourceHandle resourceRef) => resourceRef.index;
        public bool IsValid() => m_IsValid;
    }

    public struct RGBufferRef
    {
        internal RGResourceHandle handle;

        internal RGBufferRef(int handle) 
        { 
            this.handle = new RGResourceHandle(handle, ERGResourceType.Buffer); 
        }

        public static implicit operator ComputeBuffer(RGBufferRef bufferRef) => bufferRef.IsValid() ? RGResourceFactory.current.GetBuffer(bufferRef) : null;
        public bool IsValid() => handle.IsValid();
    }

    public struct RGTextureRef
    {
        private static RGTextureRef s_NullHandle = new RGTextureRef();

        public static RGTextureRef nullHandle { get { return s_NullHandle; } }

        internal RGResourceHandle handle;

        internal RGTextureRef(int handle) 
        { 
            this.handle = new RGResourceHandle(handle, ERGResourceType.Texture); 
        }

        public static implicit operator RenderTexture(RGTextureRef textureRef) => textureRef.IsValid() ? RGResourceFactory.current.GetTexture(textureRef) : null;
        public static implicit operator RenderTargetIdentifier(RGTextureRef textureRef) => textureRef.IsValid() ? RGResourceFactory.current.GetTexture(textureRef) : null;
        public bool IsValid() => handle.IsValid();
    }

    internal class IRGResource
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

    internal class RGResource<DescType, ResType> : IRGResource where DescType : struct where ResType : class
    {
        public ResType resource;
        public DescType descriptor;

        protected RGResource()
        {

        }

        public override void Reset()
        {
            base.Reset();
            resource = null;
        }
    }

    internal class RGBuffer : RGResource<BufferDescriptor, ComputeBuffer>
    {
        public override string GetName()
        {
            return descriptor.name;
        }
    }

    internal class RGTexture : RGResource<TextureDescriptor, RTHandle>
    {
        public override string GetName()
        {
            return descriptor.name;
        }
    }

    internal class RGResourceFactory
    {
        static RGResourceFactory m_CurrentRegistry;
        internal static RGResourceFactory current
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
        DynamicArray<IRGResource>[] m_Resources = new DynamicArray<IRGResource>[(int)ERGResourceType.Max];

        public RGResourceFactory()
        {
            for (int i = 0; i < 2; ++i)
            {
                m_Resources[i] = new DynamicArray<IRGResource>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComputeBuffer GetBuffer(in RGBufferRef bufferRef)
        {
            return GetBufferResource(bufferRef.handle).resource;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RTHandle GetTexture(in RGTextureRef textureRef)
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
        internal string GetResourceName(in RGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].GetName();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsResourceImported(in RGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].imported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetResourceTemporalIndex(in RGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].temporalPassIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int AddNewResource<ResType>(DynamicArray<IRGResource> resourceArray, out ResType outRes) where ResType : IRGResource, new()
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
        internal RGBufferRef ImportBuffer(ComputeBuffer computeBuffer)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERGResourceType.Buffer], out RGBuffer rdgBuffer);
            rdgBuffer.resource = computeBuffer;
            rdgBuffer.imported = true;

            return new RGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGBufferRef CreateBuffer(in BufferDescriptor descriptor, int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERGResourceType.Buffer], out RGBuffer rdgBuffer);
            rdgBuffer.descriptor = descriptor;
            rdgBuffer.temporalPassIndex = temporalPassIndex;

            return new RGBufferRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetBufferCount()
        {
            return m_Resources[(int)ERGResourceType.Buffer].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RGBuffer GetBufferResource(in RGResourceHandle handle)
        {
            return m_Resources[(int)ERGResourceType.Buffer][handle] as RGBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal BufferDescriptor GetBufferDescriptor(in RGResourceHandle handle)
        {
            return (m_Resources[(int)ERGResourceType.Buffer][handle] as RGBuffer).descriptor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGTextureRef ImportTexture(RTHandle rt, in int shaderProperty = 0)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERGResourceType.Texture], out RGTexture rdgTexture);
            rdgTexture.resource = rt;
            rdgTexture.imported = true;
            rdgTexture.shaderProperty = shaderProperty;

            return new RGTextureRef(newHandle);
        }

        /*internal RGTextureRef ImportBackbuffer(in RenderTargetIdentifier backBuffer, in int shaderProperty = 0)
        {
            if (m_Backbuffer != null)
            {
                m_Backbuffer.SetTexture(backBuffer);
            }
            else
            {
                m_Backbuffer = RTHandles.Alloc(backBuffer, "Backbuffer");
            }

            int newHandle = AddNewResource(m_Resources[(int)ERGResourceType.Texture], out RGTexture texResource);
            texResource.resource = m_Backbuffer;
            texResource.imported = true;
            texResource.shaderProperty = shaderProperty;
            return new RGTextureRef(newHandle);
        }*/

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGTextureRef CreateTexture(in TextureDescriptor descriptor, in int shaderProperty = 0, in int temporalPassIndex = -1)
        {
            int newHandle = AddNewResource(m_Resources[(int)ERGResourceType.Texture], out RGTexture rdgTexture);
            rdgTexture.descriptor = descriptor;
            rdgTexture.shaderProperty = shaderProperty;
            rdgTexture.temporalPassIndex = temporalPassIndex;
            return new RGTextureRef(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetTextureCount()
        {
            return m_Resources[(int)ERGResourceType.Texture].size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RGTexture GetTextureResource(in RGResourceHandle handle)
        {
            return m_Resources[(int)ERGResourceType.Texture][handle] as RGTexture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TextureDescriptor GetTextureDescriptor(in RGResourceHandle handle)
        {
            return (m_Resources[(int)ERGResourceType.Texture][handle] as RGTexture).descriptor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateBufferResource(in int index)
        {
            RGBuffer resource = m_Resources[(int)ERGResourceType.Buffer][index] as RGBuffer;

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
            RGBuffer resource = m_Resources[(int)ERGResourceType.Buffer][index] as RGBuffer;

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
        internal void CreateTextureResource(ref RGContext graphContext, int index)
        {
            RGTexture resource = m_Resources[(int)ERGResourceType.Texture][index] as RGTexture;

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
                    desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, (MSAASamples)desc.msaaSamples, desc.bindTextureMS, false, false, RenderTextureMemoryless.None, VRTextureUsage.None, desc.name);
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
            RGTexture resource = m_Resources[(int)ERGResourceType.Texture][index] as RGTexture;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a texture ({resource.descriptor.name}) that was never created. Check that there is at least one pass writing to it first.");

                /*using (new ProfilingScope(rgContext.CmdBuffer, ProfilingSampler.Get(ERGProfileId.RenderGraphClearDebug)))
                {
                    var clearFlag = resource.desc.depthBufferBits != EDepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                    CoreUtils.SetRenderTarget(rgContext.CmdBuffer, GetTexture(new RGTextureHandle(index)), clearFlag, Color.magenta);
                }*/

                m_TexturePool.Push(resource.cachedHash, resource.resource);
                resource.cachedHash = -1;
                resource.resource = null;
                resource.wasReleased = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetGlobalTextures(ref RGContext graphContext, List<RGResourceHandle> textures)
        {
            foreach (var resource in textures)
            {
                RGTexture Texture = GetTextureResource(resource);
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
