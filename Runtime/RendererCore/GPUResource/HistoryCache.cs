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

        readonly Dictionary<int, TextureSlot> m_Textures;
        readonly Dictionary<int, BufferSlot> m_Buffers;
        readonly List<RTHandle> m_RetiredTextures;
        readonly List<ComputeBuffer> m_RetiredBuffers;

        internal bool RetiredResourcesAreAlive
        {
            get
            {
                foreach (RTHandle texture in m_RetiredTextures)
                    if (texture == null || texture.rt == null || !texture.rt.IsCreated()) return false;
                foreach (ComputeBuffer buffer in m_RetiredBuffers)
                    if (buffer == null || !buffer.IsValid()) return false;
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

        public FBufferRef GetBuffer(in int id, in BufferDescriptor descriptor)
        {
            BufferSlot slot = GetOrCreateBufferSlot(id);
            ReallocCommittedBufferIfNeeded(slot, descriptor);
            return new FBufferRef(-1, descriptor, slot.committed);
        }

        public FBufferRef GetWriteBuffer(in int id, in BufferDescriptor descriptor)
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
            return new FBufferRef(-1, descriptor, slot.pending);
        }

        public FTextureRef GetTexture(in int id, in TextureDescriptor descriptor)
        {
            return GetTexture(id, descriptor, out _);
        }

        public FTextureRef GetTexture(in int id, in TextureDescriptor descriptor, out bool created)
        {
            TextureSlot slot = GetOrCreateTextureSlot(id);
            created = ReallocCommittedTextureIfNeeded(slot, descriptor);
            if (slot.invalidateNextGet)
            {
                created = true;
                slot.invalidateNextGet = false;
            }

            return new FTextureRef(-1, descriptor, slot.committed);
        }

        public FTextureRef GetWriteTexture(in int id, in TextureDescriptor descriptor)
        {
            TextureSlot slot = GetOrCreateTextureSlot(id);
            if (slot.pending != null && !descriptor.Equals(slot.pendingDescriptor))
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
            return new FTextureRef(-1, descriptor, slot.pending);
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

        public void FlushRetired()
        {
            for (int i = 0; i < m_RetiredTextures.Count; ++i)
            {
                RTHandle texture = m_RetiredTextures[i];
                if (texture != null)
                {
                    RTHandles.Release(texture);
                }
            }
            m_RetiredTextures.Clear();

            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
            {
                ComputeBuffer buffer = m_RetiredBuffers[i];
                if (buffer != null)
                {
                    buffer.Release();
                }
            }
            m_RetiredBuffers.Clear();
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
            FlushRetired();
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

        bool ReallocCommittedTextureIfNeeded(TextureSlot slot, in TextureDescriptor descriptor)
        {
            if (slot.committed != null && !descriptor.Equals(slot.committedDescriptor))
            {
                Retire(slot.committed);
                slot.committed = null;
            }

            if (slot.committed == null)
            {
                slot.committed = AllocTexture(descriptor);
                slot.committedDescriptor = descriptor;
                return true;
            }

            slot.committedDescriptor = descriptor;
            return false;
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
                descriptor.width,
                descriptor.height,
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
                RenderTextureMemoryless.None,
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
    }
}
