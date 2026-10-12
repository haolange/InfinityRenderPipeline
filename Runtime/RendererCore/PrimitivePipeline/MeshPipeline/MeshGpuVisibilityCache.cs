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
    // Structured identity of one GPU-culled view. Full field equality is the key; the hash only accelerates lookup.
    public readonly struct MeshGpuVisibilityKey : IEquatable<MeshGpuVisibilityKey>
    {
        public readonly ulong viewKey;
        public readonly int subviewIndex;
        public readonly int policyId;
        public readonly int layerMask;
        public readonly uint renderingLayerMask;
        public readonly bool filterRenderingLayers;

        public MeshGpuVisibilityKey(in MeshView view)
        {
            viewKey = view.viewKey;
            subviewIndex = view.subviewIndex;
            policyId = view.PolicyId;
            layerMask = view.layerMask;
            renderingLayerMask = view.renderingLayerMask;
            filterRenderingLayers = view.FilterRenderingLayers;
        }

        public bool Equals(MeshGpuVisibilityKey other)
        {
            return viewKey == other.viewKey && subviewIndex == other.subviewIndex && policyId == other.policyId
                && layerMask == other.layerMask && renderingLayerMask == other.renderingLayerMask
                && filterRenderingLayers == other.filterRenderingLayers;
        }

        public override bool Equals(object obj) => obj is MeshGpuVisibilityKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = viewKey.GetHashCode();
                hash = hash * 31 + subviewIndex;
                hash = hash * 31 + policyId;
                hash = hash * 31 + layerMask;
                hash = hash * 31 + (int)renderingLayerMask;
                hash = hash * 31 + (filterRenderingLayers ? 1 : 0);
                return hash;
            }
        }
    }

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
        private readonly Dictionary<MeshGpuVisibilityKey, Slot> m_Slots = new Dictionary<MeshGpuVisibilityKey, Slot>(8);
        private readonly List<FBufferRef> m_Retired = new List<FBufferRef>(8);
        private bool m_Disposed;

        public MeshGpuVisibilityCache(ResourcePool resourcePool)
        {
            m_ResourcePool = resourcePool ?? throw new ArgumentNullException(nameof(resourcePool));
        }

        public static MeshGpuVisibilityKey MakeKey(in MeshView view)
        {
            return new MeshGpuVisibilityKey(view);
        }

        public FBufferRef GetBufferRef(in MeshView view, int instanceCount)
        {
            MeshGpuVisibilityKey key = MakeKey(view);
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

            return slot.buffer;
        }

        public ComputeBuffer GetBuffer(in MeshView view, int instanceCount)
        {
            return GetBufferRef(view, instanceCount).buffer;
        }

        public bool NeedsCull(in MeshView view)
        {
            return !m_Slots.TryGetValue(MakeKey(view), out Slot slot) || !slot.culled;
        }

        public void MarkCulled(in MeshView view)
        {
            MeshGpuVisibilityKey key = MakeKey(view);
            if (!m_Slots.TryGetValue(key, out Slot slot))
            {
                return;
            }

            slot.culled = true;
            m_Slots[key] = slot;
        }

        public void BeginCamera()
        {
            List<MeshGpuVisibilityKey> keys = new List<MeshGpuVisibilityKey>(m_Slots.Keys);
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
            foreach (KeyValuePair<MeshGpuVisibilityKey, Slot> pair in m_Slots)
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
