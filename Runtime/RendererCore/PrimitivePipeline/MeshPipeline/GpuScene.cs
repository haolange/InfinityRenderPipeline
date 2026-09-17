using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Resident GPU tables for MeshScene. Uploads dirty pages every Update().
    /// Current transforms are transform-indexed; previous poses stay on per-camera history.
    /// Bounds + InstanceTransformIndex are instance-indexed so GPU cull can sample per instance
    /// while compact remaps to TransformId.Index for shading.
    /// </summary>
    public class GpuScene
    {
        public const int DirtyPageSize = DirtyPageBitmap.PageSize;

        private readonly MeshScene m_Scene;
        private readonly ResourcePool m_ResourcePool;
        private readonly ProfilingSampler m_ProfileSampler;

        private FBufferRef m_TransformBuffer;
        private FBufferRef m_RenderingLayerBuffer;
        private FBufferRef m_BakedLightingBuffer;
        private FBufferRef m_BoundsCenterBuffer;
        private FBufferRef m_BoundsExtentBuffer;
        private FBufferRef m_InstanceTransformIndexBuffer;
        private FBufferRef m_InstanceFlagsBuffer;
        private FBufferRef m_InstanceLayerMaskBuffer;
        private FBufferRef m_InstanceRenderingLayerBuffer;
        private int m_TransformBufferCapacity;
        private int m_InstanceBufferCapacity;
        private bool m_HasTransformBuffer;
        private bool m_HasInstanceBuffer;

        public FBufferRef TransformBuffer => m_TransformBuffer;
        public FBufferRef RenderingLayerBuffer => m_RenderingLayerBuffer;
        public FBufferRef BakedLightingBuffer => m_BakedLightingBuffer;
        public FBufferRef BoundsCenterBuffer => m_BoundsCenterBuffer;
        public FBufferRef BoundsExtentBuffer => m_BoundsExtentBuffer;
        public FBufferRef InstanceTransformIndexBuffer => m_InstanceTransformIndexBuffer;
        public FBufferRef InstanceFlagsBuffer => m_InstanceFlagsBuffer;
        public FBufferRef InstanceLayerMaskBuffer => m_InstanceLayerMaskBuffer;
        public FBufferRef InstanceRenderingLayerBuffer => m_InstanceRenderingLayerBuffer;
        public int TransformCapacity => m_TransformBufferCapacity;
        public int InstanceCapacity => m_InstanceBufferCapacity;
        public MeshScene Scene => m_Scene;

        public GpuScene(ResourcePool resourcePool, MeshScene scene)
        {
            m_Scene = scene;
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
                        m_ResourcePool.ReleaseBuffer(m_RenderingLayerBuffer);
                        m_ResourcePool.ReleaseBuffer(m_BakedLightingBuffer);
                    }

                    m_TransformBufferCapacity = neededTransforms;
                    m_TransformBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_TransformBufferCapacity, Marshal.SizeOf<float4x4>()));
                    m_RenderingLayerBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_TransformBufferCapacity, sizeof(uint)));
                    m_BakedLightingBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_TransformBufferCapacity, Marshal.SizeOf<FMeshBakedLighting>()));
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
                        m_ResourcePool.ReleaseBuffer(m_InstanceFlagsBuffer);
                        m_ResourcePool.ReleaseBuffer(m_InstanceLayerMaskBuffer);
                        m_ResourcePool.ReleaseBuffer(m_InstanceRenderingLayerBuffer);
                    }

                    m_InstanceBufferCapacity = neededInstances;
                    m_BoundsCenterBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, Marshal.SizeOf<float4>()));
                    m_BoundsExtentBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, Marshal.SizeOf<float4>()));
                    m_InstanceTransformIndexBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, sizeof(uint)));
                    m_InstanceFlagsBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, sizeof(uint)));
                    m_InstanceLayerMaskBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, sizeof(uint)));
                    m_InstanceRenderingLayerBuffer = m_ResourcePool.GetBuffer(new BufferDescriptor(m_InstanceBufferCapacity, sizeof(uint)));
                    m_HasInstanceBuffer = true;

                    UploadBoundsRange(0, m_Scene.InstanceHighWater);
                    m_Scene.ClearBoundsDirtyRange();
                }

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
            var layers = new NativeArray<uint>(count, Allocator.Temp);
            var baked = new NativeArray<FMeshBakedLighting>(count, Allocator.Temp);
            try
            {
                for (int i = begin; i < exclusiveEnd; ++i)
                {
                    TransformRecord transform = transforms[i];
                    currentMatrices[i - begin] = transform.current;
                    layers[i - begin] = m_Scene.GetTransformRenderingLayer(i);
                    baked[i - begin] = m_Scene.GetTransformBakedLighting(i);
                }

                m_TransformBuffer.buffer.SetData(currentMatrices, 0, begin, count);
                m_RenderingLayerBuffer.buffer.SetData(layers, 0, begin, count);
                m_BakedLightingBuffer.buffer.SetData(baked, 0, begin, count);
                AccountUpload(count, Marshal.SizeOf<float4x4>() + sizeof(uint) + Marshal.SizeOf<FMeshBakedLighting>());
            }
            finally
            {
                currentMatrices.Dispose();
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
            var centers = new NativeArray<float4>(count, Allocator.Temp);
            var extents = new NativeArray<float4>(count, Allocator.Temp);
            var transformIndices = new NativeArray<uint>(count, Allocator.Temp);
            var flags = new NativeArray<uint>(count, Allocator.Temp);
            var layerMasks = new NativeArray<uint>(count, Allocator.Temp);
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
                        renderingLayers[local] = 0u;
                        continue;
                    }

                    var instance = instances[i];
                    centers[local] = new float4(instance.worldBounds.center, 0.0f);
                    extents[local] = new float4(instance.worldBounds.extents, 0.0f);
                    transformIndices[local] = instance.transform.IsValid ? instance.transform.Index : 0u;
                    flags[local] = (uint)instance.flags;
                    layerMasks[local] = (uint)instance.layerMask;
                    renderingLayers[local] = instance.renderingLayerMask;
                }

                m_BoundsCenterBuffer.buffer.SetData(centers, 0, begin, count);
                m_BoundsExtentBuffer.buffer.SetData(extents, 0, begin, count);
                m_InstanceTransformIndexBuffer.buffer.SetData(transformIndices, 0, begin, count);
                m_InstanceFlagsBuffer.buffer.SetData(flags, 0, begin, count);
                m_InstanceLayerMaskBuffer.buffer.SetData(layerMasks, 0, begin, count);
                m_InstanceRenderingLayerBuffer.buffer.SetData(renderingLayers, 0, begin, count);
                AccountUpload(count, Marshal.SizeOf<float4>() * 2 + sizeof(uint) * 4);
            }
            finally
            {
                centers.Dispose();
                extents.Dispose();
                transformIndices.Dispose();
                flags.Dispose();
                layerMasks.Dispose();
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
            if (m_HasTransformBuffer)
            {
                m_ResourcePool.ReleaseBuffer(m_TransformBuffer);
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
                m_ResourcePool.ReleaseBuffer(m_InstanceRenderingLayerBuffer);
                m_HasInstanceBuffer = false;
                m_InstanceBufferCapacity = 0;
            }
        }
    }
}
