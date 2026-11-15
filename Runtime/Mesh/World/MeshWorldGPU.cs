using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// The entity tables a draw list binds, frozen when the draw list is created. The transform version is retained
    /// for the draw list's lifetime; the entity columns are replaced atomically by residency and never written in place
    /// while referenced by recorded work.
    /// </summary>
    internal readonly struct MeshWorldGPUTables
    {
        internal readonly BufferHandle boundsCenterHandle, boundsExtentHandle, flagsHandle, layerMaskHandle, sceneCullingMaskHandle, renderingLayersHandle, lodChainsHandle;
        internal readonly MeshTransformVersion version;
        internal readonly int entityCount;
        internal bool isValid => renderingLayersHandle.buffer != null && version != null;

        internal MeshWorldGPUTables(MeshWorldGPU gpu, int entityCount)
        {
            boundsCenterHandle = gpu.boundsCenterHandle;
            boundsExtentHandle = gpu.boundsExtentHandle;
            flagsHandle = gpu.flagsHandle;
            layerMaskHandle = gpu.layerMaskHandle;
            sceneCullingMaskHandle = gpu.sceneCullingMaskHandle;
            renderingLayersHandle = gpu.renderingLayersHandle;
            lodChainsHandle = gpu.lodChainsHandle;
            version = gpu.transformVersions.current;
            this.entityCount = entityCount;
        }
    }

    /// <summary>
    /// Resident GPU entity columns, uploaded by dirty pages. Transform matrices live in immutable transform versions; the
    /// columns here hold bounds, flags, masks and rendering layers per entity slot.
    /// </summary>
    internal sealed class MeshWorldGPU
    {
        readonly MeshWorld m_World;
        readonly ResourcePool m_Pool;
        readonly ProfilingSampler m_Sampler = new ProfilingSampler("UpdateMeshWorldGPU");
        internal readonly MeshTransformVersionCache transformVersions;
        int m_Capacity;

        internal BufferHandle boundsCenterHandle, boundsExtentHandle, flagsHandle, layerMaskHandle, sceneCullingMaskHandle, renderingLayersHandle, lodChainsHandle;
        internal int capacity => m_Capacity;

        internal MeshWorldGPU(MeshWorld world, ResourcePool pool)
        {
            m_World = world;
            m_Pool = pool;
            transformVersions = new MeshTransformVersionCache(pool, world.meshDrawShader);
        }

        internal MeshWorldGPUTables Snapshot() => new MeshWorldGPUTables(this, m_World.entityHighWater);

        internal void Update()
        {
            if (m_World.isUpdating)
            {
                throw new System.InvalidOperationException("GPU residency cannot observe an open MeshWorldUpdate.");
            }
            using (new ProfilingScope(m_Sampler))
            {
                var runs = new NativeList<int2>(8, Allocator.Temp);
                try
                {
                    if (transformVersions.current == null || transformVersions.current.revision != m_World.transformRevision)
                    {
                        m_World.CollectTransformDirtyRuns(runs);
                        transformVersions.Prepare(m_World, runs);
                    }
                    m_World.ClearTransformDirty();

                    int needed = math.max(64, m_World.entityHighWater);
                    bool recreate = m_Capacity < needed || renderingLayersHandle.buffer == null;
                    if (!recreate && m_World.hasEntityDirty)
                        recreate = !m_Pool.CanWriteBuffer(boundsCenterHandle) || !m_Pool.CanWriteBuffer(boundsExtentHandle) || !m_Pool.CanWriteBuffer(flagsHandle)
                            || !m_Pool.CanWriteBuffer(layerMaskHandle) || !m_Pool.CanWriteBuffer(sceneCullingMaskHandle) || !m_Pool.CanWriteBuffer(renderingLayersHandle)
                            || !m_Pool.CanWriteBuffer(lodChainsHandle);
                    if (recreate)
                    {
                        Recreate(math.max(needed, m_Capacity));
                    }
                    else if (m_World.hasEntityDirty)
                    {
                        m_World.CollectEntityDirtyRuns(runs);
                        foreach (int2 run in runs)
                        {
                            Upload(run.x, run.y);
                        }
                    }
                    m_World.ClearEntityDirty();
                }
                finally
                {
                    runs.Dispose();
                }
            }
        }

        void Recreate(int newCapacity)
        {
            var old = (boundsCenterHandle, boundsExtentHandle, flagsHandle, layerMaskHandle, sceneCullingMaskHandle, renderingLayersHandle, lodChainsHandle);
            BufferHandle centerHandle = default, extent = default, flagBuffer = default, layers = default, scene = default, rendering = default, lods = default;
            try
            {
                centerHandle = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<float4>()) { name = "EntityBoundsCenter" });
                extent = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<float4>()) { name = "EntityBoundsExtent" });
                flagBuffer = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, sizeof(uint)) { name = "EntityFlags" });
                layers = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, sizeof(uint)) { name = "EntityLayerMask" });
                scene = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<uint2>()) { name = "EntitySceneCullingMask" });
                rendering = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, sizeof(uint)) { name = "EntityRenderingLayers" });
                lods = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<MeshLODChain>()) { name = "EntityLodChains" });
                (boundsCenterHandle, boundsExtentHandle, flagsHandle, layerMaskHandle, sceneCullingMaskHandle, renderingLayersHandle, lodChainsHandle) = (centerHandle, extent, flagBuffer, layers, scene, rendering, lods);
                Upload(0, m_World.entityHighWater);
            }
            catch
            {
                (boundsCenterHandle, boundsExtentHandle, flagsHandle, layerMaskHandle, sceneCullingMaskHandle, renderingLayersHandle, lodChainsHandle) = old;
                Release(centerHandle);
                Release(extent);
                Release(flagBuffer);
                Release(layers);
                Release(scene);
                Release(rendering);
                Release(lods);
                throw;
            }
            Release(old.boundsCenterHandle);
            Release(old.boundsExtentHandle);
            Release(old.flagsHandle);
            Release(old.layerMaskHandle);
            Release(old.sceneCullingMaskHandle);
            Release(old.renderingLayersHandle);
            Release(old.lodChainsHandle);
            m_Capacity = newCapacity;
        }

        void Release(BufferHandle bufferHandle)
        {
            if (bufferHandle.buffer != null)
            {
                m_Pool.ReleaseBuffer(bufferHandle);
            }
        }

        void Upload(int begin, int end)
        {
            end = math.min(end, m_World.entityHighWater);
            int count = end - begin;
            if (count <= 0)
            {
                return;
            }
            var centers = new NativeArray<float4>(count, Allocator.Temp);
            var extents = new NativeArray<float4>(count, Allocator.Temp);
            var flagValues = new NativeArray<uint>(count, Allocator.Temp);
            var layerMasks = new NativeArray<uint>(count, Allocator.Temp);
            var sceneMasks = new NativeArray<uint2>(count, Allocator.Temp);
            var renderingMasks = new NativeArray<uint>(count, Allocator.Temp);
            var chains = new NativeArray<MeshLODChain>(count, Allocator.Temp);
            try
            {
                var entities = m_World.entities;
                var bounds = m_World.entityBounds;
                for (int i = begin; i < end; ++i)
                {
                    int local = i - begin;
                    if (!m_World.IsEntitySlotLive(i))
                    {
                        continue;
                    }
                    MeshEntityDesc desc = entities[i].desc;
                    centers[local] = new float4(bounds[i].center, 0);
                    extents[local] = new float4(bounds[i].extents, 0);
                    flagValues[local] = (uint)desc.flags;
                    layerMasks[local] = (uint)desc.layerMask;
                    sceneMasks[local] = new uint2((uint)desc.sceneCullingMask, (uint)(desc.sceneCullingMask >> 32));
                    renderingMasks[local] = desc.renderingLayerMask;
                    chains[local] = entities[i].lod;
                }
                boundsCenterHandle.buffer.SetData(centers, 0, begin, count);
                boundsExtentHandle.buffer.SetData(extents, 0, begin, count);
                flagsHandle.buffer.SetData(flagValues, 0, begin, count);
                layerMaskHandle.buffer.SetData(layerMasks, 0, begin, count);
                sceneCullingMaskHandle.buffer.SetData(sceneMasks, 0, begin, count);
                renderingLayersHandle.buffer.SetData(renderingMasks, 0, begin, count);
                lodChainsHandle.buffer.SetData(chains, 0, begin, count);
                m_World.stats.uploadedBytes += (long)count * (Marshal.SizeOf<float4>() * 2 + sizeof(uint) * 5 + Marshal.SizeOf<MeshLODChain>());
            }
            finally
            {
                centers.Dispose();
                extents.Dispose();
                flagValues.Dispose();
                layerMasks.Dispose();
                sceneMasks.Dispose();
                renderingMasks.Dispose();
                chains.Dispose();
            }
        }

        internal void Dispose()
        {
            transformVersions.Dispose();
            Release(boundsCenterHandle);
            Release(boundsExtentHandle);
            Release(flagsHandle);
            Release(layerMaskHandle);
            Release(sceneCullingMaskHandle);
            Release(renderingLayersHandle);
            Release(lodChainsHandle);
            boundsCenterHandle = boundsExtentHandle = flagsHandle = layerMaskHandle = sceneCullingMaskHandle = renderingLayersHandle = lodChainsHandle = default;
            m_Capacity = 0;
        }
    }
}
