using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>One frustum view inside the camera's batched visibility buffer.</summary>
    // GPU-RECORD: MeshFrustumView Shaders/Mesh/Compute_MeshDrawPipeline.compute
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshFrustumView
    {
        public float4 plane0;
        public float4 plane1;
        public float4 plane2;
        public float4 plane3;
        public float4 plane4;
        public float4 plane5;
        public uint planeCount;
        public uint layerMask;
        public uint sceneLow;
        public uint sceneHigh;
        public uint renderingLayerMask;
        public uint filterRenderingLayers;
        public uint lodOrthographic;
        public uint reserved0;
        public float lodScale;
        public float lodBias;
        public float lodOriginX;
        public float lodOriginY;
        public float lodOriginZ;
        public uint reserved1;
        public uint reserved2;
        public uint reserved3;

        public static MeshFrustumView FromView(in MeshView view)
        {
            return new MeshFrustumView
            {
                plane0 = PackPlane(view, 0), plane1 = PackPlane(view, 1), plane2 = PackPlane(view, 2),
                plane3 = PackPlane(view, 3), plane4 = PackPlane(view, 4), plane5 = PackPlane(view, 5),
                planeCount = (uint)math.clamp(view.planeCount, 0, MeshView.MaxPlanes),
                layerMask = unchecked((uint)view.layerMask),
                sceneLow = unchecked((uint)view.sceneCullingMask),
                sceneHigh = unchecked((uint)(view.sceneCullingMask >> 32)),
                renderingLayerMask = view.renderingLayerMask,
                filterRenderingLayers = view.FilterRenderingLayers ? 1u : 0u,
                lodOrthographic = view.lodProjection.orthographic ? 1u : 0u,
                lodScale = view.lodProjection.scale,
                lodBias = view.lodProjection.bias,
                lodOriginX = view.lodProjection.origin.x,
                lodOriginY = view.lodProjection.origin.y,
                lodOriginZ = view.lodProjection.origin.z
            };
        }

        static float4 PackPlane(in MeshView view, int index)
        {
            Plane source = index < view.planeCount ? view.GetPlane(index) : default;
            return new float4(source.normal.x, source.normal.y, source.normal.z, source.distance);
        }
    }

    /// <summary>
    /// Occlusion keeps one resident buffer per visibility key. Frustum culling of one camera render shares one buffer,
    /// sliced by view, which grows only when the slice count or the entity count exceeds its capacity.
    /// </summary>
    internal sealed class MeshGPUVisibilityCache : IDisposable
    {
        struct Slot
        {
            public BufferHandle buffer;
            public int capacity;
        }

        readonly ResourcePool m_ResourcePool;
        readonly Dictionary<MeshVisibilityKey, Slot> m_Slots = new Dictionary<MeshVisibilityKey, Slot>(8);
        readonly List<MeshVisibilityKey> m_Keys = new List<MeshVisibilityKey>(8);
        readonly List<BufferHandle> m_Retired = new List<BufferHandle>(8);
        BufferHandle m_FrustumBatch;
        BufferHandle m_FrustumViews;
        int m_FrustumStride, m_FrustumSliceCapacity;
        bool m_Disposed;

        public MeshGPUVisibilityCache(ResourcePool resourcePool)
        {
            m_ResourcePool = resourcePool ?? throw new ArgumentNullException(nameof(resourcePool));
        }

        internal int allocatedViewCount => m_Slots.Count;
        internal int frustumStride => m_FrustumStride;
        internal BufferHandle frustumBatch => m_FrustumBatch;
        internal BufferHandle frustumViews => m_FrustumViews;

        /// <summary>One buffer for every frustum slice. The returned stride is the pitch of one slice and does not shrink.</summary>
        public BufferHandle EnsureFrustumBatch(int entityCount, int sliceCount, out int stride)
        {
            int entities = math.max(1, entityCount);
            int slices = math.max(1, sliceCount);
            if (m_FrustumBatch.buffer == null || m_FrustumStride < entities || m_FrustumSliceCapacity < slices)
            {
                if (m_FrustumBatch.buffer != null)
                {
                    m_Retired.Add(m_FrustumBatch);
                }
                m_FrustumStride = math.max(m_FrustumStride, entities);
                m_FrustumSliceCapacity = math.max(m_FrustumSliceCapacity, slices);
                m_FrustumBatch = m_ResourcePool.GetBuffer(new BufferDescriptor(m_FrustumStride * m_FrustumSliceCapacity, sizeof(uint)) { name = "MeshFrustumVisibility" });
            }
            int viewStride = Marshal.SizeOf<MeshFrustumView>();
            if (m_FrustumViews.buffer == null || m_FrustumViews.buffer.count < m_FrustumSliceCapacity)
            {
                if (m_FrustumViews.buffer != null)
                {
                    m_Retired.Add(m_FrustumViews);
                }
                m_FrustumViews = m_ResourcePool.GetBuffer(new BufferDescriptor(m_FrustumSliceCapacity, viewStride) { name = "MeshFrustumViews" });
            }
            stride = m_FrustumStride;
            return m_FrustumBatch;
        }

        public BufferHandle GetBufferHandle(in MeshView view, int entityCount)
        {
            var key = new MeshVisibilityKey(view, false, residentIdentity: true);
            int needed = math.max(1, entityCount);
            if (!m_Slots.TryGetValue(key, out Slot slot) || slot.capacity < needed)
            {
                if (slot.buffer.buffer != null)
                {
                    m_Retired.Add(slot.buffer);
                }
                slot = new Slot { buffer = m_ResourcePool.GetBuffer(new BufferDescriptor(needed, sizeof(uint)) { name = "MeshVisibility" }), capacity = needed };
                m_Slots[key] = slot;
            }
            return slot.buffer;
        }

        public void ReleaseView(ulong viewKey)
        {
            m_Keys.Clear();
            foreach (var pair in m_Slots)
                if (pair.Key.viewKey == viewKey)
                {
                    m_Keys.Add(pair.Key);
                }
            foreach (MeshVisibilityKey key in m_Keys)
            {
                m_Retired.Add(m_Slots[key].buffer);
                m_Slots.Remove(key);
            }
        }

        public void FlushRetired()
        {
            foreach (BufferHandle buffer in m_Retired)
            {
                m_ResourcePool.ReleaseBuffer(buffer);
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
            foreach (var pair in m_Slots)
            {
                m_ResourcePool.ReleaseBuffer(pair.Value.buffer);
            }
            m_Slots.Clear();
            if (m_FrustumBatch.buffer != null)
            {
                m_ResourcePool.ReleaseBuffer(m_FrustumBatch);
            }
            if (m_FrustumViews.buffer != null)
            {
                m_ResourcePool.ReleaseBuffer(m_FrustumViews);
            }
            m_FrustumBatch = default;
            m_FrustumViews = default;
        }
    }
}
