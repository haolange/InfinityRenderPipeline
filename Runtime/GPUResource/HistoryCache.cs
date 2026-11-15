using System.Collections.Generic;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.GPUResource
{
    // A committed history texture and whether it holds an earlier frame's contents of the requested layout.
    internal readonly struct HistoryRead
    {
        internal readonly TextureHandle textureHandle;
        internal readonly bool valid;

        internal HistoryRead(in TextureHandle textureHandle, bool valid)
        {
            this.textureHandle = textureHandle;
            this.valid = valid;
        }
    }

    // Double-buffered history per id: one committed and one pending acquisition from the resource pool. A replaced or
    // discarded acquisition is retired and returned to the pool with the frame's reuse fence; the pool owns physical
    // lifetime, so history never allocates or destroys a resource itself.
    internal class HistoryCache : IFrameTransaction
    {
        class TextureSlot
        {
            public TextureHandle committedHandle;
            public TextureHandle pendingHandle;
            public int generation;
            public bool producedThisFrame;
        }

        class BufferSlot
        {
            public BufferHandle committedHandle;
            public BufferHandle pendingHandle;
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
                foreach (TextureHandle textureHandle in m_RetiredTextures)
                    if (textureHandle.texture.rt == null || !textureHandle.texture.rt.IsCreated())
                    {
                        return false;
                    }
                foreach (BufferHandle bufferHandle in m_RetiredBuffers)
                    if (!bufferHandle.buffer.IsValid())
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
            if (slot.committedHandle.buffer != null && !descriptor.Equals(slot.committedHandle.descriptor))
            {
                Retire(slot.committedHandle);
                slot.committedHandle = default;
            }
            if (slot.committedHandle.buffer == null)
            {
                slot.committedHandle = m_Pool.GetBuffer(descriptor);
            }
            return slot.committedHandle;
        }

        public BufferHandle GetWriteBuffer(in int id, in BufferDescriptor descriptor)
        {
            BufferSlot slot = GetOrCreateBufferSlot(id);
            if (slot.pendingHandle.buffer != null && !descriptor.Equals(slot.pendingHandle.descriptor))
            {
                Retire(slot.pendingHandle);
                slot.pendingHandle = default;
                slot.producedThisFrame = false;
            }
            if (slot.pendingHandle.buffer == null)
            {
                slot.pendingHandle = m_Pool.GetBuffer(descriptor);
            }
            return slot.pendingHandle;
        }

        public TextureHandle GetTexture(in int id, in TextureDescriptor descriptor)
        {
            return GetTexture(id, descriptor, out _);
        }

        public TextureHandle GetTexture(in int id, in TextureDescriptor requestedDescriptor, out bool created)
        {
            var descriptor = m_TextureCapacity.Resolve(requestedDescriptor);
            TextureSlot slot = GetOrCreateTextureSlot(id);
            created = slot.committedHandle.texture == null;
            if (created)
            {
                slot.committedHandle = m_Pool.GetTexture(descriptor);
            }
            else
            {
                // Reading never replaces accepted history. Keep its own logical view until commit.
                var requestedLogical = descriptor;
                var committedLogical = slot.committedHandle.descriptor;
                requestedLogical.allocationWidth = requestedLogical.allocationHeight = 0;
                committedLogical.allocationWidth = committedLogical.allocationHeight = 0;
                created = !requestedLogical.Equals(committedLogical);
            }
            return slot.committedHandle;
        }

        // The one validity rule for history: the committed texture has the requested logical layout, was committed at least
        // once, and the camera did not discard its history this frame. A feature that may skip frames also checks that it
        // produced in the last committed frame.
        public HistoryRead Read(in int id, in TextureDescriptor descriptor, bool resetHistory)
        {
            TextureHandle textureHandle = GetTexture(id, descriptor, out bool created);
            return new HistoryRead(textureHandle, !resetHistory && !created && TextureGeneration(id) > 0);
        }

        public TextureHandle GetWriteTexture(in int id, in TextureDescriptor requestedDescriptor)
        {
            var descriptor = m_TextureCapacity.Resolve(requestedDescriptor);
            TextureSlot slot = GetOrCreateTextureSlot(id);
            if (slot.pendingHandle.texture != null && !descriptor.PhysicalDescriptor().Equals(slot.pendingHandle.descriptor.PhysicalDescriptor()))
            {
                Retire(slot.pendingHandle);
                slot.pendingHandle = default;
                slot.producedThisFrame = false;
            }
            if (slot.pendingHandle.texture == null)
            {
                slot.pendingHandle = m_Pool.GetTexture(descriptor);
            }
            else if (!descriptor.Equals(slot.pendingHandle.descriptor))
            {
                // Same physical allocation, new logical view: keep the acquisition and carry the requested view.
                slot.pendingHandle = new TextureHandle(slot.pendingHandle.lease, descriptor, slot.pendingHandle.texture);
            }
            return slot.pendingHandle;
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
                if (slot.producedThisFrame && slot.pendingHandle.texture != null)
                {
                    TextureHandle previousHandle = slot.committedHandle;
                    slot.committedHandle = slot.pendingHandle;
                    slot.pendingHandle = previousHandle;
                    slot.generation++;
                }

                slot.producedThisFrame = false;
            }

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                if (slot.producedThisFrame && slot.pendingHandle.buffer != null)
                {
                    BufferHandle previousHandle = slot.committedHandle;
                    slot.committedHandle = slot.pendingHandle;
                    slot.pendingHandle = previousHandle;
                    slot.generation++;
                }

                slot.producedThisFrame = false;
            }
        }

        // The committed texture of a slot without creating or resizing it (editor tools read the last frame's result).
        public bool TryGetCommittedTexture(int id, out RTHandle texture)
        {
            texture = m_Textures.TryGetValue(id, out TextureSlot slot) ? slot.committedHandle.texture : null;
            return texture != null && texture.rt != null;
        }

        public void DiscardPending(int id)
        {
            if (m_Textures.TryGetValue(id, out TextureSlot texture))
            {
                Retire(texture.pendingHandle);
                texture.pendingHandle = default;
                texture.producedThisFrame = false;
            }
            if (m_Buffers.TryGetValue(id, out BufferSlot buffer))
            {
                Retire(buffer.pendingHandle);
                buffer.pendingHandle = default;
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
                Retire(slot.pendingHandle);
                slot.pendingHandle = default;
            }
            foreach (BufferSlot slot in m_Buffers.Values)
            {
                if (slot.producedThisFrame)
                {
                    continue;
                }
                Retire(slot.pendingHandle);
                slot.pendingHandle = default;
            }
        }

        public void RollbackPending()
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                Retire(slot.pendingHandle);
                slot.pendingHandle = default;

                slot.producedThisFrame = false;
            }

            foreach (BufferSlot slot in m_Buffers.Values)
            {
                Retire(slot.pendingHandle);
                slot.pendingHandle = default;

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
                TextureHandle textureHandle = m_RetiredTextures[i];
                m_RetiredTextures.RemoveAt(i);
                m_Pool.ReleaseTexture(textureHandle, fence);
            }
            for (int i = m_RetiredBuffers.Count - 1; i >= 0; --i)
            {
                BufferHandle bufferHandle = m_RetiredBuffers[i];
                m_RetiredBuffers.RemoveAt(i);
                m_Pool.ReleaseBuffer(bufferHandle, fence);
            }
        }

        public void Dispose() => Dispose(default);

        // Retires every slot and returns all acquisitions to the pool with fence, which covers GPU use outside the
        // graph's own tracking; the owner disposes before the pool does.
        internal void Dispose(ResourceReuseFence fence)
        {
            foreach (TextureSlot slot in m_Textures.Values)
            {
                Retire(slot.committedHandle);
                Retire(slot.pendingHandle);
            }
            m_Textures.Clear();
            foreach (BufferSlot slot in m_Buffers.Values)
            {
                Retire(slot.committedHandle);
                Retire(slot.pendingHandle);
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

        void Retire(in TextureHandle textureHandle)
        {
            if (textureHandle.texture != null)
            {
                m_RetiredTextures.Add(textureHandle);
            }
        }

        void Retire(in BufferHandle bufferHandle)
        {
            if (bufferHandle.buffer != null)
            {
                m_RetiredBuffers.Add(bufferHandle);
            }
        }
    }
}
