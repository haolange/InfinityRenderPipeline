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
        internal readonly BufferHandle boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers, lodChains;
        internal readonly MeshTransformVersion version;
        internal readonly int entityCount;
        internal bool isValid => renderingLayers.buffer != null && version != null;

        internal MeshWorldGPUTables(MeshWorldGPU gpu, int entityCount)
        {
            boundsCenter = gpu.boundsCenter; boundsExtent = gpu.boundsExtent; flags = gpu.flags;
            layerMask = gpu.layerMask; sceneCullingMask = gpu.sceneCullingMask; renderingLayers = gpu.renderingLayers; lodChains = gpu.lodChains;
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

        internal BufferHandle boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers, lodChains;
        internal int capacity => m_Capacity;

        internal MeshWorldGPU(MeshWorld world, ResourcePool pool)
        {
            m_World = world;
            m_Pool = pool;
            transformVersions = new MeshTransformVersionCache(pool);
        }

        internal MeshWorldGPUTables Snapshot() => new MeshWorldGPUTables(this, m_World.entityHighWater);

        internal void Update()
        {
            if (m_World.isUpdating) throw new System.InvalidOperationException("GPU residency cannot observe an open MeshWorldUpdate.");
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
                    bool recreate = m_Capacity < needed || renderingLayers.buffer == null;
                    if (!recreate && m_World.hasEntityDirty)
                        recreate = !m_Pool.CanWriteBuffer(boundsCenter) || !m_Pool.CanWriteBuffer(boundsExtent) || !m_Pool.CanWriteBuffer(flags)
                            || !m_Pool.CanWriteBuffer(layerMask) || !m_Pool.CanWriteBuffer(sceneCullingMask) || !m_Pool.CanWriteBuffer(renderingLayers)
                            || !m_Pool.CanWriteBuffer(lodChains);
                    if (recreate)
                    {
                        Recreate(math.max(needed, m_Capacity));
                    }
                    else if (m_World.hasEntityDirty)
                    {
                        m_World.CollectEntityDirtyRuns(runs);
                        foreach (int2 run in runs) Upload(run.x, run.y);
                    }
                    m_World.ClearEntityDirty();
                }
                finally { runs.Dispose(); }
            }
        }

        void Recreate(int newCapacity)
        {
            var old = (boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers, lodChains);
            BufferHandle center = default, extent = default, flagBuffer = default, layers = default, scene = default, rendering = default, lods = default;
            try
            {
                center = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<float4>()) { name = "EntityBoundsCenter" });
                extent = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<float4>()) { name = "EntityBoundsExtent" });
                flagBuffer = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, sizeof(uint)) { name = "EntityFlags" });
                layers = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, sizeof(uint)) { name = "EntityLayerMask" });
                scene = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<uint2>()) { name = "EntitySceneCullingMask" });
                rendering = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, sizeof(uint)) { name = "EntityRenderingLayers" });
                lods = m_Pool.GetBuffer(new BufferDescriptor(newCapacity, Marshal.SizeOf<MeshLODChain>()) { name = "EntityLodChains" });
                (boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers, lodChains) = (center, extent, flagBuffer, layers, scene, rendering, lods);
                Upload(0, m_World.entityHighWater);
            }
            catch
            {
                (boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers, lodChains) = old;
                Release(center); Release(extent); Release(flagBuffer); Release(layers); Release(scene); Release(rendering); Release(lods);
                throw;
            }
            Release(old.boundsCenter); Release(old.boundsExtent); Release(old.flags);
            Release(old.layerMask); Release(old.sceneCullingMask); Release(old.renderingLayers); Release(old.lodChains);
            m_Capacity = newCapacity;
        }

        void Release(BufferHandle buffer)
        {
            if (buffer.buffer != null) m_Pool.ReleaseBuffer(buffer);
        }

        void Upload(int begin, int end)
        {
            end = math.min(end, m_World.entityHighWater);
            int count = end - begin;
            if (count <= 0) return;
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
                    if (!m_World.IsEntitySlotLive(i)) continue;
                    MeshEntityDesc desc = entities[i].desc;
                    centers[local] = new float4(bounds[i].center, 0);
                    extents[local] = new float4(bounds[i].extents, 0);
                    flagValues[local] = (uint)desc.flags;
                    layerMasks[local] = (uint)desc.layerMask;
                    sceneMasks[local] = new uint2((uint)desc.sceneCullingMask, (uint)(desc.sceneCullingMask >> 32));
                    renderingMasks[local] = desc.renderingLayerMask;
                    chains[local] = entities[i].lod;
                }
                boundsCenter.buffer.SetData(centers, 0, begin, count);
                boundsExtent.buffer.SetData(extents, 0, begin, count);
                flags.buffer.SetData(flagValues, 0, begin, count);
                layerMask.buffer.SetData(layerMasks, 0, begin, count);
                sceneCullingMask.buffer.SetData(sceneMasks, 0, begin, count);
                renderingLayers.buffer.SetData(renderingMasks, 0, begin, count);
                lodChains.buffer.SetData(chains, 0, begin, count);
                m_World.stats.uploadedBytes += (long)count * (Marshal.SizeOf<float4>() * 2 + sizeof(uint) * 5 + Marshal.SizeOf<MeshLODChain>());
            }
            finally
            {
                centers.Dispose(); extents.Dispose(); flagValues.Dispose();
                layerMasks.Dispose(); sceneMasks.Dispose(); renderingMasks.Dispose(); chains.Dispose();
            }
        }

        internal void Dispose()
        {
            transformVersions.Dispose();
            Release(boundsCenter); Release(boundsExtent); Release(flags);
            Release(layerMask); Release(sceneCullingMask); Release(renderingLayers); Release(lodChains);
            boundsCenter = boundsExtent = flags = layerMask = sceneCullingMask = renderingLayers = lodChains = default;
            m_Capacity = 0;
        }
    }
}
