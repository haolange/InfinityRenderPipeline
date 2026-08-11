using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// GPU residency for MeshScene transform + bounds tables. Uploads dirty ranges every Update().
    /// Transform / PreviousTransform are transform-indexed.
    /// Bounds + InstanceTransformIndex are instance-indexed so GPU cull can sample per instance
    /// while compact remaps to TransformId.Index for shading.
    /// </summary>
    public class MeshSceneResidency
    {
        private readonly MeshScene m_Scene;
        private readonly ResourcePool m_ResourcePool;
        private readonly ProfilingSampler m_ProfileSampler;

        private FBufferRef m_TransformBuffer;
        private FBufferRef m_PreviousTransformBuffer;
        private FBufferRef m_BoundsCenterBuffer;
        private FBufferRef m_BoundsExtentBuffer;
        private FBufferRef m_InstanceTransformIndexBuffer;
        private int m_TransformBufferCapacity;
        private int m_InstanceBufferCapacity;
        private bool m_HasTransformBuffer;
        private bool m_HasInstanceBuffer;

        public FBufferRef TransformBuffer => m_TransformBuffer;
        public FBufferRef PreviousTransformBuffer => m_PreviousTransformBuffer;
        public FBufferRef BoundsCenterBuffer => m_BoundsCenterBuffer;
        public FBufferRef BoundsExtentBuffer => m_BoundsExtentBuffer;
        public FBufferRef InstanceTransformIndexBuffer => m_InstanceTransformIndexBuffer;
        public int TransformCapacity => m_TransformBufferCapacity;
        public int InstanceCapacity => m_InstanceBufferCapacity;
        /// <summary>Max of transform/instance GPU capacities (legacy accessor).</summary>
        public int Capacity => math.max(m_TransformBufferCapacity, m_InstanceBufferCapacity);
        public MeshScene Scene => m_Scene;

        public MeshSceneResidency(ResourcePool resourcePool, MeshScene scene)
        {
            m_Scene = scene;
            m_ResourcePool = resourcePool;
            m_ProfileSampler = new ProfilingSampler("UpdateMeshSceneResidency");
            m_TransformBufferCapacity = 0;
            m_InstanceBufferCapacity = 0;
            m_HasTransformBuffer = false;
            m_HasInstanceBuffer = false;
        }

        public void Update(in bool block = false)
        {
            if (block || m_Scene == null)
            {
                return;
            }

            using (new ProfilingScope(m_ProfileSampler))
            {
                int neededTransforms = math.max(16, m_Scene.TransformCapacity);
                int neededInstances = math.max(16, m_Scene.InstanceCapacity);
                bool recreateTransforms = !m_HasTransformBuffer || m_TransformBufferCapacity < neededTransforms;
                bool recreateInstances = !m_HasInstanceBuffer || m_InstanceBufferCapacity < neededInstances;

                if (recreateTransforms)
                {
                    if (m_HasTransformBuffer)
                    {
                        m_ResourcePool.ReleaseBuffer(m_TransformBuffer);
                        m_ResourcePool.ReleaseBuffer(m_PreviousTransformBuffer);
                    }

                    m_TransformBufferCapacity = neededTransforms;
                    m_TransformBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_TransformBufferCapacity, Marshal.SizeOf<float4x4>()));
                    m_PreviousTransformBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_TransformBufferCapacity, Marshal.SizeOf<float4x4>()));
                    m_HasTransformBuffer = true;

                    UploadTransformRange(0, m_Scene.TransformHighWater);
                    m_Scene.ClearTransformDirtyRange();
                }

                if (recreateInstances)
                {
                    if (m_HasInstanceBuffer)
                    {
                        m_ResourcePool.ReleaseBuffer(m_BoundsCenterBuffer);
                        m_ResourcePool.ReleaseBuffer(m_BoundsExtentBuffer);
                        m_ResourcePool.ReleaseBuffer(m_InstanceTransformIndexBuffer);
                    }

                    m_InstanceBufferCapacity = neededInstances;
                    m_BoundsCenterBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, Marshal.SizeOf<float4>()));
                    m_BoundsExtentBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, Marshal.SizeOf<float4>()));
                    m_InstanceTransformIndexBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, sizeof(uint)));
                    m_HasInstanceBuffer = true;

                    UploadBoundsFromInstances(fullRebuild: true);
                    m_Scene.ClearBoundsDirtyRange();
                }

                if (recreateTransforms && recreateInstances)
                {
                    return;
                }

                if (!recreateTransforms && m_Scene.HasTransformDirtyRange)
                {
                    int begin = m_Scene.TransformDirtyBegin;
                    int end = m_Scene.TransformDirtyEnd;
                    if (begin <= end)
                    {
                        UploadTransformRange(begin, end + 1);
                    }

                    m_Scene.ClearTransformDirtyRange();
                }

                if (!recreateInstances && m_Scene.HasBoundsDirtyRange)
                {
                    UploadBoundsFromInstances(fullRebuild: false);
                    m_Scene.ClearBoundsDirtyRange();
                }
            }
        }

        private void UploadTransformRange(int begin, int exclusiveEnd)
        {
            begin = math.max(0, begin);
            exclusiveEnd = math.min(exclusiveEnd, m_Scene.TransformHighWater);
            if (exclusiveEnd <= begin)
            {
                return;
            }

            var transforms = m_Scene.GetTransforms();
            int count = exclusiveEnd - begin;
            var currentMatrices = new NativeArray<float4x4>(count, Allocator.Temp);
            var previousMatrices = new NativeArray<float4x4>(count, Allocator.Temp);
            try
            {
                for (int i = begin; i < exclusiveEnd; ++i)
                {
                    TransformRecord transform = transforms[i];
                    currentMatrices[i - begin] = transform.current;
                    previousMatrices[i - begin] = transform.previous;
                }

                m_TransformBuffer.buffer.SetData(currentMatrices, 0, begin, count);
                m_PreviousTransformBuffer.buffer.SetData(previousMatrices, 0, begin, count);
            }
            finally
            {
                currentMatrices.Dispose();
                previousMatrices.Dispose();
            }
        }

        private void UploadBoundsFromInstances(bool fullRebuild)
        {
            int instanceCount = math.max(1, m_Scene.InstanceHighWater);
            int begin = fullRebuild ? 0 : math.max(0, m_Scene.BoundsDirtyBegin);
            int endInclusive = fullRebuild ? instanceCount - 1 : m_Scene.BoundsDirtyEnd;
            endInclusive = math.min(endInclusive, instanceCount - 1);
            if (endInclusive < begin)
            {
                return;
            }

            int count = endInclusive - begin + 1;
            var centers = new NativeArray<float4>(count, Allocator.Temp);
            var extents = new NativeArray<float4>(count, Allocator.Temp);
            var transformIndices = new NativeArray<uint>(count, Allocator.Temp);
            try
            {
                var instances = m_Scene.GetInstances();
                for (int i = begin; i <= endInclusive; ++i)
                {
                    int local = i - begin;
                    if (!m_Scene.IsInstanceSlotLive(i))
                    {
                        centers[local] = float4.zero;
                        extents[local] = float4.zero;
                        transformIndices[local] = 0u;
                        continue;
                    }

                    var instance = instances[i];
                    centers[local] = new float4(instance.worldBounds.center, 0.0f);
                    extents[local] = new float4(instance.worldBounds.extents, 0.0f);
                    transformIndices[local] = instance.transform.IsValid ? instance.transform.Index : 0u;
                }

                m_BoundsCenterBuffer.buffer.SetData(centers, 0, begin, count);
                m_BoundsExtentBuffer.buffer.SetData(extents, 0, begin, count);
                m_InstanceTransformIndexBuffer.buffer.SetData(transformIndices, 0, begin, count);
            }
            finally
            {
                centers.Dispose();
                extents.Dispose();
                transformIndices.Dispose();
            }
        }

        /// <summary>
        /// End-of-frame hook. Keeps persistent buffers alive so dirty-range
        /// uploads remain valid across frames. Use <see cref="Dispose"/> to release GPU memory.
        /// </summary>
        public void Clear()
        {
            // Intentionally retain buffers across frames.
        }

        public void Dispose()
        {
            if (m_HasTransformBuffer)
            {
                m_ResourcePool.ReleaseBuffer(m_TransformBuffer);
                m_ResourcePool.ReleaseBuffer(m_PreviousTransformBuffer);
                m_HasTransformBuffer = false;
                m_TransformBufferCapacity = 0;
            }

            if (m_HasInstanceBuffer)
            {
                m_ResourcePool.ReleaseBuffer(m_BoundsCenterBuffer);
                m_ResourcePool.ReleaseBuffer(m_BoundsExtentBuffer);
                m_ResourcePool.ReleaseBuffer(m_InstanceTransformIndexBuffer);
                m_HasInstanceBuffer = false;
                m_InstanceBufferCapacity = 0;
            }
        }
    }
}
