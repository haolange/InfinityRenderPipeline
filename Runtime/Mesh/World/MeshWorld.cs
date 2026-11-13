using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>Counters for the debugger, tests and the runtime probe. Frame counters reset in <see cref="MeshWorld.BeginCamera"/>.</summary>
    public sealed class MeshWorldStats
    {
        public int passCacheCount;
        public int commandCount;
        public int passCacheBuilds;
        // GPU draw-list producers recorded for the last camera, and the kernels they dispatch.
        public int drawListProductions;
        public int drawListKernelDispatches;
        public long uploadedBytes;
        internal Camera camera;

        internal void BeginCamera(Camera camera)
        {
            this.camera = camera;
            drawListProductions = 0;
            drawListKernelDispatches = 0;
        }
    }

    /// <summary>
    /// The mesh world: entity, element, section and material tables, their GPU residency, the interned pass caches and the
    /// draw backends. One world per render context. Structural edits go through <see cref="MeshWorldUpdate"/>; poses
    /// (transform and bounds) are written outside transactions and do not take part in undo.
    /// </summary>
    public sealed partial class MeshWorld : IDisposable
    {
        const int k_InitialCapacity = 1024;
        static int s_NextWorldId;

        SlotTable<MeshEntity> m_Entities;
        NativeList<float4x4> m_EntityTransforms;
        NativeList<BoundingBox> m_EntityBounds;
        NativeList<int> m_EntityToDynamic;
        SlotTable<MeshElement> m_Elements;
        SlotTable<MeshSection> m_Sections;
        SlotTable<MeshMaterial> m_Materials;
        NativeParallelHashMap<SectionKey, int> m_SectionLookup;
        NativeParallelHashMap<ulong, int> m_MaterialLookup;
        DirtyPageBitmap m_TransformDirty;
        DirtyPageBitmap m_EntityDirty;

        TransformAccessArray m_DynamicTransforms;
        NativeList<int> m_DynamicToEntity;
        NativeList<BoundingBox> m_DynamicLocalBounds;
        NativeList<byte> m_DynamicChanged;

        bool m_Disposed;

        /// <summary>Process-unique identity, used to keep transform versions and visibility results of different worlds apart.</summary>
        public int worldId
        {
            get;
        }
        /// <summary>Compute_MeshDrawPipeline: transform versions on every backend, culling and compaction on GPU indirect.</summary>
        internal ComputeShader meshDrawShader
        {
            get;
        }
        public int structuralRevision
        {
            get;
            private set;
        }
        public ulong transformRevision
        {
            get;
            private set;
        }
        public readonly MeshWorldStats stats = new MeshWorldStats();

        public int entityCount => m_Entities.LiveCount;
        public int elementCount => m_Elements.LiveCount;
        public int sectionCount => m_Sections.LiveCount;
        public int materialCount => m_Materials.LiveCount;
        public int entityHighWater => m_Entities.HighWater;
        public int elementHighWater => m_Elements.HighWater;
        public int dynamicEntityCount => m_DynamicToEntity.Length;

        internal NativeArray<MeshEntity> entities => m_Entities.Items;
        internal NativeArray<uint> entityGenerations => m_Entities.Generations;
        internal NativeArray<float4x4> entityTransforms => m_EntityTransforms.AsArray();
        internal NativeArray<BoundingBox> entityBounds => m_EntityBounds.AsArray();
        internal NativeArray<MeshElement> elements => m_Elements.Items;
        internal NativeArray<uint> elementGenerations => m_Elements.Generations;
        internal NativeArray<MeshSection> sections => m_Sections.Items;
        internal NativeArray<MeshMaterial> materials => m_Materials.Items;

        public MeshWorld(ResourcePool resourcePool, ComputeShader meshDrawShader)
        {
            if (resourcePool == null)
            {
                throw new ArgumentNullException(nameof(resourcePool));
            }
            if (meshDrawShader == null)
            {
                throw new ArgumentNullException(nameof(meshDrawShader));
            }
            worldId = System.Threading.Interlocked.Increment(ref s_NextWorldId);
            this.meshDrawShader = meshDrawShader;
            m_Entities = new SlotTable<MeshEntity>(k_InitialCapacity);
            m_EntityTransforms = new NativeList<float4x4>(k_InitialCapacity, Allocator.Persistent);
            m_EntityBounds = new NativeList<BoundingBox>(k_InitialCapacity, Allocator.Persistent);
            m_EntityToDynamic = new NativeList<int>(k_InitialCapacity, Allocator.Persistent);
            m_Elements = new SlotTable<MeshElement>(k_InitialCapacity * 2);
            m_Sections = new SlotTable<MeshSection>(k_InitialCapacity);
            m_Materials = new SlotTable<MeshMaterial>(k_InitialCapacity);
            m_SectionLookup = new NativeParallelHashMap<SectionKey, int>(k_InitialCapacity, Allocator.Persistent);
            m_MaterialLookup = new NativeParallelHashMap<ulong, int>(k_InitialCapacity, Allocator.Persistent);
            m_TransformDirty.Init();
            m_EntityDirty.Init();
            m_DynamicTransforms = new TransformAccessArray(256);
            m_DynamicToEntity = new NativeList<int>(256, Allocator.Persistent);
            m_DynamicLocalBounds = new NativeList<BoundingBox>(256, Allocator.Persistent);
            m_DynamicChanged = new NativeList<byte>(256, Allocator.Persistent);
            InitializeTransaction();
            m_Gpu = new MeshWorldGPU(this, resourcePool);
            m_ResourcePool = resourcePool;
            m_CPUBackend = new MeshDrawCPUBackend(resourcePool);
            m_GPUBackend = new MeshDrawGPUBackend(resourcePool, meshDrawShader);
            InternPresets();
        }

        public bool IsAlive(MeshEntityId id) => id.IsValid && m_Entities.IsLive((int)id.Index, id.Generation);
        public bool IsAlive(MeshElementId id) => id.IsValid && m_Elements.IsLive((int)id.Index, id.Generation);
        internal bool IsEntitySlotLive(int index) => m_Entities.IsLive(index);
        internal bool IsElementSlotLive(int index) => m_Elements.IsLive(index);

        public bool TryGetEntity(MeshEntityId id, out MeshEntity entity)
        {
            entity = IsAlive(id) ? m_Entities[(int)id.Index] : default;
            return IsAlive(id);
        }

        public bool TryGetElement(MeshElementId id, out MeshElement element)
        {
            element = IsAlive(id) ? m_Elements[(int)id.Index] : default;
            return IsAlive(id);
        }

        public bool TryGetPose(MeshEntityId id, out float4x4 localToWorld, out BoundingBox bounds)
        {
            bool alive = IsAlive(id);
            localToWorld = alive ? m_EntityTransforms[(int)id.Index] : float4x4.identity;
            bounds = alive ? m_EntityBounds[(int)id.Index] : default;
            return alive;
        }

        public bool TryGetSection(MeshSectionId id, out MeshSection section)
        {
            bool alive = id.IsValid && m_Sections.IsLive((int)id.Index, id.Generation);
            section = alive ? m_Sections[(int)id.Index] : default;
            return alive;
        }

        public bool TryGetMaterial(MeshMaterialId id, out MeshMaterial material)
        {
            bool alive = id.IsValid && m_Materials.IsLive((int)id.Index, id.Generation);
            material = alive ? m_Materials[(int)id.Index] : default;
            return alive;
        }

        /// <summary>
        /// Writes one entity pose outside transactions. Used for explicit static pose notifications.
        /// </summary>
        public void WriteEntityPose(MeshEntityId id, in float4x4 localToWorld, in BoundingBox bounds)
        {
            ThrowIfDisposed();
            if (m_TransactionOpen)
            {
                throw new InvalidOperationException("Entity poses are written outside MeshWorldUpdate transactions.");
            }
            if (!IsAlive(id))
            {
                throw new ArgumentException("WriteEntityPose requires a live entity.", nameof(id));
            }
            int index = (int)id.Index;
            if (m_EntityTransforms[index].Equals(localToWorld) && m_EntityBounds[index].Equals(bounds))
            {
                return;
            }
            m_EntityTransforms[index] = localToWorld;
            m_EntityBounds[index] = bounds;
            m_TransformDirty.Mark(index);
            m_EntityDirty.Mark(index);
            transformRevision++;
        }

        #region Dynamic poses
        [BurstCompile]
        struct MeshDynamicPoseJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<int> dynamicToEntity;
            [ReadOnly] public NativeArray<BoundingBox> localBounds;
            [NativeDisableParallelForRestriction] public NativeArray<float4x4> entityTransforms;
            [NativeDisableParallelForRestriction] public NativeArray<BoundingBox> entityBounds;
            [WriteOnly] public NativeArray<byte> changed;

            public void Execute(int index, TransformAccess transform)
            {
                int entity = dynamicToEntity[index];
                float4x4 matrix = transform.localToWorldMatrix;
                bool same = matrix.Equals(entityTransforms[entity]);
                changed[index] = (byte)(same ? 0 : 1);
                if (same)
                {
                    return;
                }
                BoundingBox local = localBounds[index];
                entityTransforms[entity] = matrix;
                entityBounds[entity] = BoundingBox.Transform(local, matrix);
            }
        }

        [BurstCompile]
        struct MeshDynamicDirtyJob : IJob
        {
            [ReadOnly] public NativeArray<byte> changed;
            [ReadOnly] public NativeArray<int> dynamicToEntity;
            public NativeArray<ulong> transformWords;
            public NativeArray<ulong> entityWords;
            public NativeArray<int> changedCount;

            public void Execute()
            {
                int count = 0;
                for (int i = 0; i < changed.Length; ++i)
                {
                    if (changed[i] == 0)
                    {
                        continue;
                    }
                    int entity = dynamicToEntity[i];
                    DirtyPageBitmap.Mark(transformWords, entity);
                    DirtyPageBitmap.Mark(entityWords, entity);
                    count++;
                }
                changedCount[0] = count;
            }
        }

        /// <summary>
        /// Reads every dynamic entity's Transform in parallel and writes changed poses by entity index; a second job marks
        /// the dirty pages. Revisions advance once when any pose changed.
        /// </summary>
        public void UpdateDynamicPoses()
        {
            ThrowIfDisposed();
            if (m_TransactionOpen)
            {
                throw new InvalidOperationException("Dynamic poses are read outside MeshWorldUpdate transactions.");
            }
            int count = m_DynamicToEntity.Length;
            if (count == 0)
            {
                return;
            }
            m_DynamicChanged.ResizeUninitialized(count);
            var changedCount = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                JobHandle handle = new MeshDynamicPoseJob
                {
                    dynamicToEntity = m_DynamicToEntity.AsArray(),
                    localBounds = m_DynamicLocalBounds.AsArray(),
                    entityTransforms = m_EntityTransforms.AsArray(),
                    entityBounds = m_EntityBounds.AsArray(),
                    changed = m_DynamicChanged.AsArray()
                }.ScheduleReadOnly(m_DynamicTransforms, 64);
                handle = new MeshDynamicDirtyJob
                {
                    changed = m_DynamicChanged.AsArray(),
                    dynamicToEntity = m_DynamicToEntity.AsArray(),
                    transformWords = m_TransformDirty.Words(m_Entities.HighWater),
                    entityWords = m_EntityDirty.Words(m_Entities.HighWater),
                    changedCount = changedCount
                }.Schedule(handle);
                handle.Complete();
                if (changedCount[0] > 0)
                {
                    transformRevision++;
                }
            }
            finally
            {
                changedCount.Dispose();
            }
        }

        void RegisterDynamic(int entity, Transform transform, in BoundingBox localBounds)
        {
            int slot = m_EntityToDynamic[entity];
            if (slot >= 0)
            {
                m_DynamicTransforms[slot] = transform;
                m_DynamicLocalBounds[slot] = localBounds;
                return;
            }
            m_EntityToDynamic[entity] = m_DynamicToEntity.Length;
            m_DynamicTransforms.Add(transform);
            m_DynamicToEntity.Add(entity);
            m_DynamicLocalBounds.Add(localBounds);
        }

        void UnregisterDynamic(int entity)
        {
            int slot = m_EntityToDynamic[entity];
            if (slot < 0)
            {
                return;
            }
            int last = m_DynamicToEntity.Length - 1;
            int moved = m_DynamicToEntity[last];
            m_DynamicTransforms.RemoveAtSwapBack(slot);
            m_DynamicToEntity.RemoveAtSwapBack(slot);
            m_DynamicLocalBounds.RemoveAtSwapBack(slot);
            if (moved != entity)
            {
                m_EntityToDynamic[moved] = slot;
            }
            m_EntityToDynamic[entity] = -1;
        }

        public bool IsDynamic(MeshEntityId id) => IsAlive(id) && m_EntityToDynamic[(int)id.Index] >= 0;
        #endregion

        internal void CollectTransformDirtyRuns(NativeList<int2> runs) => m_TransformDirty.CollectRuns(runs, m_Entities.HighWater);
        internal void CollectEntityDirtyRuns(NativeList<int2> runs) => m_EntityDirty.CollectRuns(runs, m_Entities.HighWater);
        internal bool hasEntityDirty => m_EntityDirty.Any;
        internal void ClearTransformDirty() => m_TransformDirty.Clear();
        internal void ClearEntityDirty() => m_EntityDirty.Clear();

        void ThrowIfDisposed()
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(nameof(MeshWorld));
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            if (m_TransactionOpen)
            {
                throw new InvalidOperationException("MeshWorld cannot be disposed while a MeshWorldUpdate is open.");
            }
            m_Disposed = true;
            var failures = new List<Exception>();
            void Release(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            Release(DisposeFrameState);
            Release(() => m_Gpu.Dispose());
            foreach (MeshPassCache cache in m_PassCaches)
            {
                Release(cache.Dispose);
            }
            m_PassCaches.Clear();
            Release(m_CPUBackend.Dispose);
            Release(m_GPUBackend.Dispose);
            Release(DisposeTransaction);
            m_Entities.Dispose();
            m_EntityTransforms.Dispose();
            m_EntityBounds.Dispose();
            m_EntityToDynamic.Dispose();
            m_Elements.Dispose();
            m_Sections.Dispose();
            m_Materials.Dispose();
            m_SectionLookup.Dispose();
            m_MaterialLookup.Dispose();
            m_TransformDirty.Dispose();
            m_EntityDirty.Dispose();
            if (m_DynamicTransforms.isCreated)
            {
                m_DynamicTransforms.Dispose();
            }
            m_DynamicToEntity.Dispose();
            m_DynamicLocalBounds.Dispose();
            m_DynamicChanged.Dispose();
            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }
    }
}
