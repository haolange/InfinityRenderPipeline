using System;
using UnityEngine;
using Unity.Mathematics;

namespace InfinityTech.Rendering.GPUResource
{
    public enum ETextureSizingDomain : byte { Exact, Internal, Display }

    // Pixel coordinates remain logical. Only normalized sampling depends on capacity.
    public readonly struct FTextureView
    {
        public readonly int2 size, capacity;
        public FTextureView(int2 size, int2 capacity)
        {
            if (math.any(size <= 0) || math.any(capacity < size))
                throw new ArgumentOutOfRangeException(nameof(capacity));
            this.size = size; this.capacity = capacity;
        }
        public Vector4 shaderValue => new Vector4(size.x, size.y, capacity.x, capacity.y);
        public Vector4 uvScale => new Vector4((float)size.x / capacity.x, (float)size.y / capacity.y, 0, 0);
    }

    public readonly struct FTextureCapacityContext
    {
        public readonly int2 internalCapacity, displayCapacity;
        public FTextureCapacityContext(int2 internalCapacity, int2 displayCapacity)
        { this.internalCapacity = internalCapacity; this.displayCapacity = displayCapacity; }
        public TextureDescriptor Resolve(TextureDescriptor descriptor)
        {
            if (descriptor.sizingDomain == ETextureSizingDomain.Exact) return descriptor;
            int2 capacity = descriptor.sizingDomain == ETextureSizingDomain.Internal ? internalCapacity : displayCapacity;
            if (math.any(capacity <= 0)) throw new InvalidOperationException("A view-relative texture requires an active camera capacity context.");
            int divisor = math.max(1, descriptor.viewDivisor);
            descriptor.allocationWidth = math.max(descriptor.width, (capacity.x + divisor - 1) / divisor);
            descriptor.allocationHeight = math.max(descriptor.height, (capacity.y + divisor - 1) / divisor);
            return descriptor;
        }
    }

    // Requests are speculative until the camera's frame transaction commits.
    public sealed class TextureCapacityState
    {
        readonly int2[] m_Recent = new int2[3];
        int2 m_Capacity, m_LastSize, m_PendingSize, m_PendingCapacity;
        int m_Index, m_SmallFrames, m_PendingSmallFrames;
        bool m_OrientationChanged;
        static int Align(int value) => checked((value + 63) / 64 * 64);
        public int2 Prepare(int2 size)
        {
            if (math.any(size <= 0)) throw new ArgumentOutOfRangeException(nameof(size));
            m_PendingSize = size;
            m_OrientationChanged = math.any(m_LastSize != 0) &&
                (m_LastSize.x > m_LastSize.y) != (size.x > size.y);
            int2 capacity = m_OrientationChanged ? int2.zero : m_Capacity;
            int2 recent = size;
            if (!m_OrientationChanged)
                for (int i = 0; i < 3; ++i) if (i != m_Index) recent = math.max(recent, m_Recent[i]);
            if (math.any(size > capacity))
            {
                for (int axis = 0; axis < 2; ++axis)
                    if (size[axis] > capacity[axis]) capacity[axis] = Align(math.max(size[axis], (capacity[axis] * 9 + 7) / 8));
            }
            m_PendingSmallFrames = (long)recent.x * recent.y * 4 < (long)capacity.x * capacity.y * 3
                ? (m_OrientationChanged ? 1 : m_SmallFrames + 1) : 0;
            if (m_PendingSmallFrames >= 30)
            {
                capacity = new int2(Align(recent.x), Align(recent.y));
                m_PendingSmallFrames = 0;
            }
            m_PendingCapacity = capacity;
            return capacity;
        }
        public void Commit()
        {
            if (m_OrientationChanged) Array.Clear(m_Recent, 0, m_Recent.Length);
            m_Recent[m_Index] = m_PendingSize;
            m_Index = (m_Index + 1) % 3;
            m_Capacity = m_PendingCapacity; m_LastSize = m_PendingSize;
            m_SmallFrames = m_PendingSmallFrames;
        }
    }
}
