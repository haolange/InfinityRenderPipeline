using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// One GPU visibility buffer per live view. CullFrustum writes it once; DrawLists only bind it.
    /// </summary>
    internal sealed class MeshGpuVisibilityCache : IDisposable
    {
        private struct Slot
        {
            public FBufferRef buffer;
            public int capacity;
            public bool culled;
            public bool hasBuffer;
        }

        private readonly ResourcePool m_ResourcePool;
        private readonly Dictionary<ulong, Slot> m_Slots = new Dictionary<ulong, Slot>(8);
        private readonly List<FBufferRef> m_Retired = new List<FBufferRef>(8);
        private bool m_Disposed;

        public MeshGpuVisibilityCache(ResourcePool resourcePool)
        {
            m_ResourcePool = resourcePool ?? throw new ArgumentNullException(nameof(resourcePool));
        }

        public static ulong MakeKey(in MeshView view)
        {
            unchecked
            {
                ulong key = view.viewKey;
                key ^= (ulong)(uint)view.subviewIndex << 1;
                key ^= (ulong)(uint)view.PolicyId << 32;
                key ^= (ulong)(uint)view.layerMask << 8;
                key ^= (ulong)view.renderingLayerMask << 40;
                key ^= view.FilterRenderingLayers ? 1ul << 63 : 0ul;
                return key;
            }
        }

        public ComputeBuffer GetBuffer(in MeshView view, int instanceCount)
        {
            ulong key = MakeKey(view);
            int needed = math.max(1, instanceCount);
            if (!m_Slots.TryGetValue(key, out Slot slot) || !slot.hasBuffer || slot.capacity < needed)
            {
                if (slot.hasBuffer)
                {
                    m_Retired.Add(slot.buffer);
                }

                slot = new Slot
                {
                    buffer = m_ResourcePool.GetBuffer(new BufferDescriptor(needed, sizeof(uint))),
                    capacity = needed,
                    culled = false,
                    hasBuffer = true
                };
                m_Slots[key] = slot;
            }

            return slot.buffer.buffer;
        }

        public bool NeedsCull(in MeshView view)
        {
            return !m_Slots.TryGetValue(MakeKey(view), out Slot slot) || !slot.culled;
        }

        public void MarkCulled(in MeshView view)
        {
            ulong key = MakeKey(view);
            if (!m_Slots.TryGetValue(key, out Slot slot))
            {
                return;
            }

            slot.culled = true;
            m_Slots[key] = slot;
        }

        public void BeginCamera()
        {
            List<ulong> keys = new List<ulong>(m_Slots.Keys);
            for (int i = 0; i < keys.Count; ++i)
            {
                Slot slot = m_Slots[keys[i]];
                slot.culled = false;
                m_Slots[keys[i]] = slot;
            }
        }

        public void FlushRetired()
        {
            for (int i = 0; i < m_Retired.Count; ++i)
            {
                m_ResourcePool.ReleaseBuffer(m_Retired[i]);
            }

            m_Retired.Clear();
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            FlushRetired();
            foreach (KeyValuePair<ulong, Slot> pair in m_Slots)
            {
                if (pair.Value.hasBuffer)
                {
                    m_ResourcePool.ReleaseBuffer(pair.Value.buffer);
                }
            }

            m_Slots.Clear();
        }
    }
}
