using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
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
        readonly NativeList<int2> m_DirtyRuns;
        NativeArray<float4> m_Centers, m_Extents;
        NativeArray<uint> m_Flags, m_Layers, m_Rendering;
        NativeArray<uint2> m_Scenes;
        NativeArray<MeshLODChain> m_Chains;
        internal readonly MeshTransformVersionCache transformVersions;
        int m_Capacity;

        internal BufferHandle boundsCenterHandle, boundsExtentHandle, flagsHandle, layerMaskHandle, sceneCullingMaskHandle, renderingLayersHandle, lodChainsHandle;
        internal int capacity => m_Capacity;

        internal MeshWorldGPU(MeshWorld world, ResourcePool pool)
        {
            m_World = world;
            m_Pool = pool;
            m_DirtyRuns = new NativeList<int2>(8, Allocator.Persistent);
            transformVersions = new MeshTransformVersionCache(pool, world.meshDrawShader);
        }

        internal MeshWorldGPUTables Snapshot() => new MeshWorldGPUTables(this, m_World.entityHighWater);

        internal void Update()
        {
            if (m_World.isUpdating)
            {
                throw new System.InvalidOperationException("GPU residency cannot observe an open MeshWorldUpdate.");
            }
            using (MeshCpuMarkers.s_UpdateMeshWorldGPU.Auto())
            using (MeshCpuMarkers.s_ResidencyGate.Auto())
            {
                if (transformVersions.current == null || transformVersions.current.revision != m_World.transformRevision)
                {
                    using (MeshCpuMarkers.s_CollectDirtyRuns.Auto())
                    {
                        m_World.CollectTransformDirtyRuns(m_DirtyRuns);
                    }
                    using (MeshCpuMarkers.s_PrepareTransformVersion.Auto())
                    {
                        transformVersions.Prepare(m_World, m_DirtyRuns);
                    }
                }
                m_World.ClearTransformDirty();

                int needed = math.max(64, m_World.entityHighWater);
                bool recreate = m_Capacity < needed || renderingLayersHandle.buffer == null;
                if (!recreate && m_World.hasEntityDirty)
                {
                    using (MeshCpuMarkers.s_CanWriteBuffer.Auto())
                    {
                        recreate = !m_Pool.CanWriteBuffer(boundsCenterHandle) || !m_Pool.CanWriteBuffer(boundsExtentHandle) || !m_Pool.CanWriteBuffer(flagsHandle)
                            || !m_Pool.CanWriteBuffer(layerMaskHandle) || !m_Pool.CanWriteBuffer(sceneCullingMaskHandle) || !m_Pool.CanWriteBuffer(renderingLayersHandle)
                            || !m_Pool.CanWriteBuffer(lodChainsHandle);
                    }
                }
                if (recreate)
                {
                    using (MeshCpuMarkers.s_RecreateColumns.Auto())
                    {
                        Recreate(math.max(needed, m_Capacity));
                    }
                }
                else if (m_World.hasEntityDirty)
                {
                    using (MeshCpuMarkers.s_CollectDirtyRuns.Auto())
                    {
                        m_World.CollectEntityDirtyRuns(m_DirtyRuns);
                    }
                    foreach (int2 run in m_DirtyRuns)
                    {
                        Upload(run.x, run.y);
                    }
                }
                m_World.ClearEntityDirty();
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
            using (MeshCpuMarkers.s_UploadColumns.Auto())
            {
                EnsureColumnScratch(count);
                var entities = m_World.entities;
                var bounds = m_World.entityBounds;
                for (int i = begin; i < end; ++i)
                {
                    int local = i - begin;
                    if (!m_World.IsEntitySlotLive(i))
                    {
                        m_Centers[local] = default;
                        m_Extents[local] = default;
                        m_Flags[local] = 0;
                        m_Layers[local] = 0;
                        m_Scenes[local] = default;
                        m_Rendering[local] = 0;
                        m_Chains[local] = default;
                        continue;
                    }
                    MeshEntityDesc desc = entities[i].desc;
                    m_Centers[local] = new float4(bounds[i].center, 0);
                    m_Extents[local] = new float4(bounds[i].extents, 0);
                    m_Flags[local] = (uint)desc.flags;
                    m_Layers[local] = (uint)desc.layerMask;
                    m_Scenes[local] = new uint2((uint)desc.sceneCullingMask, (uint)(desc.sceneCullingMask >> 32));
                    m_Rendering[local] = desc.renderingLayerMask;
                    m_Chains[local] = entities[i].lod;
                }
                boundsCenterHandle.buffer.SetData(m_Centers, 0, begin, count);
                boundsExtentHandle.buffer.SetData(m_Extents, 0, begin, count);
                flagsHandle.buffer.SetData(m_Flags, 0, begin, count);
                layerMaskHandle.buffer.SetData(m_Layers, 0, begin, count);
                sceneCullingMaskHandle.buffer.SetData(m_Scenes, 0, begin, count);
                renderingLayersHandle.buffer.SetData(m_Rendering, 0, begin, count);
                lodChainsHandle.buffer.SetData(m_Chains, 0, begin, count);
                m_World.stats.uploadedBytes += (long)count * (Marshal.SizeOf<float4>() * 2 + sizeof(uint) * 5 + Marshal.SizeOf<MeshLODChain>());
            }
        }

        void EnsureColumnScratch(int count)
        {
            if (m_Centers.IsCreated && m_Centers.Length >= count)
            {
                return;
            }
            int capacity = math.max(count, m_Centers.IsCreated ? m_Centers.Length * 2 : count);
            DisposeColumnScratch();
            m_Centers = new NativeArray<float4>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Extents = new NativeArray<float4>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Flags = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Layers = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Scenes = new NativeArray<uint2>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Rendering = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Chains = new NativeArray<MeshLODChain>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        void DisposeColumnScratch()
        {
            if (m_Centers.IsCreated) m_Centers.Dispose();
            if (m_Extents.IsCreated) m_Extents.Dispose();
            if (m_Flags.IsCreated) m_Flags.Dispose();
            if (m_Layers.IsCreated) m_Layers.Dispose();
            if (m_Scenes.IsCreated) m_Scenes.Dispose();
            if (m_Rendering.IsCreated) m_Rendering.Dispose();
            if (m_Chains.IsCreated) m_Chains.Dispose();
        }

        internal void Dispose()
        {
            if (m_DirtyRuns.IsCreated)
            {
                m_DirtyRuns.Dispose();
            }
            DisposeColumnScratch();
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
