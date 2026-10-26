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

    internal struct RGResourceHandle
    {
        readonly bool m_IsValid;

        public int index { get; private set; }
        public ERGResourceType type { get; private set; }
        public ushort generation { get; private set; }
        public int iType { get { return (int)type; } }

        internal RGResourceHandle(int value, ERGResourceType type, ushort generation = 0)
        {
            index = value;
            this.type = type;
            this.generation = generation;
            m_IsValid = true;
        }

        public static implicit operator int(RGResourceHandle resourceRef) => resourceRef.index;
        public bool IsValid() => m_IsValid;
    }

    public struct RGBufferRef
    {
        internal RGResourceHandle handle;

        internal RGBufferRef(int handle, ushort generation = 0)
        {
            this.handle = new RGResourceHandle(handle, ERGResourceType.Buffer, generation);
        }

        public static implicit operator ComputeBuffer(RGBufferRef bufferRef) => bufferRef.Resolve().resource
            ?? throw new InvalidOperationException("The graph buffer is a GraphicsBuffer, not a ComputeBuffer.");

        internal RGBuffer Resolve()
        {
            if (!IsValid() || RGResourceFactory.current == null)
                throw new InvalidOperationException("RGBufferRef is invalid or the resource factory is not active.");
            return RGResourceFactory.current.ResolveBuffer(this);
        }

        internal void BindGlobal(CommandBuffer commands, int nameID)
        {
            RGBuffer buffer = Resolve();
            if (buffer.graphicsResource != null) commands.SetGlobalBuffer(nameID, buffer.graphicsResource);
            else commands.SetGlobalBuffer(nameID, buffer.resource);
        }

        internal void DispatchIndirect(CommandBuffer commands, ComputeShader shader, int kernel, uint argsOffset)
        {
            RGBuffer buffer = Resolve();
            if (buffer.graphicsResource != null)
            {
                if ((buffer.graphicsTarget & GraphicsBuffer.Target.IndirectArguments) == 0)
                    throw new InvalidOperationException($"RG buffer {buffer.GetName()} is not an IndirectArguments buffer.");
                commands.DispatchCompute(shader, kernel, buffer.graphicsResource, argsOffset);
            }
            else
            {
                if ((buffer.descriptor.type & ComputeBufferType.IndirectArguments) == 0)
                    throw new InvalidOperationException($"RG buffer {buffer.GetName()} is not an IndirectArguments buffer.");
                commands.DispatchCompute(shader, kernel, buffer.resource, argsOffset);
            }
        }

        internal void BindCompute(CommandBuffer commands, ComputeShader shader, int kernel, int nameID)
        {
            RGBuffer buffer = Resolve();
            if (buffer.graphicsResource != null) commands.SetComputeBufferParam(shader, kernel, nameID, buffer.graphicsResource);
            else commands.SetComputeBufferParam(shader, kernel, nameID, buffer.resource);
        }
        public bool IsValid() => handle.IsValid();
    }

    public struct RGTextureRef
    {
        private static RGTextureRef s_NullHandle = new RGTextureRef();

        public static RGTextureRef nullHandle { get { return s_NullHandle; } }

        internal RGResourceHandle handle;

        internal RGTextureRef(int handle, ushort generation = 0)
        {
            this.handle = new RGResourceHandle(handle, ERGResourceType.Texture, generation);
        }

        public static implicit operator RenderTexture(RGTextureRef textureRef) => ResolveRenderTexture(textureRef);
        public static implicit operator RenderTargetIdentifier(RGTextureRef textureRef) => ResolveRenderTarget(textureRef);
        public bool IsValid() => handle.IsValid();

        internal static RenderTexture ResolveRenderTexture(in RGTextureRef textureRef)
        {
            if (!textureRef.IsValid() || RGResourceFactory.current == null)
            {
                throw new InvalidOperationException("RGTextureRef is invalid or RGResourceFactory is not active.");
            }

            RTHandle handle = RGResourceFactory.current.GetTexture(textureRef);
            if (handle == null || handle.rt == null)
            {
                throw new InvalidOperationException("RGTextureRef resolved to a null texture.");
            }

            return handle.rt;
        }

        internal static RenderTargetIdentifier ResolveRenderTarget(in RGTextureRef textureRef)
        {
            return ResolveRenderTexture(textureRef);
        }
    }

    internal class IRGResource
    {
        public bool imported;
        internal ResourceUsage usage;
        internal bool recordingLease;
        public int cachedHash;
        internal ushort generation;

        public virtual void Reset()
        {
            imported = false;
            usage = null; recordingLease = false;
            cachedHash = -1;
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

        readonly ResourcePool m_Pool;
        readonly bool m_OwnsPool;
        internal FTextureCapacityContext textureCapacity;
        readonly List<FTextureRef> m_RetiredTextures = new List<FTextureRef>();
        readonly List<FBufferRef> m_RetiredBuffers = new List<FBufferRef>();
        internal int RetiredResourceCount => m_RetiredTextures.Count + m_RetiredBuffers.Count;
        readonly Dictionary<ComputeBuffer, int> m_ImportedBuffers = new Dictionary<ComputeBuffer, int>();
        readonly Dictionary<GraphicsBuffer, int> m_ImportedGraphicsBuffers = new Dictionary<GraphicsBuffer, int>();
        readonly Dictionary<RenderTargetIdentifier, int> m_ImportedTextures = new Dictionary<RenderTargetIdentifier, int>();
        RTHandle m_Backbuffer;
        RenderTargetIdentifier m_BackbufferIdentifier;
        DynamicArray<IRGResource>[] m_Resources = new DynamicArray<IRGResource>[(int)ERGResourceType.Max];
        // Survives Clear so a slot reused next frame cannot match a handle from the previous graph.
        readonly List<ushort>[] m_Generations = new List<ushort>[(int)ERGResourceType.Max];

        public RGResourceFactory(ResourcePool pool = null)
        {
            m_OwnsPool = pool == null;
            m_Pool = pool ?? new ResourcePool();
            for (int i = 0; i < (int)ERGResourceType.Max; ++i)
            {
                m_Resources[i] = new DynamicArray<IRGResource>();
                m_Generations[i] = new List<ushort>(64);
            }
        }

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

        ushort NextGeneration(int type, int index)
        {
            List<ushort> generations = m_Generations[type];
            while (generations.Count <= index) generations.Add(0);
            ushort generation = (ushort)(generations[index] + 1);
            if (generation == 0) generation = 1;
            generations[index] = generation;
            return generation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComputeBuffer GetBuffer(in RGBufferRef bufferRef)
        {
            return ResolveBuffer(bufferRef).resource
                ?? throw new InvalidOperationException("Cannot resolve an imported GraphicsBuffer as a ComputeBuffer.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RTHandle GetTexture(in RGTextureRef textureRef)
        {
            RequireLive(textureRef.handle, "RGTextureRef");
            return GetTextureResource(textureRef.handle).resource;
        }

        internal bool TryGetTextureView(RenderTargetIdentifier identifier, out FTextureView view)
        {
            for (int i = m_Resources[(int)ERGResourceType.Texture].size - 1; i >= 0; --i)
            {
                var texture = (RGTexture)m_Resources[(int)ERGResourceType.Texture][i];
                if (texture.resource == null || texture.descriptor.width <= 0) continue;
                if (!new RenderTargetIdentifier(texture.resource.rt).Equals(identifier)) continue;
                view = texture.descriptor.view; return true;
            }
            view = default; return false;
        }

        static void RetainRecording(IRGResource resource, ResourceUsage usage)
        {
            resource.usage = usage;
            if (usage == null) return;
            usage.RetainRecording(); resource.recordingLease = true;
        }
        static void ReleaseRecording(IRGResource resource)
        {
            if (!resource.recordingLease) return;
            resource.usage.ReleaseRecording(); resource.recordingLease = false;
        }
        internal bool IsPoolTracked(RGResourceHandle handle) => m_Resources[handle.iType][handle.index].usage != null;
        internal void QueueAttempt(IRGPass pass, bool asyncCompute, ResourceReuseFence fence)
        {
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                foreach (var handle in pass.resourceReadLists[type]) m_Pool.QueueAttempt(m_Resources[type][handle.index].usage, asyncCompute, fence);
                foreach (var handle in pass.resourceWriteLists[type]) m_Pool.QueueAttempt(m_Resources[type][handle.index].usage, asyncCompute, fence);
            }
        }
        internal void SubmissionUncertain() => m_Pool.SubmissionUncertain();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BeginRender()
        {
            m_Pool.EnsureRecording();
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
                if (string.IsNullOrEmpty(name) && texture.resource != null) name = texture.resource.name;
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
            if (string.IsNullOrEmpty(name)) name = ((ERGResourceType)type) + " " + index;
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
            outRes.generation = NextGeneration(type, result);
            return result;
        }

        internal RGBuffer ResolveBuffer(in RGBufferRef bufferRef)
        {
            RequireLive(bufferRef.handle, "RGBufferRef");
            RGBuffer buffer = GetBufferResource(bufferRef.handle);
            if (buffer.resource == null && buffer.graphicsResource == null)
                throw new InvalidOperationException($"Graph buffer '{buffer.GetName()}' has no live resource.");
            return buffer;
        }

        RGBufferRef BufferHandle(int index)
        {
            return new RGBufferRef(index, m_Resources[(int)ERGResourceType.Buffer][index].generation);
        }

        RGTextureRef TextureHandle(int index)
        {
            return new RGTextureRef(index, m_Resources[(int)ERGResourceType.Texture][index].generation);
        }

        internal RGBufferRef ImportBuffer(in FBufferRef buffer)
        {
            if (buffer.buffer == null || !buffer.buffer.IsValid())
                throw new ArgumentException("Cannot import an invalid buffer.", nameof(buffer));
            if (m_ImportedBuffers.TryGetValue(buffer.buffer, out int existing)) return BufferHandle(existing);
            int index = AddNewResource((int)ERGResourceType.Buffer, m_Resources[(int)ERGResourceType.Buffer], out RGBuffer resource);
            m_ImportedBuffers.Add(buffer.buffer, index);
            resource.resource = buffer.buffer;
            RetainRecording(resource, m_Pool.BufferUsage(buffer.buffer));
            resource.descriptor = buffer.descriptor;
            resource.imported = true;
            return BufferHandle(index);
        }

        internal RGBufferRef ImportBuffer(GraphicsBuffer buffer, string name)
        {
            if (buffer == null || !buffer.IsValid())
                throw new ArgumentException("Cannot import an invalid GraphicsBuffer.", nameof(buffer));
            if (m_ImportedGraphicsBuffers.TryGetValue(buffer, out int existing)) return BufferHandle(existing);
            int index = AddNewResource((int)ERGResourceType.Buffer, m_Resources[(int)ERGResourceType.Buffer], out RGBuffer resource);
            m_ImportedGraphicsBuffers.Add(buffer, index);
            resource.graphicsResource = buffer;
            resource.graphicsTarget = buffer.target;
            resource.descriptor = new BufferDescriptor { name = name, count = buffer.count, stride = buffer.stride };
            resource.imported = true;
            return BufferHandle(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGBufferRef CreateBuffer(in BufferDescriptor descriptor)
        {
            int newHandle = AddNewResource((int)ERGResourceType.Buffer, m_Resources[(int)ERGResourceType.Buffer], out RGBuffer rdgBuffer);
            rdgBuffer.descriptor = descriptor;
            return BufferHandle(newHandle);
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
            RGBuffer buffer = GetBufferResource(handle);
            if (buffer.graphicsResource != null)
                throw new InvalidOperationException("Imported GraphicsBuffer targets cannot be cloned as ComputeBuffer descriptors.");
            return buffer.descriptor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RGTextureRef ImportTexture(RTHandle rt)
        {
            if (rt == null) throw new ArgumentNullException(nameof(rt));
            var identity = rt.rt != null ? new RenderTargetIdentifier(rt.rt) : rt.nameID;
            if (m_ImportedTextures.TryGetValue(identity, out int existing)) return TextureHandle(existing);
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

        internal RGTextureRef ImportTexture(RTHandle rt, in TextureDescriptor descriptor)
        {
            RGTextureRef reference = ImportTexture(rt);
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
        internal RGTextureRef ImportBackbuffer(in RenderTargetIdentifier backBuffer, in TextureDescriptor descriptor)
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
        internal RGTextureRef CreateTexture(in TextureDescriptor descriptor)
        {
            int newHandle = AddNewResource((int)ERGResourceType.Texture, m_Resources[(int)ERGResourceType.Texture], out RGTexture rdgTexture);
            rdgTexture.descriptor = textureCapacity.Resolve(descriptor);
            return TextureHandle(newHandle);
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
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created Compute Buffer ({0}). Buffer was probably declared for writing more than once in the same pass.", resource.descriptor.name));

                resource.resource = m_Pool.GetBuffer(desc, true, asyncCompute, out ResourceReuseFence reuseFence).buffer;
                reuseFence.Wait(graphContext.cmdBuffer, asyncCompute);
                RetainRecording(resource, m_Pool.BufferUsage(resource.resource));
                resource.cachedHash = hashCode;
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
                m_Pool.ReleaseBuffer(new FBufferRef(resource.cachedHash, resource.descriptor, resource.resource), reuseFence);
                resource.cachedHash = -1;
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
                int hashCode = desc.GetHashCode();

                if (resource.resource != null)
                    throw new InvalidOperationException(string.Format("Trying to create an already created texture ({0}). Texture was probably declared for writing more than once in the same pass.", resource.descriptor.name));

                resource.resource = m_Pool.GetTexture(desc, true, asyncCompute, out ResourceReuseFence reuseFence).texture;
                reuseFence.Wait(graphContext.cmdBuffer, asyncCompute);
                RetainRecording(resource, m_Pool.TextureUsage(resource.resource));
                resource.cachedHash = hashCode;
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
                m_Pool.ReleaseTexture(new FTextureRef(resource.cachedHash, resource.descriptor, resource.resource), reuseFence);
                resource.cachedHash = -1;
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
                    m_RetiredTextures.Add(new FTextureRef(texture.cachedHash, texture.descriptor, texture.resource));
                    texture.resource = null;
                }
            }
            for (int i = 0; i < m_Resources[(int)ERGResourceType.Buffer].size; i++)
            {
                RGBuffer buffer = (RGBuffer)m_Resources[(int)ERGResourceType.Buffer][i];
                if (!buffer.imported && buffer.resource != null)
                {
                    m_RetiredBuffers.Add(new FBufferRef(buffer.cachedHash, buffer.descriptor, buffer.resource));
                    buffer.resource = null;
                }
            }
            for (int type = 0; type < (int)ERGResourceType.Max; ++type)
            {
                for (int i = 0; i < m_Resources[type].size; ++i) ReleaseRecording(m_Resources[type][i]);
                m_Resources[type].Clear();
            }
        }

        internal void FlushRetired(ResourceReuseFence reuseFence = default)
        {
            foreach (FTextureRef texture in m_RetiredTextures)
                m_Pool.ReleaseTexture(texture, reuseFence);
            foreach (FBufferRef buffer in m_RetiredBuffers)
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
            if (m_OwnsPool) m_Pool.Dispose();
        }
    }
}
