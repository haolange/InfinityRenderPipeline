using System.Collections.Generic;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    // Double-buffered history per id: one committed and one pending acquisition from the resource pool. A replaced or
    // discarded acquisition is retired and returned to the pool with the frame's reuse fence; the pool owns physical
    // lifetime, so history never allocates or destroys a resource itself.
    public class HistoryCache : IFrameTransaction
    {
        class TextureSlot
        {
            public TextureHandle committed;
            public TextureHandle pending;
            public int generation;
            public bool producedThisFrame;
        }

        class BufferSlot
        {
            public BufferHandle committed;
            public BufferHandle pending;
            public int generation;
            public bool producedThisFrame;
        }

        readonly ResourcePool m_Pool;
        TextureCapacityContext m_TextureCapacity;
        public void SetTextureCapacity(TextureCapacityContext capacity) => m_TextureCapacity = capacity;
        readonly Dictionary<int, TextureSlot> m_Textures = new Dictionary<int, TextureSlot>();
        readonly Dictionary<int, BufferSlot> m_Buffers = new Dictionary<int, BufferSlot>();
        readonly List<TextureHandle> m_RetiredTextures = new List<TextureHandle>();
        readonly List<BufferHandle> m_RetiredBuffers = new List<BufferHandle>();

        internal bool RetiredResourcesAreAlive
        {
            get
            {
                foreach (TextureHandle texture in m_RetiredTextures)
                    if (texture.texture.rt == null || !texture.texture.rt.IsCreated())
                    {
                        return false;
                    }
                foreach (BufferHandle buffer in m_RetiredBuffers)
                    if (!buffer.buffer.IsValid())
                    {
                        return false;
                    }
                return true;
            }
        }

        public int RetiredQueuedCount => m_RetiredTextures.Count + m_RetiredBuffers.Count;

        public HistoryCache(ResourcePool pool)
        {
            m_Pool = pool ?? throw new System.ArgumentNullException(nameof(pool));
        }

        public int TextureGeneration(in int id)
        {
            return m_Textures.TryGetValue(id, out TextureSlot slot) ? slot.generation : 0;
        }

        public BufferHandle GetBuffer(in int id, in BufferDescriptor descriptor)
        {
            BufferSlot slot = GetOrCreateBufferSlot(id);
            if (slot.committed.buffer != null && !descriptor.Equals(slot.committed.descriptor))
            {
                Retire(slot.committed);
                slot.committed = default;
            }
            if (slot.committed.buffer == null)
            {
                slot.committed = m_Pool.GetBuffer(descriptor);
            }
            return slot.committed;
        }

        public BufferHandle GetWriteBuffer(in int id, in BufferDescriptor descriptor)
        {
            BufferSlot slot = GetOrCreateBufferSlot(id);
            if (slot.pending.buffer != null && !descriptor.Equals(slot.pending.descriptor))
            {
                Retire(slot.pending);
                slot.pending = default;
                slot.producedThisFrame = false;
            }
            if (slot.pending.buffer == null)
            {
                slot.pending = m_Pool.GetBuffer(descriptor);
            }
            return slot.pending;
        }

        public TextureHandle GetTexture(in int id, in TextureDescriptor descriptor)
        {
            return GetTexture(id, descriptor, out _);
        }

        public TextureHandle GetTexture(in int id, in TextureDescriptor requestedDescriptor, out bool created)
        {
            var descriptor = m_TextureCapacity.Resolve(requestedDescriptor);
            TextureSlot slot = GetOrCreateTextureSlot(id);
            created = slot.committed.texture == null;
            if (created)
            {
                slot.committed = m_Pool.GetTexture(descriptor);
            }
            else
            {
                // Reading never replaces accepted history. Keep its own logical view until commit.
                var requestedLogical = descriptor;
                var committedLogical = slot.committed.descriptor;
                requestedLogical.allocationWidth = requestedLogical.allocationHeight = 0;
                committedLogical.allocationWidth = committedLogical.allocationHeight = 0;
                created = !requestedLogical.Equals(committedLogical);
            }
            return slot.committed;
        }

        public TextureHandle GetWriteTexture(in int id, in TextureDescriptor requestedDescriptor)
        {
            var descriptor = m_TextureCapacity.Resolve(requestedDescriptor);
            TextureSlot slot = GetOrCreateTextureSlot(id);
            if (slot.pending.texture != null && !descriptor.PhysicalDescriptor().Equals(slot.pending.descriptor.PhysicalDescriptor()))
            {
                Retire(slot.pending);
                slot.pending = default;
                slot.producedThisFrame = false;
            }
            if (slot.pending.texture == null)
            {
                slot.pending = m_Pool.GetTexture(descriptor);
            }
            else if (!descriptor.Equals(slot.pending.descriptor))
            {
                // Same physical allocation, new logical view: keep the acquisition and carry the requested view.
                slot.pending = new TextureHandle(slot.pending.lease, descriptor, slot.pending.texture);
            }
            return slot.pending;
        }

        public void BeginFrame()
        {
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
                if (slot.producedThisFrame && slot.pending.texture != null)
                {
                    TextureHandle previous = slot.committed;
                    slot.committed = slot.pending;
                    slot.pending = previous;
                    slot.generation++;
                }

                slot.producedThisFrame = false;
            }

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                if (slot.producedThisFrame && slot.pending.buffer != null)
                {
                    BufferHandle previous = slot.committed;
                    slot.committed = slot.pending;
                    slot.pending = previous;
                    slot.generation++;
                }

                slot.producedThisFrame = false;
            }
        }

        // The committed texture of a slot without creating or resizing it (editor tools read the last frame's result).
        public bool TryGetCommittedTexture(int id, out RTHandle texture)
        {
            texture = m_Textures.TryGetValue(id, out TextureSlot slot) ? slot.committed.texture : null;
            return texture != null && texture.rt != null;
        }

        public void DiscardPending(int id)
        {
            if (m_Textures.TryGetValue(id, out TextureSlot texture))
            {
                Retire(texture.pending);
                texture.pending = default;
                texture.producedThisFrame = false;
            }
            if (m_Buffers.TryGetValue(id, out BufferSlot buffer))
            {
                Retire(buffer.pending);
                buffer.pending = default;
                buffer.producedThisFrame = false;
            }
        }

        public void DiscardUnproducedPending()
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                if (slot.producedThisFrame)
                {
                    continue;
                }
                Retire(slot.pending);
                slot.pending = default;
            }
            foreach (BufferSlot slot in m_Buffers.Values)
            {
                if (slot.producedThisFrame)
                {
                    continue;
                }
                Retire(slot.pending);
                slot.pending = default;
            }
        }

        public void RollbackPending()
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                Retire(slot.pending);
                slot.pending = default;

                slot.producedThisFrame = false;
            }

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                Retire(slot.pending);
                slot.pending = default;

                slot.producedThisFrame = false;
            }
        }

        // A camera-owned history holds only that camera's writes, so a failed camera discards all of them.
        public void CameraFailed(bool queueUncertain) => RollbackPending();

        // Returns every retired acquisition to the pool. The pool reuses or destroys it only after the fence and its own
        // recorded GPU uses complete.
        public void FlushRetired(ResourceReuseFence fence)
        {
            // An entry leaves the list before it is returned, so a failed return never repeats an earlier one.
            for (int i = m_RetiredTextures.Count - 1; i >= 0; --i)
            {
                TextureHandle texture = m_RetiredTextures[i];
                m_RetiredTextures.RemoveAt(i);
                m_Pool.ReleaseTexture(texture, fence);
            }
            for (int i = m_RetiredBuffers.Count - 1; i >= 0; --i)
            {
                BufferHandle buffer = m_RetiredBuffers[i];
                m_RetiredBuffers.RemoveAt(i);
                m_Pool.ReleaseBuffer(buffer, fence);
            }
        }

        public void Dispose() => Dispose(default);

        // Retires every slot and returns all acquisitions to the pool with fence, which covers GPU use outside the
        // graph's own tracking; the owner disposes before the pool does.
        internal void Dispose(ResourceReuseFence fence)
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                Retire(slot.committed);
                Retire(slot.pending);
            }
            m_Textures.Clear();
            foreach (BufferSlot slot in m_Buffers.Values)
            {
                Retire(slot.committed);
                Retire(slot.pending);
            }
            m_Buffers.Clear();
            FlushRetired(fence);
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

        void Retire(in TextureHandle texture)
        {
            if (texture.texture != null)
            {
                m_RetiredTextures.Add(texture);
            }
        }

        void Retire(in BufferHandle buffer)
        {
            if (buffer.buffer != null)
            {
                m_RetiredBuffers.Add(buffer);
            }
        }
    }
}
