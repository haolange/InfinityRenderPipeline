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
            using (MeshCpuMarkers.s_UpdateMeshWorldGPU.Auto())
            using (MeshCpuMarkers.s_ResidencyGate.Auto())
            // Dirty runs live until this update returns (AGENTS R17: Temp).
            using (var dirtyRuns = new NativeList<int2>(8, Allocator.Temp))
            {
                if (transformVersions.current == null || transformVersions.current.revision != m_World.transformRevision)
                {
                    using (MeshCpuMarkers.s_CollectDirtyRuns.Auto())
                    {
                        m_World.CollectTransformDirtyRuns(dirtyRuns);
                    }
                    using (MeshCpuMarkers.s_PrepareTransformVersion.Auto())
                    {
                        transformVersions.Prepare(m_World, dirtyRuns);
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
                        m_World.CollectEntityDirtyRuns(dirtyRuns);
                    }
                    foreach (int2 run in dirtyRuns)
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
                // Column scratch lives until SetData copies it (AGENTS R17: Temp). Dead slots are written as zero.
                var centers = new NativeArray<float4>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var extents = new NativeArray<float4>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var flags = new NativeArray<uint>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var layers = new NativeArray<uint>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var scenes = new NativeArray<uint2>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var rendering = new NativeArray<uint>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var chains = new NativeArray<MeshLODChain>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var entities = m_World.entities;
                var bounds = m_World.entityBounds;
                for (int i = begin; i < end; ++i)
                {
                    int local = i - begin;
                    if (!m_World.IsEntitySlotLive(i))
                    {
                        centers[local] = default;
                        extents[local] = default;
                        flags[local] = 0;
                        layers[local] = 0;
                        scenes[local] = default;
                        rendering[local] = 0;
                        chains[local] = default;
                        continue;
                    }
                    MeshEntityDesc desc = entities[i].desc;
                    centers[local] = new float4(bounds[i].center, 0);
                    extents[local] = new float4(bounds[i].extents, 0);
                    flags[local] = (uint)desc.flags;
                    layers[local] = (uint)desc.layerMask;
                    scenes[local] = new uint2((uint)desc.sceneCullingMask, (uint)(desc.sceneCullingMask >> 32));
                    rendering[local] = desc.renderingLayerMask;
                    chains[local] = entities[i].lod;
                }
                boundsCenterHandle.buffer.SetData(centers, 0, begin, count);
                boundsExtentHandle.buffer.SetData(extents, 0, begin, count);
                flagsHandle.buffer.SetData(flags, 0, begin, count);
                layerMaskHandle.buffer.SetData(layers, 0, begin, count);
                sceneCullingMaskHandle.buffer.SetData(scenes, 0, begin, count);
                renderingLayersHandle.buffer.SetData(rendering, 0, begin, count);
                lodChainsHandle.buffer.SetData(chains, 0, begin, count);
                m_World.stats.uploadedBytes += (long)count * (Marshal.SizeOf<float4>() * 2 + sizeof(uint) * 5 + Marshal.SizeOf<MeshLODChain>());
                centers.Dispose();
                extents.Dispose();
                flags.Dispose();
                layers.Dispose();
                scenes.Dispose();
                rendering.Dispose();
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
