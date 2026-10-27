using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    public class HistoryCache
    {
        class TextureSlot
        {
            public RTHandle committed;
            public RTHandle pending;
            public TextureDescriptor committedDescriptor, pendingDescriptor;
            public int generation;
            public bool producedThisFrame;
            public bool invalidateNextGet;
        }

        class BufferSlot
        {
            public ComputeBuffer committed;
            public ComputeBuffer pending;
            public BufferDescriptor committedDescriptor, pendingDescriptor;
            public int generation;
            public bool producedThisFrame;
            public bool invalidateNextGet;
        }

        FTextureCapacityContext m_TextureCapacity;
        public void SetTextureCapacity(FTextureCapacityContext capacity) => m_TextureCapacity = capacity;
        readonly Dictionary<int, TextureSlot> m_Textures;
        readonly Dictionary<int, BufferSlot> m_Buffers;
        readonly List<RTHandle> m_RetiredTextures;
        readonly List<ComputeBuffer> m_RetiredBuffers;
        readonly List<PendingDestroy> m_PendingDestroys = new List<PendingDestroy>();
        struct PendingDestroy
        {
            public RTHandle texture;
            public ComputeBuffer buffer;
            public ResourceReuseFence fence;
        }

        internal bool RetiredResourcesAreAlive
        {
            get
            {
                foreach (RTHandle texture in m_RetiredTextures)
                    if (texture == null || texture.rt == null || !texture.rt.IsCreated()) return false;
                foreach (ComputeBuffer buffer in m_RetiredBuffers)
                    if (buffer == null || !buffer.IsValid()) return false;
                for (int i = 0; i < m_PendingDestroys.Count; ++i)
                {
                    PendingDestroy pending = m_PendingDestroys[i];
                    if (pending.texture != null && (pending.texture.rt == null || !pending.texture.rt.IsCreated())) return false;
                    if (pending.buffer != null && !pending.buffer.IsValid()) return false;
                }
                return true;
            }
        }

        public int RetiredQueuedCount => m_RetiredTextures.Count + m_RetiredBuffers.Count;

        public HistoryCache()
        {
            m_Textures = new Dictionary<int, TextureSlot>();
            m_Buffers = new Dictionary<int, BufferSlot>();
            m_RetiredTextures = new List<RTHandle>();
            m_RetiredBuffers = new List<ComputeBuffer>();
        }

        public int TextureGeneration(in int id)
        {
            return m_Textures.TryGetValue(id, out TextureSlot slot) ? slot.generation : 0;
        }

        public BufferHandle GetBuffer(in int id, in BufferDescriptor descriptor)
        {
            BufferSlot slot = GetOrCreateBufferSlot(id);
            ReallocCommittedBufferIfNeeded(slot, descriptor);
            return new BufferHandle(-1, descriptor, slot.committed);
        }

        public BufferHandle GetWriteBuffer(in int id, in BufferDescriptor descriptor)
        {
            BufferSlot slot = GetOrCreateBufferSlot(id);
            if (slot.pending != null && !descriptor.Equals(slot.pendingDescriptor))
            {
                Retire(slot.pending);
                slot.pending = null;
                slot.producedThisFrame = false;
            }

            if (slot.pending == null)
            {
                slot.pending = new ComputeBuffer(descriptor.count, descriptor.stride, descriptor.type);
            }

            slot.pendingDescriptor = descriptor;
            return new BufferHandle(-1, descriptor, slot.pending);
        }

        public TextureHandle GetTexture(in int id, in TextureDescriptor descriptor)
        {
            return GetTexture(id, descriptor, out _);
        }

        public TextureHandle GetTexture(in int id, in TextureDescriptor requestedDescriptor, out bool created)
        {
            var descriptor = m_TextureCapacity.Resolve(requestedDescriptor);
            TextureSlot slot = GetOrCreateTextureSlot(id);
            created = slot.committed == null;
            if (created)
            {
                slot.committed = AllocTexture(descriptor);
                slot.committedDescriptor = descriptor;
            }
            else
            {
                // Reading never replaces accepted history. Keep its own logical view until commit.
                var requestedLogical = descriptor;
                var committedLogical = slot.committedDescriptor;
                requestedLogical.allocationWidth = requestedLogical.allocationHeight = 0;
                committedLogical.allocationWidth = committedLogical.allocationHeight = 0;
                created = !requestedLogical.Equals(committedLogical);
            }
            if (slot.invalidateNextGet)
            {
                created = true;
                slot.invalidateNextGet = false;
            }

            return new TextureHandle(-1, slot.committedDescriptor, slot.committed);
        }

        public TextureHandle GetWriteTexture(in int id, in TextureDescriptor requestedDescriptor)
        {
            var descriptor = m_TextureCapacity.Resolve(requestedDescriptor);
            TextureSlot slot = GetOrCreateTextureSlot(id);
            if (slot.pending != null && !descriptor.PhysicalDescriptor().Equals(slot.pendingDescriptor.PhysicalDescriptor()))
            {
                Retire(slot.pending);
                slot.pending = null;
                slot.producedThisFrame = false;
            }

            if (slot.pending == null)
            {
                slot.pending = AllocTexture(descriptor);
            }

            slot.pendingDescriptor = descriptor;
            return new TextureHandle(-1, descriptor, slot.pending);
        }

        public void Invalidate(in int id)
        {
            if (m_Textures.TryGetValue(id, out TextureSlot textureSlot))
            {
                textureSlot.invalidateNextGet = true;
            }

            if (m_Buffers.TryGetValue(id, out BufferSlot bufferSlot))
            {
                bufferSlot.invalidateNextGet = true;
            }
        }

        public void BeginFrame()
        {
            ReleaseCompletedDestroys();
            foreach (TextureSlot slot in m_Textures.Values)
            {
                slot.producedThisFrame = false;
            }

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                slot.producedThisFrame = false;
            }
        }

        public void MarkProduced(in int id)
        {
            if (m_Textures.TryGetValue(id, out TextureSlot textureSlot))
            {
                textureSlot.producedThisFrame = true;
            }

            if (m_Buffers.TryGetValue(id, out BufferSlot bufferSlot))
            {
                bufferSlot.producedThisFrame = true;
            }
        }

        public void CommitFrame()
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                if (slot.producedThisFrame && slot.pending != null)
                {
                    RTHandle previous = slot.committed;
                    slot.committed = slot.pending;
                    slot.pending = previous;
                    var previousDescriptor = slot.committedDescriptor;
                    slot.committedDescriptor = slot.pendingDescriptor;
                    slot.pendingDescriptor = previousDescriptor;
                    slot.generation++;
                }

                slot.producedThisFrame = false;
            }

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                if (slot.producedThisFrame && slot.pending != null)
                {
                    ComputeBuffer previous = slot.committed;
                    slot.committed = slot.pending;
                    slot.pending = previous;
                    var previousDescriptor = slot.committedDescriptor;
                    slot.committedDescriptor = slot.pendingDescriptor;
                    slot.pendingDescriptor = previousDescriptor;
                    slot.generation++;
                }

                slot.producedThisFrame = false;
            }
        }

        // The committed texture of a slot without creating or resizing it (editor tools read the last frame's result).
        public bool TryGetCommittedTexture(int id, out RTHandle texture)
        {
            texture = m_Textures.TryGetValue(id, out TextureSlot slot) ? slot.committed : null;
            return texture != null && texture.rt != null;
        }

        public void DiscardPending(int id)
        {
            if (m_Textures.TryGetValue(id, out TextureSlot texture))
            {
                Retire(texture.pending);
                texture.pending = null;
                texture.producedThisFrame = false;
            }
            if (m_Buffers.TryGetValue(id, out BufferSlot buffer))
            {
                Retire(buffer.pending);
                buffer.pending = null;
                buffer.producedThisFrame = false;
            }
        }

        public void DiscardUnproducedPending()
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                if (slot.producedThisFrame) continue;
                Retire(slot.pending);
                slot.pending = null;
            }
            foreach (BufferSlot slot in m_Buffers.Values)
            {
                if (slot.producedThisFrame) continue;
                Retire(slot.pending);
                slot.pending = null;
            }
        }

        public void RollbackPending()
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                if (slot.pending != null && slot.pending != slot.committed)
                {
                    Retire(slot.pending);
                    slot.pending = null;
                }

                slot.producedThisFrame = false;
            }

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                if (slot.pending != null && slot.pending != slot.committed)
                {
                    Retire(slot.pending);
                    slot.pending = null;
                }

                slot.producedThisFrame = false;
            }
        }

        public void FlushRetired() => FlushRetired(default);

        // A fence parks the physical destroy until the GPU has finished the frame that retired it.
        // Without a fence (tests, devices that cannot create one) the resources are released now.
        // The fence type stays internal, matching MeshWorld.FlushRetired.
        internal void FlushRetired(ResourceReuseFence fence)
        {
            ReleaseCompletedDestroys();
            bool wait = fence.hasGraphics || fence.hasCompute;
            if (!wait)
            {
                ReleaseRetiredNow();
                return;
            }

            for (int i = 0; i < m_RetiredTextures.Count; ++i)
            {
                if (m_RetiredTextures[i] != null)
                    m_PendingDestroys.Add(new PendingDestroy { texture = m_RetiredTextures[i], fence = fence });
            }
            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
            {
                if (m_RetiredBuffers[i] != null)
                    m_PendingDestroys.Add(new PendingDestroy { buffer = m_RetiredBuffers[i], fence = fence });
            }
            m_RetiredTextures.Clear();
            m_RetiredBuffers.Clear();
        }

        void ReleaseRetiredNow()
        {
            for (int i = 0; i < m_RetiredTextures.Count; ++i)
                if (m_RetiredTextures[i] != null) RTHandles.Release(m_RetiredTextures[i]);
            m_RetiredTextures.Clear();
            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
                if (m_RetiredBuffers[i] != null) m_RetiredBuffers[i].Release();
            m_RetiredBuffers.Clear();
        }

        void ReleaseCompletedDestroys()
        {
            for (int i = m_PendingDestroys.Count - 1; i >= 0; --i)
            {
                if (!m_PendingDestroys[i].fence.passed) continue;
                DestroyPending(m_PendingDestroys[i]);
                m_PendingDestroys.RemoveAt(i);
            }
        }

        static void DestroyPending(PendingDestroy pending)
        {
            if (pending.texture != null) RTHandles.Release(pending.texture);
            if (pending.buffer != null) pending.buffer.Release();
        }

        public void Release()
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                Retire(slot.committed);
                if (slot.pending != null && slot.pending != slot.committed)
                {
                    Retire(slot.pending);
                }
            }
            m_Textures.Clear();

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                Retire(slot.committed);
                if (slot.pending != null && slot.pending != slot.committed)
                {
                    Retire(slot.pending);
                }
            }
            m_Buffers.Clear();
        }

        public void ForceFlushForTeardown()
        {
            ReleaseCompletedDestroys();
            ReleaseRetiredNow();
            // Teardown has no later frame to poll. Completed fences are already released; anything still
            // in flight is orphaned and destroyed when its fence passes.
            if (m_PendingDestroys.Count == 0) return;
            PendingDestroyQueue.Adopt(m_PendingDestroys);
            m_PendingDestroys.Clear();
        }

        TextureSlot GetOrCreateTextureSlot(in int id)
        {
            if (!m_Textures.TryGetValue(id, out TextureSlot slot))
            {
                slot = new TextureSlot();
                m_Textures.Add(id, slot);
            }

            return slot;
        }

        BufferSlot GetOrCreateBufferSlot(in int id)
        {
            if (!m_Buffers.TryGetValue(id, out BufferSlot slot))
            {
                slot = new BufferSlot();
                m_Buffers.Add(id, slot);
            }

            return slot;
        }

        void ReallocCommittedBufferIfNeeded(BufferSlot slot, in BufferDescriptor descriptor)
        {
            if (slot.committed != null && !descriptor.Equals(slot.committedDescriptor))
            {
                Retire(slot.committed);
                slot.committed = null;
            }

            if (slot.committed == null)
            {
                slot.committed = new ComputeBuffer(descriptor.count, descriptor.stride, descriptor.type);
                slot.committedDescriptor = descriptor;
            }
            else
            {
                slot.committedDescriptor = descriptor;
            }
        }

        static RTHandle AllocTexture(in TextureDescriptor descriptor)
        {
            return RTHandles.Alloc(
                descriptor.physicalWidth,
                descriptor.physicalHeight,
                descriptor.slices,
                (DepthBits)descriptor.depthBufferBits,
                descriptor.colorFormat,
                descriptor.filterMode,
                descriptor.wrapMode,
                descriptor.dimension,
                descriptor.enableRandomWrite,
                descriptor.useMipMap,
                descriptor.autoGenerateMips,
                descriptor.isShadowMap,
                descriptor.anisoLevel,
                descriptor.mipMapBias,
                (MSAASamples)descriptor.msaaSamples,
                descriptor.bindTextureMS,
                false,
                false,
                descriptor.memoryless,
                VRTextureUsage.None,
                descriptor.name);
        }

        void Retire(RTHandle texture)
        {
            if (texture != null)
            {
                m_RetiredTextures.Add(texture);
            }
        }

        void Retire(ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                m_RetiredBuffers.Add(buffer);
            }
        }

        static class PendingDestroyQueue
        {
            static readonly List<PendingDestroy> s_Orphaned = new List<PendingDestroy>();
            static bool s_Polling;

            public static void Adopt(List<PendingDestroy> pending)
            {
                s_Orphaned.AddRange(pending);
                if (s_Polling || s_Orphaned.Count == 0) return;
                s_Polling = true;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update += Poll;
#else
                Application.onBeforeRender += Poll;
#endif
            }

            static void Poll()
            {
                for (int i = s_Orphaned.Count - 1; i >= 0; --i)
                {
                    if (!s_Orphaned[i].fence.passed) continue;
                    DestroyPending(s_Orphaned[i]);
                    s_Orphaned.RemoveAt(i);
                }
                if (s_Orphaned.Count != 0) return;
                s_Polling = false;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update -= Poll;
#else
                Application.onBeforeRender -= Poll;
#endif
            }
        }
    }
}
