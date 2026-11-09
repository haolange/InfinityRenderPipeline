using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    internal struct SlotUndo<T> where T : unmanaged
    {
        public int index;
        public uint generation;
        public bool wasLive;
        public T value;
    }

    public sealed partial class MeshWorld
    {
        struct PendingMobility
        {
            public int entity;
            public uint generation;
            public Transform transform;
            public BoundingBox localBounds;
        }

        bool m_TransactionOpen;
        ulong m_TransactionGeneration;
        NativeList<SlotUndo<MeshEntity>> m_EntityUndo;
        NativeList<SlotUndo<MeshElement>> m_ElementUndo;
        NativeList<SlotUndo<MeshSection>> m_SectionUndo;
        NativeList<SlotUndo<MeshMaterial>> m_MaterialUndo;
        NativeList<int> m_DirtyElements;
        NativeList<byte> m_ElementDirtyMark;
        NativeList<int> m_KilledEntities;
        NativeList<int> m_KilledElements;
        NativeList<int> m_ReleasedSections;
        NativeList<int> m_ReleasedMaterials;
        NativeList<ulong> m_SnapshotTransformWords;
        NativeList<ulong> m_SnapshotEntityWords;
        readonly List<PendingMobility> m_PendingMobility = new List<PendingMobility>(16);
        int m_SnapshotStructural;
        ulong m_SnapshotTransform;

        internal bool isUpdating => m_TransactionOpen;

        void InitializeTransaction()
        {
            m_EntityUndo = new NativeList<SlotUndo<MeshEntity>>(64, Allocator.Persistent);
            m_ElementUndo = new NativeList<SlotUndo<MeshElement>>(64, Allocator.Persistent);
            m_SectionUndo = new NativeList<SlotUndo<MeshSection>>(64, Allocator.Persistent);
            m_MaterialUndo = new NativeList<SlotUndo<MeshMaterial>>(64, Allocator.Persistent);
            m_DirtyElements = new NativeList<int>(64, Allocator.Persistent);
            m_ElementDirtyMark = new NativeList<byte>(1024, Allocator.Persistent);
            m_KilledEntities = new NativeList<int>(16, Allocator.Persistent);
            m_KilledElements = new NativeList<int>(16, Allocator.Persistent);
            m_ReleasedSections = new NativeList<int>(16, Allocator.Persistent);
            m_ReleasedMaterials = new NativeList<int>(16, Allocator.Persistent);
            m_SnapshotTransformWords = new NativeList<ulong>(4, Allocator.Persistent);
            m_SnapshotEntityWords = new NativeList<ulong>(4, Allocator.Persistent);
        }

        void DisposeTransaction()
        {
            m_EntityUndo.Dispose();
            m_ElementUndo.Dispose();
            m_SectionUndo.Dispose();
            m_MaterialUndo.Dispose();
            m_DirtyElements.Dispose();
            m_ElementDirtyMark.Dispose();
            m_KilledEntities.Dispose();
            m_KilledElements.Dispose();
            m_ReleasedSections.Dispose();
            m_ReleasedMaterials.Dispose();
            m_SnapshotTransformWords.Dispose();
            m_SnapshotEntityWords.Dispose();
        }

        public MeshWorldUpdate BeginUpdate()
        {
            ThrowIfDisposed();
            if (m_TransactionOpen)
            {
                throw new InvalidOperationException("A MeshWorldUpdate transaction is already open.");
            }
            m_TransactionOpen = true;
            m_TransactionGeneration++;
            m_SnapshotStructural = structuralRevision;
            m_SnapshotTransform = transformRevision;
            m_TransformDirty.CopyTo(m_SnapshotTransformWords);
            m_EntityDirty.CopyTo(m_SnapshotEntityWords);
            return new MeshWorldUpdate(this, m_TransactionGeneration);
        }

        internal bool IsUpdateActive(ulong generation) => m_TransactionOpen && generation == m_TransactionGeneration;

        void RequireTransaction(ulong generation)
        {
            if (!IsUpdateActive(generation))
            {
                throw new InvalidOperationException("This MeshWorldUpdate lease is closed or belongs to an earlier transaction.");
            }
        }

        #region Undo log
        void LogEntity(int index)
        {
            bool live = m_Entities.IsLive(index);
            m_EntityUndo.Add(new SlotUndo<MeshEntity> { index = index, generation = m_Entities.GenerationOf(index), wasLive = live, value = live ? m_Entities[index] : default });
        }
        void LogElement(int index)
        {
            bool live = m_Elements.IsLive(index);
            m_ElementUndo.Add(new SlotUndo<MeshElement> { index = index, generation = m_Elements.GenerationOf(index), wasLive = live, value = live ? m_Elements[index] : default });
        }
        void LogSection(int index)
        {
            bool live = m_Sections.IsLive(index);
            m_SectionUndo.Add(new SlotUndo<MeshSection> { index = index, generation = m_Sections.GenerationOf(index), wasLive = live, value = live ? m_Sections[index] : default });
        }
        void LogMaterial(int index)
        {
            bool live = m_Materials.IsLive(index);
            m_MaterialUndo.Add(new SlotUndo<MeshMaterial> { index = index, generation = m_Materials.GenerationOf(index), wasLive = live, value = live ? m_Materials[index] : default });
        }

        static void Replay<T>(ref SlotTable<T> table, NativeList<SlotUndo<T>> log) where T : unmanaged
        {
            for (int i = log.Length - 1; i >= 0; --i)
            {
                SlotUndo<T> entry = log[i];
                bool live = table.IsLive(entry.index);
                if (entry.wasLive)
                {
                    if (live)
                    {
                        table[entry.index] = entry.value;
                    }
                    else
                    {
                        table.Revive(entry.index, entry.generation, entry.value);
                    }
                }
                else
                {
                    // Allocated inside this transaction: kill it if still live and hand the slot back to the free list.
                    if (live)
                    {
                        table.Kill(entry.index);
                    }
                    table.Reclaim(entry.index);
                }
            }
            log.Clear();
        }
        #endregion

        void MarkElementDirty(int element)
        {
            while (m_ElementDirtyMark.Length <= element)
            {
                m_ElementDirtyMark.Add(0);
            }
            if (m_ElementDirtyMark[element] != 0)
            {
                return;
            }
            m_ElementDirtyMark[element] = 1;
            m_DirtyElements.Add(element);
        }

        void MarkEntityElementsDirty(int entity)
        {
            for (int element = m_Entities[entity].firstElement; element >= 0; element = m_Elements[element].nextElement)
                MarkElementDirty(element);
        }

        void MarkMaterialElementsDirty(int material)
        {
            for (int i = 0; i < m_Elements.HighWater; ++i)
                if (m_Elements.IsLive(i) && m_Elements[i].material.IsValid && (int)m_Elements[i].material.Index == material)
                    MarkElementDirty(i);
        }

        void RequireEntity(MeshEntityId id)
        {
            if (!IsAlive(id))
            {
                throw new ArgumentException("The entity is not alive.", nameof(id));
            }
        }

        void RequireElement(MeshElementId id)
        {
            if (!IsAlive(id))
            {
                throw new ArgumentException("The element is not alive.", nameof(id));
            }
        }

        #region Structural edits
        internal MeshEntityId CreateEntity(ulong generation, in float4x4 localToWorld, in BoundingBox bounds, in MeshEntityDesc desc, in MeshLODChain lod)
        {
            RequireTransaction(generation);
            RenderingLayerUtility.Validate(desc.renderingLayerMask);
            lod.ThrowIfInvalid();
            int index = m_Entities.Allocate(new MeshEntity { desc = desc, lod = lod, firstElement = -1, elementCount = 0 }, out uint entityGeneration);
            m_EntityUndo.Add(new SlotUndo<MeshEntity> { index = index, wasLive = false });
            while (m_EntityTransforms.Length <= index)
            {
                m_EntityTransforms.Add(float4x4.identity);
                m_EntityBounds.Add(default);
                m_EntityToDynamic.Add(-1);
            }
            m_EntityTransforms[index] = localToWorld;
            m_EntityBounds[index] = bounds;
            m_EntityToDynamic[index] = -1;
            m_TransformDirty.Mark(index);
            m_EntityDirty.Mark(index);
            structuralRevision++;
            transformRevision++;
            return new MeshEntityId((uint)index, entityGeneration);
        }

        internal void SetEntityDesc(ulong generation, MeshEntityId id, in MeshEntityDesc desc)
        {
            RequireTransaction(generation);
            RequireEntity(id);
            RenderingLayerUtility.Validate(desc.renderingLayerMask);
            int index = (int)id.Index;
            MeshEntity entity = m_Entities[index];
            if (entity.desc.Equals(desc))
            {
                return;
            }
            LogEntity(index);
            bool membershipChanged = entity.desc.flags != desc.flags;
            entity.desc = desc;
            m_Entities[index] = entity;
            m_EntityDirty.Mark(index);
            if (membershipChanged)
            {
                MarkEntityElementsDirty(index);
                structuralRevision++;
            }
        }

        internal void SetEntityMobility(ulong generation, MeshEntityId id, Transform transform, in BoundingBox localBounds)
        {
            RequireTransaction(generation);
            RequireEntity(id);
            m_PendingMobility.Add(new PendingMobility { entity = (int)id.Index, generation = id.Generation, transform = transform, localBounds = localBounds });
        }

        internal MeshElementId AddElement(ulong generation, MeshEntityId entityId, ulong meshId, int submesh, int lodIndex, ulong materialId, in MeshMaterialState state, int priority)
        {
            RequireTransaction(generation);
            RequireEntity(entityId);
            if (meshId == 0)
            {
                throw new ArgumentException("An element requires a mesh.", nameof(meshId));
            }
            if (submesh < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(submesh));
            }
            if (lodIndex < 0 || lodIndex >= m_Entities[(int)entityId.Index].lod.count)
            {
                throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "The element's LOD level is outside its entity's chain.");
            }
            MeshSectionId section = AcquireSection(meshId, submesh);
            MeshMaterialId material = AcquireMaterial(materialId, state);
            int entityIndex = (int)entityId.Index;
            MeshEntity entity = m_Entities[entityIndex];
            int index = m_Elements.Allocate(new MeshElement
            {
                entity = entityId, section = section, material = material, lodIndex = (byte)lodIndex, priority = priority, nextElement = entity.firstElement
            }, out uint elementGeneration);
            m_ElementUndo.Add(new SlotUndo<MeshElement> { index = index, wasLive = false });
            LogEntity(entityIndex);
            entity.firstElement = index;
            entity.elementCount++;
            m_Entities[entityIndex] = entity;
            MarkElementDirty(index);
            structuralRevision++;
            return new MeshElementId((uint)index, elementGeneration);
        }

        internal void SetElementMaterial(ulong generation, MeshElementId id, ulong materialId, in MeshMaterialState state)
        {
            RequireTransaction(generation);
            RequireElement(id);
            int index = (int)id.Index;
            MeshElement element = m_Elements[index];
            if (element.material.IsValid && m_Materials[(int)element.material.Index].materialId == materialId)
            {
                SetMaterialState((int)element.material.Index, state);
                return;
            }
            if (!element.material.IsValid && materialId == 0)
            {
                return;
            }
            LogElement(index);
            MeshMaterialId material = AcquireMaterial(materialId, state);
            ReleaseMaterial(element.material);
            element.material = material;
            m_Elements[index] = element;
            MarkElementDirty(index);
            structuralRevision++;
        }

        internal void SetElementPriority(ulong generation, MeshElementId id, int priority)
        {
            RequireTransaction(generation);
            RequireElement(id);
            int index = (int)id.Index;
            MeshElement element = m_Elements[index];
            if (element.priority == priority)
            {
                return;
            }
            LogElement(index);
            element.priority = priority;
            m_Elements[index] = element;
        }

        internal void RemoveEntity(ulong generation, MeshEntityId id)
        {
            RequireTransaction(generation);
            RequireEntity(id);
            int index = (int)id.Index;
            for (int element = m_Entities[index].firstElement; element >= 0;)
            {
                MeshElement record = m_Elements[element];
                int next = record.nextElement;
                LogElement(element);
                ReleaseSection(record.section);
                ReleaseMaterial(record.material);
                m_Elements.Kill(element);
                m_KilledElements.Add(element);
                MarkElementDirty(element);
                element = next;
            }
            LogEntity(index);
            m_Entities.Kill(index);
            m_KilledEntities.Add(index);
            m_PendingMobility.Add(new PendingMobility { entity = index, generation = id.Generation, transform = null });
            m_TransformDirty.Mark(index);
            m_EntityDirty.Mark(index);
            structuralRevision++;
            transformRevision++;
        }

        void SetMaterialState(int index, in MeshMaterialState state)
        {
            MeshMaterial material = m_Materials[index];
            if (material.state.Equals(state))
            {
                return;
            }
            LogMaterial(index);
            material.state = state;
            m_Materials[index] = material;
            MarkMaterialElementsDirty(index);
            structuralRevision++;
        }

        MeshSectionId AcquireSection(ulong meshId, int submesh)
        {
            var key = new SectionKey(meshId, submesh);
            if (m_SectionLookup.TryGetValue(key, out int index))
            {
                LogSection(index);
                MeshSection section = m_Sections[index];
                section.refCount++;
                m_Sections[index] = section;
                return new MeshSectionId((uint)index, m_Sections.GenerationOf(index));
            }
            index = m_Sections.Allocate(new MeshSection { meshId = meshId, submesh = submesh, refCount = 1 }, out uint generation);
            m_SectionUndo.Add(new SlotUndo<MeshSection> { index = index, wasLive = false });
            m_SectionLookup.Add(key, index);
            return new MeshSectionId((uint)index, generation);
        }

        void ReleaseSection(MeshSectionId id)
        {
            int index = (int)id.Index;
            LogSection(index);
            MeshSection section = m_Sections[index];
            section.refCount--;
            m_Sections[index] = section;
            if (section.refCount == 0)
            {
                m_ReleasedSections.Add(index);
            }
        }

        MeshMaterialId AcquireMaterial(ulong materialId, in MeshMaterialState state)
        {
            if (materialId == 0)
            {
                return MeshMaterialId.Invalid;
            }
            if (m_MaterialLookup.TryGetValue(materialId, out int index))
            {
                SetMaterialState(index, state);
                LogMaterial(index);
                MeshMaterial material = m_Materials[index];
                material.refCount++;
                m_Materials[index] = material;
                return new MeshMaterialId((uint)index, m_Materials.GenerationOf(index));
            }
            index = m_Materials.Allocate(new MeshMaterial { materialId = materialId, state = state, refCount = 1 }, out uint generation);
            m_MaterialUndo.Add(new SlotUndo<MeshMaterial> { index = index, wasLive = false });
            m_MaterialLookup.Add(materialId, index);
            return new MeshMaterialId((uint)index, generation);
        }

        void ReleaseMaterial(MeshMaterialId id)
        {
            if (!id.IsValid)
            {
                return;
            }
            int index = (int)id.Index;
            LogMaterial(index);
            MeshMaterial material = m_Materials[index];
            material.refCount--;
            m_Materials[index] = material;
            if (material.refCount == 0)
            {
                m_ReleasedMaterials.Add(index);
            }
        }
        #endregion

        #region Commit and rollback
        internal void Commit(ulong generation)
        {
            RequireTransaction(generation);
            // Every pass cache resolves the bindings it would create before anything is published. A failure here leaves
            // the transaction open, so the lease's Dispose rolls it back.
            foreach (MeshPassCache cache in m_PassCaches)
            {
                cache.Prepare(this, m_DirtyElements.AsArray());
            }

            // Published: nothing below may roll back, so reclaimed slots can never be revived.
            m_TransactionOpen = false;
            try
            {
                PublishCommit();
            }
            finally
            {
                ClearTransactionState();
                PublishStats();
            }
        }

        void PublishCommit()
        {
            foreach (int entity in m_KilledEntities)
            {
                m_Entities.Reclaim(entity);
            }
            foreach (int element in m_KilledElements)
            {
                m_Elements.Reclaim(element);
            }
            foreach (int index in m_ReleasedSections)
            {
                if (!m_Sections.IsLive(index) || m_Sections[index].refCount != 0)
                {
                    continue;
                }
                MeshSection section = m_Sections[index];
                m_SectionLookup.Remove(new SectionKey(section.meshId, section.submesh));
                m_Sections.Kill(index);
                m_Sections.Reclaim(index);
            }
            foreach (int index in m_ReleasedMaterials)
            {
                if (!m_Materials.IsLive(index) || m_Materials[index].refCount != 0)
                {
                    continue;
                }
                m_MaterialLookup.Remove(m_Materials[index].materialId);
                m_Materials.Kill(index);
                m_Materials.Reclaim(index);
            }
            foreach (PendingMobility pending in m_PendingMobility)
            {
                bool alive = m_Entities.IsLive(pending.entity, pending.generation);
                if (alive && pending.transform != null)
                {
                    RegisterDynamic(pending.entity, pending.transform, pending.localBounds);
                }
                else
                {
                    UnregisterDynamic(pending.entity);
                }
            }
            foreach (MeshPassCache cache in m_PassCaches)
            {
                cache.Apply(this);
            }
        }

        internal void Rollback(ulong generation)
        {
            if (!IsUpdateActive(generation))
            {
                return;
            }
            foreach (MeshPassCache cache in m_PassCaches)
            {
                cache.DiscardPrepared();
            }
            Replay(ref m_Elements, m_ElementUndo);
            Replay(ref m_Entities, m_EntityUndo);
            ReplayKeyed(ref m_Sections, m_SectionUndo, m_SectionLookup);
            ReplayKeyed(ref m_Materials, m_MaterialUndo, m_MaterialLookup);
            structuralRevision = m_SnapshotStructural;
            transformRevision = m_SnapshotTransform;
            m_TransformDirty.Restore(m_SnapshotTransformWords);
            m_EntityDirty.Restore(m_SnapshotEntityWords);
            ClearTransactionState();
            m_TransactionOpen = false;
        }

        static void ReplayKeyed(ref SlotTable<MeshSection> table, NativeList<SlotUndo<MeshSection>> log, NativeParallelHashMap<SectionKey, int> lookup)
        {
            // Sections are only killed at commit, so keys never move inside a transaction: drop every touched key, replay,
            // then publish the keys of the slots that are live again.
            foreach (SlotUndo<MeshSection> entry in log)
            {
                if (table.IsLive(entry.index))
                {
                    lookup.Remove(new SectionKey(table[entry.index].meshId, table[entry.index].submesh));
                }
                if (entry.wasLive)
                {
                    lookup.Remove(new SectionKey(entry.value.meshId, entry.value.submesh));
                }
            }
            var touched = new NativeList<int>(log.Length, Allocator.Temp);
            foreach (SlotUndo<MeshSection> entry in log)
            {
                touched.Add(entry.index);
            }
            Replay(ref table, log);
            foreach (int index in touched)
                if (table.IsLive(index))
                {
                    lookup[new SectionKey(table[index].meshId, table[index].submesh)] = index;
                }
            touched.Dispose();
        }

        static void ReplayKeyed(ref SlotTable<MeshMaterial> table, NativeList<SlotUndo<MeshMaterial>> log, NativeParallelHashMap<ulong, int> lookup)
        {
            foreach (SlotUndo<MeshMaterial> entry in log)
            {
                if (table.IsLive(entry.index))
                {
                    lookup.Remove(table[entry.index].materialId);
                }
                if (entry.wasLive)
                {
                    lookup.Remove(entry.value.materialId);
                }
            }
            var touched = new NativeList<int>(log.Length, Allocator.Temp);
            foreach (SlotUndo<MeshMaterial> entry in log)
            {
                touched.Add(entry.index);
            }
            Replay(ref table, log);
            foreach (int index in touched)
                if (table.IsLive(index))
                {
                    lookup[table[index].materialId] = index;
                }
            touched.Dispose();
        }

        void ClearTransactionState()
        {
            m_EntityUndo.Clear();
            m_ElementUndo.Clear();
            m_SectionUndo.Clear();
            m_MaterialUndo.Clear();
            foreach (int element in m_DirtyElements)
            {
                m_ElementDirtyMark[element] = 0;
            }
            m_DirtyElements.Clear();
            m_KilledEntities.Clear();
            m_KilledElements.Clear();
            m_ReleasedSections.Clear();
            m_ReleasedMaterials.Clear();
            m_PendingMobility.Clear();
        }

        void PublishStats()
        {
            int commands = 0;
            foreach (MeshPassCache cache in m_PassCaches)
            {
                commands += cache.commandCount;
            }
            stats.passCacheCount = m_PassCaches.Count;
            stats.commandCount = commands;
        }
        #endregion
    }

    /// <summary>
    /// One structural transaction on a <see cref="MeshWorld"/>. Dispose without <see cref="Commit"/> rolls every edit back.
    /// The lease carries the transaction generation; a copied or expired lease cannot edit a newer transaction.
    /// </summary>
    public readonly struct MeshWorldUpdate : IDisposable
    {
        readonly MeshWorld m_World;
        readonly ulong m_Generation;

        internal MeshWorldUpdate(MeshWorld world, ulong generation)
        {
            m_World = world;
            m_Generation = generation;
        }

        MeshWorld world => m_World ?? throw new InvalidOperationException("Default MeshWorldUpdate.");

        public MeshEntityId CreateEntity(in float4x4 localToWorld, in BoundingBox bounds, in MeshEntityDesc desc, in MeshLODChain lod)
            => world.CreateEntity(m_Generation, localToWorld, bounds, desc, lod);

        public void SetEntityDesc(MeshEntityId entity, in MeshEntityDesc desc) => world.SetEntityDesc(m_Generation, entity, desc);

        /// <summary>A non-null transform makes the entity dynamic: its pose is read from the transform every frame.</summary>
        public void SetEntityMobility(MeshEntityId entity, Transform dynamicTransform, in BoundingBox localBounds)
            => world.SetEntityMobility(m_Generation, entity, dynamicTransform, localBounds);

        public MeshElementId AddElement(MeshEntityId entity, ulong meshId, int submesh, int lodIndex, ulong materialId, in MeshMaterialState materialState, int priority)
            => world.AddElement(m_Generation, entity, meshId, submesh, lodIndex, materialId, materialState, priority);

        public void SetElementMaterial(MeshElementId element, ulong materialId, in MeshMaterialState materialState)
            => world.SetElementMaterial(m_Generation, element, materialId, materialState);

        public void SetElementPriority(MeshElementId element, int priority) => world.SetElementPriority(m_Generation, element, priority);

        public void RemoveEntity(MeshEntityId entity) => world.RemoveEntity(m_Generation, entity);

        public void Commit() => world.Commit(m_Generation);

        public void Rollback() => m_World?.Rollback(m_Generation);

        public void Dispose() => Rollback();
    }
}
