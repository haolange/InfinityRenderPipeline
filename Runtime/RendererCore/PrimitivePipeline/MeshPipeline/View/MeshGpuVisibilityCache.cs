using System;
using System.Collections.Generic;
using Unity.Mathematics;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// One resident GPU visibility buffer (one uint per entity slot) per visibility key. CullFrustum or the occlusion
    /// kernels write it once per camera render; draw lists only read it.
    /// </summary>
    internal sealed class MeshGpuVisibilityCache : IDisposable
    {
        struct Slot
        {
            public BufferHandle buffer;
            public int capacity;
            public bool culled;
        }

        readonly ResourcePool m_ResourcePool;
        readonly Dictionary<MeshVisibilityKey, Slot> m_Slots = new Dictionary<MeshVisibilityKey, Slot>(8);
        readonly List<MeshVisibilityKey> m_Keys = new List<MeshVisibilityKey>(8);
        readonly List<BufferHandle> m_Retired = new List<BufferHandle>(8);
        bool m_Disposed;

        public MeshGpuVisibilityCache(ResourcePool resourcePool)
        {
            m_ResourcePool = resourcePool ?? throw new ArgumentNullException(nameof(resourcePool));
        }

        internal int allocatedViewCount => m_Slots.Count;

        public BufferHandle GetBufferHandle(in MeshView view, int entityCount)
        {
            var key = new MeshVisibilityKey(view, false);
            int needed = math.max(1, entityCount);
            if (!m_Slots.TryGetValue(key, out Slot slot) || slot.capacity < needed)
            {
                if (slot.buffer.buffer != null) m_Retired.Add(slot.buffer);
                slot = new Slot { buffer = m_ResourcePool.GetBuffer(new BufferDescriptor(needed, sizeof(uint)) { name = "MeshVisibility" }), capacity = needed };
                m_Slots[key] = slot;
            }
            return slot.buffer;
        }

        public bool NeedsCull(in MeshView view) => !m_Slots.TryGetValue(new MeshVisibilityKey(view, false), out Slot slot) || !slot.culled;

        public void MarkCulled(in MeshView view)
        {
            var key = new MeshVisibilityKey(view, false);
            if (!m_Slots.TryGetValue(key, out Slot slot)) return;
            slot.culled = true;
            m_Slots[key] = slot;
        }

        public void BeginCamera()
        {
            m_Keys.Clear();
            m_Keys.AddRange(m_Slots.Keys);
            foreach (MeshVisibilityKey key in m_Keys)
            {
                Slot slot = m_Slots[key];
                slot.culled = false;
                m_Slots[key] = slot;
            }
        }

        public void ReleaseView(ulong viewKey)
        {
            m_Keys.Clear();
            foreach (var pair in m_Slots)
                if (pair.Key.viewKey == viewKey) m_Keys.Add(pair.Key);
            foreach (MeshVisibilityKey key in m_Keys)
            {
                m_Retired.Add(m_Slots[key].buffer);
                m_Slots.Remove(key);
            }
        }

        public void FlushRetired()
        {
            foreach (BufferHandle buffer in m_Retired) m_ResourcePool.ReleaseBuffer(buffer);
            m_Retired.Clear();
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            FlushRetired();
            foreach (var pair in m_Slots) m_ResourcePool.ReleaseBuffer(pair.Value.buffer);
            m_Slots.Clear();
        }
    }
}
