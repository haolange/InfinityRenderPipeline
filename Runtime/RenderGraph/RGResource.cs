using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
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
        Max = 2
    }

    // A graph resource reference. It is valid exactly when its generation is nonzero; the generation must match the live
    // slot, so a handle from an earlier graph is rejected. Two handles name the same resource when all three fields match.
    internal struct RGResourceHandle : IEquatable<RGResourceHandle>
    {
        public int index
        {
            get;
            private set;
        }
        public ERGResourceType type
        {
            get;
            private set;
        }
        public ushort generation
        {
            get;
            private set;
        }
        public int iType { get { return (int)type; } }

        internal RGResourceHandle(int value, ERGResourceType type, ushort generation)
        {
            index = value;
            this.type = type;
            this.generation = generation;
        }

        public bool IsValid() => generation != 0;
        public bool Equals(RGResourceHandle other) => index == other.index && type == other.type && generation == other.generation;
        public override bool Equals(object obj) => obj is RGResourceHandle other && Equals(other);
        public override int GetHashCode() => ((int)type << 24) ^ (index << 8) ^ generation;
    }

    public struct RGBufferHandle
    {
        internal RGResourceHandle handle;

        internal RGBufferHandle(int handle, ushort generation)
        {
            this.handle = new RGResourceHandle(handle, ERGResourceType.Buffer, generation);
        }

        public bool IsValid() => handle.IsValid();
    }

    public struct RGTextureHandle
    {
        internal RGResourceHandle handle;

        internal RGTextureHandle(int handle, ushort generation)
        {
            this.handle = new RGResourceHandle(handle, ERGResourceType.Texture, generation);
        }

        public bool IsValid() => handle.IsValid();
    }

    internal class IRGResource
    {
        public bool imported;
        internal ResourceUsage usage;
        internal bool recordingLease;
        // Acquisition generation of the pooled resource; returned with it so the pool rejects a stale return.
        internal int poolLease;
        internal ushort generation;

        public virtual void Reset()
        {
            imported = false;
            usage = null;
            recordingLease = false;
            poolLease = 0;
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
            descriptor = default;
        }
    }

    internal class RGBuffer : RGResource<BufferDescriptor, ComputeBuffer>
    {
        public GraphicsBuffer graphicsResource;
        public GraphicsBuffer.Target graphicsTarget;

        public override void Reset()
        {
            base.Reset();
            graphicsResource = null;
            graphicsTarget = default;
        }
        public override string GetName()
        {
            return descriptor.name;
        }
    }

    internal class RGTexture : RGResource<TextureDescriptor, RTHandle>
    {
        internal bool hasExplicitDescriptor;

        public override void Reset()
        {
            base.Reset();
            hasExplicitDescriptor = false;
        }

        public override string GetName()
        {
            return descriptor.name;
        }
    }

    internal class RGResourceFactory
    {
        readonly ResourcePool m_Pool;
        readonly bool m_OwnsPool;
        internal TextureCapacityContext textureCapacity;
        readonly List<TextureHandle> m_RetiredTextures = new List<TextureHandle>();
        readonly List<BufferHandle> m_RetiredBuffers = new List<BufferHandle>();
        internal int RetiredResourceCount => m_RetiredTextures.Count + m_RetiredBuffers.Count;
        readonly Dictionary<ComputeBuffer, int> m_ImportedBuffers = new Dictionary<ComputeBuffer, int>();
        readonly Dictionary<GraphicsBuffer, int> m_ImportedGraphicsBuffers = new Dictionary<GraphicsBuffer, int>();
        readonly Dictionary<RenderTargetIdentifier, int> m_ImportedTextures = new Dictionary<RenderTargetIdentifier, int>();
        RTHandle m_Backbuffer;
        RenderTargetIdentifier m_BackbufferIdentifier;
        // Slot objects survive Clear with their generation, so a slot reused next frame cannot match a handle from the previous graph.
        DynamicArray<IRGResource>[] m_Resources = new DynamicArray<IRGResource>[(int)ERGResourceType.Max];

        public RGResourceFactory(ResourcePool pool = null)
        {
            m_OwnsPool = pool == null;
            m_Pool = pool ?? new ResourcePool();
            for (int i = 0; i < (int)ERGResourceType.Max; ++i)
            {
                m_Resources[i] = new DynamicArray<IRGResource>();
            }
        }

        // The one checked lookup: every handle-taking accessor resolves through it.
        IRGResource Resolve(in RGResourceHandle handle, string api)
        {
            RequireLive(handle, api);
            return m_Resources[handle.iType][handle.index];
        }

        // A handle to the live slot, for compile code that walks resources by index.
        internal RGResourceHandle LiveHandle(ERGResourceType type, int index)
            => new RGResourceHandle(index, type, m_Resources[(int)type][index].generation);

        internal void RequireLive(in RGResourceHandle handle, string api)
        {
            if (!handle.IsValid()
                || (uint)handle.iType >= (uint)m_Resources.Length
                || handle.index < 0
                || handle.index >= m_Resources[handle.iType].size
                || m_Resources[handle.iType][handle.index].generation != handle.generation)
            {
                throw new InvalidOperationException($"{api} received a resource from a previous graph.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RTHandle GetTexture(in RGTextureHandle textureHandle)
        {
            return GetTextureResource(textureHandle.handle).resource;
        }

        static void RetainRecording(IRGResource resource, ResourceUsage usage)
        {
            resource.usage = usage;
            if (usage == null)
            {
                return;
            }
            usage.RetainRecording();
            resource.recordingLease = true;
        }
        static void ReleaseRecording(IRGResource resource)
        {
            if (!resource.recordingLease)
            {
                return;
            }
            resource.usage.ReleaseRecording();
            resource.recordingLease = false;
        }
        internal bool IsPoolTracked(RGResourceHandle handle) => m_Resources[handle.iType][handle.index].usage != null;
        internal void QueueAttempt(IRGPass pass, bool asyncCompute, ResourceReuseFence fence)
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (var handle in pass.resourceReadLists[type])
                {
                    m_Pool.QueueAttempt(m_Resources[type][handle.index].usage, asyncCompute, fence);
                }
                foreach (var handle in pass.resourceWriteLists[type])
                {
                    m_Pool.QueueAttempt(m_Resources[type][handle.index].usage, asyncCompute, fence);
                }
            }
        }
        internal void SubmissionUncertain() => m_Pool.SubmissionUncertain();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BeginRender()
        {
            m_Pool.EnsureRecording();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string GetResourceName(in RGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].GetName();
        }

        internal int GetResourceCount(int type) => m_Resources[type].size;

        // RenderGraph Viewer: name, import state and a one-line size / format of a resource.
        internal void DescribeResource(int type, int index, out string name, out bool imported, out string description)
        {
            IRGResource resource = m_Resources[type][index];
            name = resource.GetName();
            imported = resource.imported;
            description = string.Empty;
            if (resource is RGTexture texture)
            {
                TextureDescriptor descriptor = texture.descriptor;
                RenderTexture importedTexture = texture.resource != null ? texture.resource.rt : null;
                if (string.IsNullOrEmpty(name) && texture.resource != null)
                {
                    name = texture.resource.name;
                }
                if (descriptor.width > 0)
                {
                    string format = descriptor.colorFormat != GraphicsFormat.None ? descriptor.colorFormat.ToString() : "Depth" + (int)descriptor.depthBufferBits;
                    description = descriptor.width + "x" + descriptor.height + " capacity " + descriptor.physicalWidth + "x" + descriptor.physicalHeight + (descriptor.slices > 1 ? "x" + descriptor.slices : "") + " " + format;
                }
                else if (importedTexture != null)
                {
                    description = importedTexture.width + "x" + importedTexture.height + (importedTexture.volumeDepth > 1 ? "x" + importedTexture.volumeDepth : "") + " " + importedTexture.graphicsFormat;
                }
            }
            else if (resource is RGBuffer buffer)
            {
                description = buffer.descriptor.count + " x " + buffer.descriptor.stride + " B";
            }
            if (string.IsNullOrEmpty(name))
            {
                name = ((ERGResourceType)type) + " " + index;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsResourceImported(in RGResourceHandle handle)
        {
            return m_Resources[handle.iType][handle.index].imported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int AddNewResource<ResType>(int type, DynamicArray<IRGResource> resourceArray, out ResType outRes) where ResType : IRGResource, new()
        {
            int result = resourceArray.size;
            resourceArray.Resize(resourceArray.size + 1, true);
            if (resourceArray[result] == null)
            {
                resourceArray[result] = new ResType();
            }

            outRes = resourceArray[result] as ResType;
            outRes.Reset();
            // Zero is the invalid generation; wrapping skips it.
            outRes.generation = outRes.generation == ushort.MaxValue ? (ushort)1 : (ushort)(outRes.generation + 1);
            return result;
        }

        // The checked texture lookup for encoders: rejects a handle from another graph and a texture with no live resource.
        internal RGTexture ResolveTexture(in RGTextureHandle textureHandle)
        {
            RGTexture texture = GetTextureResource(textureHandle.handle);
            if (texture.resource == null)
                throw new InvalidOperationException($"Graph texture '{texture.GetName()}' has no live resource.");
            return texture;
        }

        internal RGBuffer ResolveBuffer(in RGBufferHandle bufferHandle)
        {
            RGBuffer buffer = GetBufferResource(bufferHandle.handle);
            if (buffer.resource == null && buffer.graphicsResource == null)
                throw new InvalidOperationException($"Graph buffer '{buffer.GetName()}' has no live resource.");
            return buffer;
        }

        RGBufferHandle BufferHandle(int index)
        {
            return new RGBufferHandle(index, m_Resources[(int)ERGResourceType.Buffer][index].generation);
        }

        internal RGTextureHandle TextureHandle(int index)
        {
            return new RGTextureHandle(index, m_Resources[(int)ERGResourceType.Texture][index].generation);
        }

        internal RGBufferHandle ImportBuffer(in BufferHandle buffer)
        {
            if (buffer.buffer == null || !buffer.buffer.IsValid())
                throw new ArgumentException("Cannot import an invalid buffer.", nameof(buffer));
            if (m_ImportedBuffers.TryGetValue(buffer.buffer, out int existing))
            {
                return BufferHandle(existing);
            }
            int index = AddNewResource((int)ERGResourceType.Buffer, m_Resources[(int)ERGResourceType.Buffer], out RGBuffer resource);
            m_ImportedBuffers.Add(buffer.buffer, index);
            resource.resource = buffer.buffer;
            RetainRecording(resource, m_Pool.BufferUsage(buffer.buffer));
            resource.descriptor = buffer.descriptor;
            resource.imported = true;
            return BufferHandle(index);
        }

        internal RGBufferHandle ImportBuffer(GraphicsBuffer buffer, string name)
        {
            if (buffer == null || !buffer.IsValid())
                throw new ArgumentException("Cannot import an invalid GraphicsBuffer.", nameof(buffer));
            if (m_ImportedGraphicsBuffers.TryGetValue(buffer, out int existing))
            {
                return BufferHandle(existing);
            }
            int index = AddNewResource((int)ERGResourceType.Buffer, m_Resources[(int)ERGResourceType.Buffer], out RGBuffer resource);
            m_ImportedGraphicsBuffers.Add(buffer, index);
            resource.graphicsResource = buffer;
            resource.graphicsTarget = buffer.target;
            resource.descriptor = new BufferDescriptor { name = name, count = buffer.count, stride = buffer.stride };
            resource.imported = true;
            return BufferHandle(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGBufferHandle CreateBuffer(in BufferDescriptor descriptor)
        {
            int newHandle = AddNewResource((int)ERGResourceType.Buffer, m_Resources[(int)ERGResourceType.Buffer], out RGBuffer rdgBuffer);
            rdgBuffer.descriptor = descriptor;
            return BufferHandle(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RGBuffer GetBufferResource(in RGResourceHandle handle)
        {
            return (RGBuffer)Resolve(handle, "RGBufferHandle");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal BufferDescriptor GetBufferDescriptor(in RGResourceHandle handle)
        {
            RGBuffer buffer = GetBufferResource(handle);
            if (buffer.graphicsResource != null)
                throw new InvalidOperationException("Imported GraphicsBuffer targets cannot be cloned as ComputeBuffer descriptors.");
            return buffer.descriptor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGTextureHandle ImportTexture(RTHandle rt)
        {
            if (rt == null)
            {
                throw new ArgumentNullException(nameof(rt));
            }
            var identity = rt.rt != null ? new RenderTargetIdentifier(rt.rt) : rt.nameID;
            if (m_ImportedTextures.TryGetValue(identity, out int existing))
            {
                return TextureHandle(existing);
            }
            int newHandle = AddNewResource((int)ERGResourceType.Texture, m_Resources[(int)ERGResourceType.Texture], out RGTexture rdgTexture);
            m_ImportedTextures.Add(identity, newHandle);
            rdgTexture.resource = rt;
            RetainRecording(rdgTexture, m_Pool.TextureUsage(rt));
            if (rt.rt != null)
            {
                var texture = rt.rt;
                rdgTexture.descriptor = new TextureDescriptor(texture.width, texture.height, texture.volumeDepth)
                {
                    name = rt.name, colorFormat = texture.graphicsFormat, depthBufferBits = (EDepthBits)texture.depth,
                    dimension = texture.dimension, filterMode = texture.filterMode, wrapMode = texture.wrapMode,
                    useMipMap = texture.useMipMap, autoGenerateMips = texture.autoGenerateMips,
                    enableRandomWrite = texture.enableRandomWrite, msaaSamples = (EMSAASamples)texture.antiAliasing
                };
            }
            rdgTexture.imported = true;
            return TextureHandle(newHandle);
        }

        internal RGTextureHandle ImportTexture(RTHandle rt, in TextureDescriptor descriptor)
        {
            RGTextureHandle reference = ImportTexture(rt);
            RGTexture resource = GetTextureResource(reference.handle);
            SetImportedTextureDescriptor(resource, descriptor);
            return reference;
        }

        static void SetImportedTextureDescriptor(RGTexture resource, in TextureDescriptor descriptor)
        {
            if (resource.hasExplicitDescriptor)
            {
                var current = resource.descriptor;
                var requested = descriptor;
                current.name = requested.name;
                if (!current.Equals(requested))
                    throw new InvalidOperationException("The same imported texture cannot have conflicting logical views in one graph.");
            }
            resource.descriptor = descriptor;
            resource.hasExplicitDescriptor = true;
        }

        // The backbuffer has no owning RenderTexture, so it can only be bound as a RenderTargetIdentifier.
        // Imported resources skip Create/Release, which keeps the single RTHandle wrapper valid across frames.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGTextureHandle ImportBackbuffer(in RenderTargetIdentifier backBuffer, in TextureDescriptor descriptor)
        {
            if (m_ImportedTextures.TryGetValue(backBuffer, out int existing))
            {
                RGTexture resource = GetTextureResource(TextureHandle(existing).handle);
                SetImportedTextureDescriptor(resource, descriptor);
                return TextureHandle(existing);
            }
            // RTHandle.SetTexture is internal to CoreRP, so rebinding is done by reallocating on identifier change.
            if (m_Backbuffer == null || !m_BackbufferIdentifier.Equals(backBuffer))
            {
                if (m_Backbuffer != null)
                {
                    RTHandles.Release(m_Backbuffer);
                }

                m_Backbuffer = RTHandles.Alloc(backBuffer, "Backbuffer");
                m_BackbufferIdentifier = backBuffer;
            }

            int newHandle = AddNewResource((int)ERGResourceType.Texture, m_Resources[(int)ERGResourceType.Texture], out RGTexture texResource);
            m_ImportedTextures.Add(backBuffer, newHandle);
            texResource.resource = m_Backbuffer;
            texResource.descriptor = descriptor;
            texResource.hasExplicitDescriptor = true;
            texResource.imported = true;
            return TextureHandle(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGTextureHandle CreateTexture(in TextureDescriptor descriptor)
        {
            int newHandle = AddNewResource((int)ERGResourceType.Texture, m_Resources[(int)ERGResourceType.Texture], out RGTexture rdgTexture);
            rdgTexture.descriptor = textureCapacity.Resolve(descriptor);
            return TextureHandle(newHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RGTexture GetTextureResource(in RGResourceHandle handle)
        {
            return (RGTexture)Resolve(handle, "RGTextureHandle");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TextureDescriptor GetTextureDescriptor(in RGResourceHandle handle)
        {
            return GetTextureResource(handle).descriptor;
        }

        internal void SetTextureDescriptor(int index, in TextureDescriptor descriptor)
        {
            ((RGTexture)m_Resources[(int)ERGResourceType.Texture][index]).descriptor = descriptor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateBufferResource(ref RGContext graphContext, in int index, bool asyncCompute)
        {
            RGBuffer resource = m_Resources[(int)ERGResourceType.Buffer][index] as RGBuffer;

            if (!resource.imported)
            {
                var desc = resource.descriptor;

                BufferHandle pooled = m_Pool.GetBuffer(desc, true, asyncCompute, out ResourceReuseFence reuseFence);
                resource.resource = pooled.buffer;
                resource.poolLease = pooled.lease;
                reuseFence.Wait(graphContext.cmdBuffer, asyncCompute);
                RetainRecording(resource, m_Pool.BufferUsage(resource.resource));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseBufferResource(int index, ResourceReuseFence reuseFence = default)
        {
            RGBuffer resource = m_Resources[(int)ERGResourceType.Buffer][index] as RGBuffer;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a compute buffer ({resource.descriptor.name}) that was never created. Check that there is at least one pass writing to it first.");

                ReleaseRecording(resource);
                m_Pool.ReleaseBuffer(new BufferHandle(resource.poolLease, resource.descriptor, resource.resource), reuseFence);
                resource.poolLease = 0;
                resource.resource = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CreateTextureResource(ref RGContext graphContext, int index, bool asyncCompute)
        {
            RGTexture resource = m_Resources[(int)ERGResourceType.Texture][index] as RGTexture;

            if (!resource.imported)
            {
                var desc = resource.descriptor;

                TextureHandle pooled = m_Pool.GetTexture(desc, true, asyncCompute, out ResourceReuseFence reuseFence);
                resource.resource = pooled.texture;
                resource.poolLease = pooled.lease;
                reuseFence.Wait(graphContext.cmdBuffer, asyncCompute);
                RetainRecording(resource, m_Pool.TextureUsage(resource.resource));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseTextureResource(int index, ResourceReuseFence reuseFence = default)
        {
            RGTexture resource = m_Resources[(int)ERGResourceType.Texture][index] as RGTexture;

            if (!resource.imported)
            {
                if (resource.resource == null)
                    throw new InvalidOperationException($"Tried to release a texture ({resource.descriptor.name}) that was never created. Check that there is at least one pass writing to it first.");

                ReleaseRecording(resource);
                m_Pool.ReleaseTexture(new TextureHandle(resource.poolLease, resource.descriptor, resource.resource), reuseFence);
                resource.poolLease = 0;
                resource.resource = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Clear()
        {
            m_ImportedBuffers.Clear();
            m_ImportedGraphicsBuffers.Clear();
            m_ImportedTextures.Clear();
            // A failed graph may never reach compiled last-use releases. Preserve those allocations
            // outside the reusable pool until the enclosing frame has been submitted.
            for (int i = 0; i < m_Resources[(int)ERGResourceType.Texture].size; i++)
            {
                RGTexture texture = (RGTexture)m_Resources[(int)ERGResourceType.Texture][i];
                if (!texture.imported && texture.resource != null)
                {
                    m_RetiredTextures.Add(new TextureHandle(texture.poolLease, texture.descriptor, texture.resource));
                    texture.resource = null;
                }
            }
            for (int i = 0; i < m_Resources[(int)ERGResourceType.Buffer].size; i++)
            {
                RGBuffer buffer = (RGBuffer)m_Resources[(int)ERGResourceType.Buffer][i];
                if (!buffer.imported && buffer.resource != null)
                {
                    m_RetiredBuffers.Add(new BufferHandle(buffer.poolLease, buffer.descriptor, buffer.resource));
                    buffer.resource = null;
                }
            }
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                for (int i = 0; i < m_Resources[type].size; ++i)
                {
                    ReleaseRecording(m_Resources[type][i]);
                }
                m_Resources[type].Clear();
            }
        }

        internal void FlushRetired(ResourceReuseFence reuseFence = default)
        {
            foreach (TextureHandle texture in m_RetiredTextures)
                m_Pool.ReleaseTexture(texture, reuseFence);
            foreach (BufferHandle buffer in m_RetiredBuffers)
                m_Pool.ReleaseBuffer(buffer, reuseFence);
            m_RetiredTextures.Clear();
            m_RetiredBuffers.Clear();
        }

        internal void Dispose()
        {
            if (m_Backbuffer != null)
            {
                RTHandles.Release(m_Backbuffer);
                m_Backbuffer = null;
            }

            FlushRetired();
            if (m_OwnsPool)
            {
                m_Pool.Dispose();
            }
        }
    }
}
