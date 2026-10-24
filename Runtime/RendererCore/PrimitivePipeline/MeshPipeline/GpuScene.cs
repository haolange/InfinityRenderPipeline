using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Resident GPU tables for MeshScene. Uploads dirty pages every Update().
    /// Immutable transform versions are scene-owned; views retain accepted version references.
    /// Bounds + InstanceTransformIndex are instance-indexed so GPU cull can sample per instance
    /// while compact remaps to TransformId.Index for shading.
    /// </summary>
    public class GpuScene
    {
        public const int DirtyPageSize = DirtyPageBitmap.PageSize;

        private readonly MeshScene m_Scene;
        private readonly ResourcePool m_ResourcePool;
        private readonly ProfilingSampler m_ProfileSampler;

        internal readonly MeshTransformVersionCache transformVersions;
        private FBufferRef m_RenderingLayerBuffer;
        private FBufferRef m_BakedLightingBuffer;
        private FBufferRef m_BoundsCenterBuffer;
        private FBufferRef m_BoundsExtentBuffer;
        private FBufferRef m_InstanceTransformIndexBuffer;
        private FBufferRef m_InstanceFlagsBuffer;
        private FBufferRef m_InstanceLayerMaskBuffer;
        private FBufferRef m_InstanceSceneCullingMaskBuffer;
        private FBufferRef m_InstanceRenderingLayerBuffer;
        private int m_TransformBufferCapacity;
        private int m_InstanceBufferCapacity;
        private bool m_HasTransformBuffer;
        private bool m_HasInstanceBuffer;

        public FBufferRef TransformBuffer => transformVersions.current != null ? transformVersions.current.matrices : default;
        public FBufferRef RenderingLayerBuffer => m_RenderingLayerBuffer;
        public FBufferRef BakedLightingBuffer => m_BakedLightingBuffer;
        public FBufferRef BoundsCenterBuffer => m_BoundsCenterBuffer;
        public FBufferRef BoundsExtentBuffer => m_BoundsExtentBuffer;
        public FBufferRef InstanceTransformIndexBuffer => m_InstanceTransformIndexBuffer;
        public FBufferRef InstanceFlagsBuffer => m_InstanceFlagsBuffer;
        public FBufferRef InstanceLayerMaskBuffer => m_InstanceLayerMaskBuffer;
        public FBufferRef InstanceSceneCullingMaskBuffer => m_InstanceSceneCullingMaskBuffer;
        public FBufferRef InstanceRenderingLayerBuffer => m_InstanceRenderingLayerBuffer;
        public int TransformCapacity => m_TransformBufferCapacity;
        public int InstanceCapacity => m_InstanceBufferCapacity;
        public MeshScene Scene => m_Scene;

        public GpuScene(ResourcePool resourcePool, MeshScene scene)
        {
            m_Scene = scene;
            transformVersions = new MeshTransformVersionCache(resourcePool);
            m_ResourcePool = resourcePool;
            m_ProfileSampler = new ProfilingSampler("UpdateGpuScene");
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

            if (m_Scene.isUpdating)
                throw new System.InvalidOperationException("GPU residency cannot observe an uncommitted MeshScene transaction.");

            using (new ProfilingScope(m_ProfileSampler))
            {
                int neededTransforms = math.max(math.max(16, m_Scene.TransformCapacity), m_TransformBufferCapacity);
                int neededInstances = math.max(math.max(16, m_Scene.InstanceCapacity), m_InstanceBufferCapacity);
                bool recreateTransforms = !m_HasTransformBuffer || m_TransformBufferCapacity < neededTransforms;
                bool recreateInstances = !m_HasInstanceBuffer || m_InstanceBufferCapacity < neededInstances;
                if (m_HasTransformBuffer && m_Scene.HasTransformDirtyRange)
                    recreateTransforms |= !m_ResourcePool.CanWriteBuffer(m_RenderingLayerBuffer) || !m_ResourcePool.CanWriteBuffer(m_BakedLightingBuffer);
                if (m_HasInstanceBuffer && m_Scene.HasBoundsDirtyRange)
                    recreateInstances |= !m_ResourcePool.CanWriteBuffer(m_BoundsCenterBuffer) || !m_ResourcePool.CanWriteBuffer(m_BoundsExtentBuffer)
                        || !m_ResourcePool.CanWriteBuffer(m_InstanceTransformIndexBuffer) || !m_ResourcePool.CanWriteBuffer(m_InstanceFlagsBuffer)
                        || !m_ResourcePool.CanWriteBuffer(m_InstanceLayerMaskBuffer) || !m_ResourcePool.CanWriteBuffer(m_InstanceSceneCullingMaskBuffer)
                        || !m_ResourcePool.CanWriteBuffer(m_InstanceRenderingLayerBuffer);


                if (transformVersions.current == null || transformVersions.current.revision != m_Scene.TransformRevision)
                {
                    var transformRuns = new NativeList<int2>(8, Allocator.Temp);
                    try
                    {
                        m_Scene.CollectTransformDirtyRuns(transformRuns);
                        transformVersions.Prepare(m_Scene, transformRuns);
                    }
                    finally { transformRuns.Dispose(); }
                }

                if (recreateTransforms) RecreateTransformAttributes(neededTransforms);
                if (recreateInstances) RecreateInstanceAttributes(neededInstances);

                if (recreateTransforms && recreateInstances)
                {
                    return;
                }

                var runs = new NativeList<int2>(8, Allocator.Temp);
                try
                {
                    if (!recreateTransforms && m_Scene.HasTransformDirtyRange)
                    {
                        m_Scene.CollectTransformDirtyRuns(runs);
                        for (int i = 0; i < runs.Length; ++i)
                        {
                            UploadTransformRange(runs[i].x, runs[i].y);
                        }

                        m_Scene.ClearTransformDirtyRange();
                    }

                    if (!recreateInstances && m_Scene.HasBoundsDirtyRange)
                    {
                        runs.Clear();
                        m_Scene.CollectBoundsDirtyRuns(runs);
                        for (int i = 0; i < runs.Length; ++i)
                        {
                            UploadBoundsRange(runs[i].x, runs[i].y);
                        }

                        m_Scene.ClearBoundsDirtyRange();
                    }
                }
                finally
                {
                    runs.Dispose();
                }
            }
        }

        void ReturnIfCreated(FBufferRef buffer)
        {
            if (buffer.buffer != null) m_ResourcePool.ReleaseBuffer(buffer);
        }
        void RecreateTransformAttributes(int capacity)
        {
            var old = (m_RenderingLayerBuffer, m_BakedLightingBuffer);
            FBufferRef layers = default, baked = default;
            try
            {
                layers = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, sizeof(uint)));
                baked = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, Marshal.SizeOf<FMeshBakedLighting>()));
                m_RenderingLayerBuffer = layers; m_BakedLightingBuffer = baked;
                UploadTransformRange(0, m_Scene.TransformHighWater);
            }
            catch
            {
                (m_RenderingLayerBuffer, m_BakedLightingBuffer) = old;
                ReturnIfCreated(layers); ReturnIfCreated(baked); throw;
            }
            if (m_HasTransformBuffer) { ReturnIfCreated(old.Item1); ReturnIfCreated(old.Item2); }
            m_TransformBufferCapacity = capacity; m_HasTransformBuffer = true;
            m_Scene.ClearTransformDirtyRange();
        }
        void RecreateInstanceAttributes(int capacity)
        {
            var old = (m_BoundsCenterBuffer, m_BoundsExtentBuffer, m_InstanceTransformIndexBuffer, m_InstanceFlagsBuffer,
                m_InstanceLayerMaskBuffer, m_InstanceSceneCullingMaskBuffer, m_InstanceRenderingLayerBuffer);
            FBufferRef center = default, extent = default, transforms = default, flags = default, layers = default, masks = default, rendering = default;
            try
            {
                center = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, Marshal.SizeOf<float4>()));
                extent = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, Marshal.SizeOf<float4>()));
                transforms = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, sizeof(uint)));
                flags = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, sizeof(uint)));
                layers = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, sizeof(uint)));
                masks = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, Marshal.SizeOf<uint2>()));
                rendering = m_ResourcePool.GetBuffer(new BufferDescriptor(capacity, sizeof(uint)));
                (m_BoundsCenterBuffer, m_BoundsExtentBuffer, m_InstanceTransformIndexBuffer, m_InstanceFlagsBuffer,
                    m_InstanceLayerMaskBuffer, m_InstanceSceneCullingMaskBuffer, m_InstanceRenderingLayerBuffer) = (center, extent, transforms, flags, layers, masks, rendering);
                UploadBoundsRange(0, m_Scene.InstanceHighWater);
            }
            catch
            {
                (m_BoundsCenterBuffer, m_BoundsExtentBuffer, m_InstanceTransformIndexBuffer, m_InstanceFlagsBuffer,
                    m_InstanceLayerMaskBuffer, m_InstanceSceneCullingMaskBuffer, m_InstanceRenderingLayerBuffer) = old;
                ReturnIfCreated(center); ReturnIfCreated(extent); ReturnIfCreated(transforms); ReturnIfCreated(flags);
                ReturnIfCreated(layers); ReturnIfCreated(masks); ReturnIfCreated(rendering); throw;
            }
            if (m_HasInstanceBuffer)
            {
                ReturnIfCreated(old.Item1); ReturnIfCreated(old.Item2); ReturnIfCreated(old.Item3); ReturnIfCreated(old.Item4);
                ReturnIfCreated(old.Item5); ReturnIfCreated(old.Item6); ReturnIfCreated(old.Item7);
            }
            m_InstanceBufferCapacity = capacity; m_HasInstanceBuffer = true;
            m_Scene.ClearBoundsDirtyRange();
        }

        private void UploadTransformRange(int begin, int exclusiveEnd)
        {
            begin = math.max(0, begin);
            exclusiveEnd = math.min(exclusiveEnd, m_Scene.TransformHighWater);
            if (exclusiveEnd <= begin)
            {
                return;
            }

            int count = exclusiveEnd - begin;
            if (!m_ResourcePool.CanWriteBuffer(m_RenderingLayerBuffer) || !m_ResourcePool.CanWriteBuffer(m_BakedLightingBuffer))
                throw new System.InvalidOperationException("Transform attributes are still referenced by recorded or pending GPU work.");
            var layers = new NativeArray<uint>(count, Allocator.Temp);
            var baked = new NativeArray<FMeshBakedLighting>(count, Allocator.Temp);
            try
            {
                for (int i = begin; i < exclusiveEnd; ++i)
                {
                    layers[i - begin] = m_Scene.GetTransformRenderingLayer(i);
                    baked[i - begin] = m_Scene.GetTransformBakedLighting(i);
                }

                m_RenderingLayerBuffer.buffer.SetData(layers, 0, begin, count);
                m_BakedLightingBuffer.buffer.SetData(baked, 0, begin, count);
                AccountUpload(count, sizeof(uint) + Marshal.SizeOf<FMeshBakedLighting>());
            }
            finally
            {
                layers.Dispose();
                baked.Dispose();
            }
        }

        private void UploadBoundsRange(int begin, int exclusiveEnd)
        {
            begin = math.max(0, begin);
            exclusiveEnd = math.min(exclusiveEnd, m_Scene.InstanceHighWater);
            if (exclusiveEnd <= begin)
            {
                return;
            }

            int count = exclusiveEnd - begin;
            if (!m_ResourcePool.CanWriteBuffer(m_BoundsCenterBuffer) || !m_ResourcePool.CanWriteBuffer(m_BoundsExtentBuffer)
                || !m_ResourcePool.CanWriteBuffer(m_InstanceTransformIndexBuffer) || !m_ResourcePool.CanWriteBuffer(m_InstanceFlagsBuffer)
                || !m_ResourcePool.CanWriteBuffer(m_InstanceLayerMaskBuffer) || !m_ResourcePool.CanWriteBuffer(m_InstanceSceneCullingMaskBuffer)
                || !m_ResourcePool.CanWriteBuffer(m_InstanceRenderingLayerBuffer))
                throw new System.InvalidOperationException("Instance attributes are still referenced by recorded or pending GPU work.");
            var centers = new NativeArray<float4>(count, Allocator.Temp);
            var extents = new NativeArray<float4>(count, Allocator.Temp);
            var transformIndices = new NativeArray<uint>(count, Allocator.Temp);
            var flags = new NativeArray<uint>(count, Allocator.Temp);
            var layerMasks = new NativeArray<uint>(count, Allocator.Temp);
            var sceneMasks = new NativeArray<uint2>(count, Allocator.Temp);
            var renderingLayers = new NativeArray<uint>(count, Allocator.Temp);
            try
            {
                var instances = m_Scene.GetInstances();
                for (int i = begin; i < exclusiveEnd; ++i)
                {
                    int local = i - begin;
                    if (!m_Scene.IsInstanceSlotLive(i))
                    {
                        centers[local] = float4.zero;
                        extents[local] = float4.zero;
                        transformIndices[local] = 0u;
                        flags[local] = 0u;
                        layerMasks[local] = 0u;
                        sceneMasks[local] = uint2.zero;
                        renderingLayers[local] = 0u;
                        continue;
                    }

                    var instance = instances[i];
                    centers[local] = new float4(instance.worldBounds.center, 0.0f);
                    extents[local] = new float4(instance.worldBounds.extents, 0.0f);
                    transformIndices[local] = instance.transform.IsValid ? instance.transform.Index : 0u;
                    flags[local] = (uint)instance.flags;
                    layerMasks[local] = (uint)instance.layerMask;
                    sceneMasks[local] = new uint2((uint)instance.sceneCullingMask, (uint)(instance.sceneCullingMask >> 32));
                    renderingLayers[local] = instance.renderingLayerMask;
                }

                m_BoundsCenterBuffer.buffer.SetData(centers, 0, begin, count);
                m_BoundsExtentBuffer.buffer.SetData(extents, 0, begin, count);
                m_InstanceTransformIndexBuffer.buffer.SetData(transformIndices, 0, begin, count);
                m_InstanceFlagsBuffer.buffer.SetData(flags, 0, begin, count);
                m_InstanceLayerMaskBuffer.buffer.SetData(layerMasks, 0, begin, count);
                m_InstanceSceneCullingMaskBuffer.buffer.SetData(sceneMasks, 0, begin, count);
                m_InstanceRenderingLayerBuffer.buffer.SetData(renderingLayers, 0, begin, count);
                AccountUpload(count, Marshal.SizeOf<float4>() * 2 + sizeof(uint) * 6);
            }
            finally
            {
                centers.Dispose();
                extents.Dispose();
                transformIndices.Dispose();
                flags.Dispose();
                layerMasks.Dispose();
                sceneMasks.Dispose();
                renderingLayers.Dispose();
            }
        }

        static void AccountUpload(int slots, int bytesPerSlot)
        {
            MeshPipelineDiagnostics.GpuSceneUploadedSlots += slots;
            MeshPipelineDiagnostics.GpuSceneUploadedBytes += slots * bytesPerSlot;
        }

        /// <summary>
        /// End-of-frame hook. Keeps persistent buffers alive so dirty-page
        /// uploads remain valid across frames. Use <see cref="Dispose"/> to release GPU memory.
        /// </summary>
        public void Clear()
        {
            // Intentionally retain buffers across frames.
        }

        public void Dispose()
        {
            transformVersions.Dispose();
            if (m_HasTransformBuffer)
            {
                m_ResourcePool.ReleaseBuffer(m_RenderingLayerBuffer);
                m_ResourcePool.ReleaseBuffer(m_BakedLightingBuffer);
                m_HasTransformBuffer = false;
                m_TransformBufferCapacity = 0;
            }

            if (m_HasInstanceBuffer)
            {
                m_ResourcePool.ReleaseBuffer(m_BoundsCenterBuffer);
                m_ResourcePool.ReleaseBuffer(m_BoundsExtentBuffer);
                m_ResourcePool.ReleaseBuffer(m_InstanceTransformIndexBuffer);
                m_ResourcePool.ReleaseBuffer(m_InstanceFlagsBuffer);
                m_ResourcePool.ReleaseBuffer(m_InstanceLayerMaskBuffer);
                m_ResourcePool.ReleaseBuffer(m_InstanceSceneCullingMaskBuffer);
                m_ResourcePool.ReleaseBuffer(m_InstanceRenderingLayerBuffer);
                m_HasInstanceBuffer = false;
                m_InstanceBufferCapacity = 0;
            }
        }
    }
}
