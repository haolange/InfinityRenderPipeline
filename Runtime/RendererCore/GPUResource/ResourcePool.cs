using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public class ResourcePool
    {
        public BufferCache buffers { get; }
        public TextureCache textures { get; }
        readonly Dictionary<ComputeBuffer, ResourceUsage> m_BufferUses = new Dictionary<ComputeBuffer, ResourceUsage>();
        readonly Dictionary<RTHandle, ResourceUsage> m_TextureUses = new Dictionary<RTHandle, ResourceUsage>();
        readonly Dictionary<RenderTexture, ResourceUsage> m_TexturePhysicalUses = new Dictionary<RenderTexture, ResourceUsage>();
        readonly HashSet<ResourceUsage> m_PendingUses = new HashSet<ResourceUsage>();
        readonly HashSet<int> m_SubmittedAwaitingJoin = new HashSet<int>();
        int m_RecordingEpoch;
        bool m_RecordingOpen, m_DisposeRequested, m_DisposalPolling, m_NeedsQueueDrain;
        readonly List<ResourceUsage> m_CompletedUses = new List<ResourceUsage>();

        internal int recordingEpoch => m_RecordingEpoch;
        internal bool requiresQueueDrain => m_NeedsQueueDrain;
        internal void QueueDrainAccepted() => m_NeedsQueueDrain = false;
        internal void BeginFrame()
        {
            if (m_RecordingOpen) SubmissionUncertain(true);
            unchecked { m_RecordingEpoch++; }
            m_RecordingOpen = true;
        }
        internal void EnsureRecording() { if (!m_RecordingOpen) BeginFrame(); }
        internal void QueueAttempt(ResourceUsage usage, bool asyncCompute, ResourceReuseFence fence)
        {
            if (usage == null) return;
            EnsureRecording();
            usage.QueueAttempt(m_RecordingEpoch, asyncCompute, fence);
            m_PendingUses.Add(usage);
        }
        internal void SubmissionUncertain(bool closeRecording = false)
        {
            foreach (var usage in m_PendingUses) usage.MarkUncertain();
            m_NeedsQueueDrain |= m_PendingUses.Count != 0;
            if (closeRecording) m_RecordingOpen = false;
        }
        internal void SubmissionNeedsJoin()
        {
            m_SubmittedAwaitingJoin.Add(m_RecordingEpoch);
            SubmissionUncertain(true);
        }
        public void AfterSubmit(GraphicsFence fence) => AfterSubmit(fence, default);
        internal void AfterSubmit(GraphicsFence completion, ResourceReuseFence joinedTail)
        {
            AcknowledgeSubmission(m_RecordingEpoch, completion, joinedTail);
            if (!m_NeedsQueueDrain)
            {
                foreach (int epoch in m_SubmittedAwaitingJoin) AcknowledgeSubmission(epoch, completion, joinedTail);
                m_SubmittedAwaitingJoin.Clear();
            }
            m_RecordingOpen = false;
            textures.AfterSubmit(400L * 1024 * 1024, completion);
            buffers.AfterSubmit(64L * 1024 * 1024, completion);
        }
        internal void AcknowledgeSubmission(int epoch, GraphicsFence completion, ResourceReuseFence joinedTail)
        {
            m_CompletedUses.Clear();
            foreach (var usage in m_PendingUses)
            {
                usage.Submitted(epoch, completion, SystemInfo.supportsGraphicsFence, joinedTail);
                if (!usage.pending) m_CompletedUses.Add(usage);
            }
            foreach (var usage in m_CompletedUses) m_PendingUses.Remove(usage);
            m_CompletedUses.Clear();
        }
        internal ResourceUsage BufferUsage(ComputeBuffer buffer) => buffer != null && m_BufferUses.TryGetValue(buffer, out var usage) ? usage : null;
        internal ResourceUsage TextureUsage(RTHandle texture)
        {
            if (texture == null) return null;
            if (m_TextureUses.TryGetValue(texture, out var usage)) return usage;
            return texture.rt != null && m_TexturePhysicalUses.TryGetValue(texture.rt, out usage) ? usage : null;
        }
        internal bool CanWriteBuffer(in FBufferRef buffer)
        {
            var usage = BufferUsage(buffer.buffer) ?? throw new InvalidOperationException("CPU writes require a buffer owned by this pool.");
            return !usage.returned && usage.CanWriteCpu;
        }

        public ResourcePool()
        {
            buffers = new BufferCache();
            textures = new TextureCache();
            buffers.resourceDestroyed = resource => m_BufferUses.Remove(resource);
            textures.resourceDestroyed = resource =>
            {
                if (m_TextureUses.TryGetValue(resource, out var usage)) m_TexturePhysicalUses.Remove(usage.textureIdentity);
                m_TextureUses.Remove(resource);
            };
        }

        public FBufferRef GetBuffer(in BufferDescriptor descriptor)
            => GetBuffer(descriptor, false, false, out _);

        internal FBufferRef GetBuffer(in BufferDescriptor descriptor, bool commandUse, bool asyncCompute, out ResourceReuseFence reuseFence)
        {
            if (m_DisposeRequested) throw new ObjectDisposedException(nameof(ResourcePool));
            if (commandUse) EnsureRecording();
            ComputeBuffer buffer;
            int handle = descriptor.GetHashCode();

            if (!buffers.Pull(handle, descriptor, commandUse, asyncCompute, m_RecordingEpoch, out buffer, out reuseFence))
            {
                buffer = new ComputeBuffer(descriptor.count, descriptor.stride, descriptor.type);
                buffer.name = descriptor.name;
                buffers.RecordAllocation(descriptor);
                m_BufferUses.Add(buffer, new ResourceUsage());
            }
            m_BufferUses[buffer].returned = false;

            return new FBufferRef(handle, descriptor, buffer);
        }

        public void ReleaseBuffer(in FBufferRef bufferRef) => ReleaseBuffer(bufferRef, default);

        internal void ReleaseBuffer(in FBufferRef bufferRef, ResourceReuseFence reuseFence)
        {
            var usage = BufferUsage(bufferRef.buffer) ?? throw new InvalidOperationException("Cannot return a buffer owned by another pool.");
            if (usage.returned) throw new InvalidOperationException("Buffer returned twice.");
            usage.SetTail(reuseFence); usage.returned = true;
            buffers.Push(bufferRef.handle, bufferRef.descriptor, bufferRef.buffer, reuseFence, usage);
        }

        public FTextureRef GetTexture(in TextureDescriptor descriptor)
            => GetTexture(descriptor, false, false, out _);

        internal FTextureRef GetTexture(in TextureDescriptor descriptor, bool commandUse, bool asyncCompute, out ResourceReuseFence reuseFence)
        {
            if (m_DisposeRequested) throw new ObjectDisposedException(nameof(ResourcePool));
            if (commandUse) EnsureRecording();
            RTHandle texture;
            var physical = descriptor.PhysicalDescriptor();
            int handle = physical.GetHashCode();

            if (!textures.Pull(handle, physical, commandUse, asyncCompute, m_RecordingEpoch, out texture, out reuseFence))
            {
                texture = RTHandles.Alloc(descriptor.physicalWidth, descriptor.physicalHeight, descriptor.slices, (DepthBits)descriptor.depthBufferBits, descriptor.colorFormat, descriptor.filterMode, descriptor.wrapMode, descriptor.dimension, descriptor.enableRandomWrite,
                                          descriptor.useMipMap, descriptor.autoGenerateMips, descriptor.isShadowMap, descriptor.anisoLevel, descriptor.mipMapBias, (MSAASamples)descriptor.msaaSamples, descriptor.bindTextureMS, false, false, RenderTextureMemoryless.None, VRTextureUsage.None, descriptor.name);
                textures.RecordAllocation(physical);
                var usage = new ResourceUsage { textureIdentity = texture.rt };
                m_TextureUses.Add(texture, usage); m_TexturePhysicalUses.Add(texture.rt, usage);
            }
            m_TextureUses[texture].returned = false;

            return new FTextureRef(handle, descriptor, texture);
        }

        public void ReleaseTexture(in FTextureRef textureRef) => ReleaseTexture(textureRef, default);

        internal void ReleaseTexture(in FTextureRef textureRef, ResourceReuseFence reuseFence)
        {
            var usage = TextureUsage(textureRef.texture) ?? throw new InvalidOperationException("Cannot return a texture owned by another pool.");
            if (usage.returned) throw new InvalidOperationException("Texture returned twice.");
            usage.SetTail(reuseFence); usage.returned = true;
            var physical = textureRef.descriptor.PhysicalDescriptor();
            textures.Push(physical.GetHashCode(), physical, textureRef.texture, reuseFence, usage);
        }

        public void Dispose()
        {
            m_DisposeRequested = true;
            PollDisposal();
            if (m_BufferUses.Count == 0 && m_TextureUses.Count == 0 || m_DisposalPolling) return;
            // Keep the physical owner alive until its recorded/submitted uses really finish.
            // Polling never treats an update, next frame or a GPU wait as completion.
            m_DisposalPolling = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += PollDisposal;
#else
            Application.onBeforeRender += PollDisposal;
#endif
        }
        void PollDisposal()
        {
            buffers.DisposeCompleted(); textures.DisposeCompleted();
            if (m_BufferUses.Count != 0 || m_TextureUses.Count != 0 || !m_DisposalPolling) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= PollDisposal;
#else
            Application.onBeforeRender -= PollDisposal;
#endif
            m_DisposalPolling = false;
        }
    }
}
