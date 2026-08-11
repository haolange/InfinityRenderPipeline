using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// CPU authoritative mesh scene: one Transform per logical instance, N draws per instance.
    /// Draw slots use a free list (generation bump); Filter scans the table by live draw.instance.
    /// </summary>
    public class MeshScene : IDisposable
    {
        private const int k_DefaultCapacity = 1024;
        private const int k_TombstoneId = -1;
        private static int s_NextSceneId;

        private NativeArray<MeshInstanceRecord> m_Instances;
        private NativeArray<uint> m_InstanceGenerations;
        private NativeList<int> m_InstanceFreeList;

        private NativeArray<TransformRecord> m_Transforms;
        private NativeArray<uint> m_TransformGenerations;
        private NativeArray<int> m_TransformOwners;
        private NativeList<int> m_TransformFreeList;

        private NativeArray<MeshDrawRecord> m_Draws;
        private NativeArray<uint> m_DrawGenerations;
        private NativeList<int> m_DrawFreeList;

        private NativeArray<MeshSectionRecord> m_Sections;
        private NativeArray<uint> m_SectionGenerations;
        private NativeList<int> m_SectionFreeList;

        private NativeArray<MaterialDataRecord> m_Materials;
        private NativeArray<uint> m_MaterialGenerations;
        private NativeList<int> m_MaterialFreeList;

        private int m_InstanceCapacity;
        private int m_TransformCapacity;
        private int m_DrawCapacity;
        private int m_SectionCapacity;
        private int m_MaterialCapacity;

        private int m_InstanceHighWater;
        private int m_TransformHighWater;
        private int m_DrawHighWater;
        private int m_SectionHighWater;
        private int m_MaterialHighWater;

        private int m_LogicalInstanceCount;
        private int m_TransformCount;
        private int m_DrawCount;
        private int m_SectionCount;
        private int m_MaterialCount;

        private int m_TransformDirtyBegin;
        private int m_TransformDirtyEnd;

        private int m_BoundsDirtyBegin;
        private int m_BoundsDirtyEnd;

        private MeshSceneUpdate m_ActiveUpdate;
        private bool m_InTransaction;
        private readonly List<MeshSectionId> m_PendingSectionReclaims = new List<MeshSectionId>(8);
        private readonly List<MaterialDataId> m_PendingMaterialReclaims = new List<MaterialDataId>(8);
        private MeshSceneStateSnapshot m_TransactionStateSnapshot;
        private bool m_IsCreated;

        /// <summary>
        /// Process-unique scene identity for visibility interning (not a generation).
        /// </summary>
        public int SceneId { get; }

        public int StructuralRevision { get; private set; }
        public int ContentRevision { get; private set; }
        public int VisibilityRevision { get; private set; }

        public int LogicalInstanceCount => m_LogicalInstanceCount;
        public int TransformCount => m_TransformCount;
        public int DrawCount => m_DrawCount;
        public int SectionCount => m_SectionCount;
        public int MaterialCount => m_MaterialCount;

        public int InstanceCapacity => m_InstanceCapacity;
        public int TransformCapacity => m_TransformCapacity;
        public int DrawCapacity => m_DrawCapacity;
        public int InstanceHighWater => m_InstanceHighWater;
        public int TransformHighWater => m_TransformHighWater;
        public int DrawHighWater => m_DrawHighWater;
        public int SectionHighWater => m_SectionHighWater;
        public int MaterialHighWater => m_MaterialHighWater;

        public int InstanceFreeListLength => m_InstanceFreeList.IsCreated ? m_InstanceFreeList.Length : 0;
        public int TransformFreeListLength => m_TransformFreeList.IsCreated ? m_TransformFreeList.Length : 0;
        public int DrawFreeListLength => m_DrawFreeList.IsCreated ? m_DrawFreeList.Length : 0;
        public int SectionFreeListLength => m_SectionFreeList.IsCreated ? m_SectionFreeList.Length : 0;
        public int MaterialFreeListLength => m_MaterialFreeList.IsCreated ? m_MaterialFreeList.Length : 0;

        /// <summary>
        /// Steady-state invariant target is 1.0 (one transform per logical instance).
        /// </summary>
        public float MatrixDuplicateRatio => (float)m_TransformCount / math.max(1, m_LogicalInstanceCount);

        public bool HasTransformDirtyRange => m_TransformDirtyBegin <= m_TransformDirtyEnd;
        public int TransformDirtyBegin => m_TransformDirtyBegin;
        public int TransformDirtyEnd => m_TransformDirtyEnd;

        public bool HasBoundsDirtyRange => m_BoundsDirtyBegin <= m_BoundsDirtyEnd;
        public int BoundsDirtyBegin => m_BoundsDirtyBegin;
        public int BoundsDirtyEnd => m_BoundsDirtyEnd;

        public MeshScene(int initialCapacity = k_DefaultCapacity)
        {
            SceneId = System.Threading.Interlocked.Increment(ref s_NextSceneId);
            initialCapacity = math.max(16, initialCapacity);

            m_InstanceCapacity = initialCapacity;
            m_TransformCapacity = initialCapacity;
            m_DrawCapacity = initialCapacity * 2;
            m_SectionCapacity = initialCapacity * 2;
            m_MaterialCapacity = initialCapacity * 2;

            m_Instances = new NativeArray<MeshInstanceRecord>(m_InstanceCapacity, Allocator.Persistent);
            m_InstanceGenerations = new NativeArray<uint>(m_InstanceCapacity, Allocator.Persistent);
            m_InstanceFreeList = new NativeList<int>(m_InstanceCapacity, Allocator.Persistent);

            m_Transforms = new NativeArray<TransformRecord>(m_TransformCapacity, Allocator.Persistent);
            m_TransformGenerations = new NativeArray<uint>(m_TransformCapacity, Allocator.Persistent);
            m_TransformOwners = new NativeArray<int>(m_TransformCapacity, Allocator.Persistent);
            FillTransformOwners(-1, 0, m_TransformCapacity);
            m_TransformFreeList = new NativeList<int>(m_TransformCapacity, Allocator.Persistent);

            m_Draws = new NativeArray<MeshDrawRecord>(m_DrawCapacity, Allocator.Persistent);
            m_DrawGenerations = new NativeArray<uint>(m_DrawCapacity, Allocator.Persistent);
            m_DrawFreeList = new NativeList<int>(m_DrawCapacity, Allocator.Persistent);

            m_Sections = new NativeArray<MeshSectionRecord>(m_SectionCapacity, Allocator.Persistent);
            m_SectionGenerations = new NativeArray<uint>(m_SectionCapacity, Allocator.Persistent);
            m_SectionFreeList = new NativeList<int>(m_SectionCapacity, Allocator.Persistent);

            m_Materials = new NativeArray<MaterialDataRecord>(m_MaterialCapacity, Allocator.Persistent);
            m_MaterialGenerations = new NativeArray<uint>(m_MaterialCapacity, Allocator.Persistent);
            m_MaterialFreeList = new NativeList<int>(m_MaterialCapacity, Allocator.Persistent);

            ClearTransformDirtyRange();
            ClearBoundsDirtyRange();
            m_IsCreated = true;
            MeshPipelineDiagnostics.PublishFromScene(this);
        }

        public MeshSceneUpdate BeginUpdate()
        {
            if (!m_IsCreated)
            {
                throw new ObjectDisposedException(nameof(MeshScene));
            }

            if (m_ActiveUpdate != null)
            {
                throw new InvalidOperationException("MeshScene update transaction already active.");
            }

            m_InTransaction = true;
            m_TransactionStateSnapshot = SnapshotState();
            m_ActiveUpdate = new MeshSceneUpdate(this);
            return m_ActiveUpdate;
        }

        internal void EndUpdate(MeshSceneUpdate update, bool committed)
        {
            if (m_ActiveUpdate != update)
            {
                return;
            }

            m_ActiveUpdate = null;
            m_InTransaction = false;
            if (committed)
            {
                FlushPendingReclaims();
                MeshPipelineDiagnostics.PublishFromScene(this);
            }
            else
            {
                // Rollback restored refCounts via undo; drop deferred reclaim work.
                DiscardPendingReclaims();
            }
        }

        internal void RestoreTransactionState()
        {
            // Free abandoned zero-ref section/material slots created/orphaned by this transaction.
            // Undo ops own slot membership and live counts; do NOT truncate free-lists or rewind
            // highWater here — that would orphan slots Free* just returned to the free list.
            FlushPendingReclaims();
            RestoreRevisionsAndDirty(m_TransactionStateSnapshot);
        }

        private void RestoreRevisionsAndDirty(in MeshSceneStateSnapshot snapshot)
        {
            StructuralRevision = snapshot.StructuralRevision;
            ContentRevision = snapshot.ContentRevision;
            VisibilityRevision = snapshot.VisibilityRevision;
            m_TransformDirtyBegin = snapshot.TransformDirtyBegin;
            m_TransformDirtyEnd = snapshot.TransformDirtyEnd;
            m_BoundsDirtyBegin = snapshot.BoundsDirtyBegin;
            m_BoundsDirtyEnd = snapshot.BoundsDirtyEnd;
        }

        public void EnsureCapacity(int instanceCapacity, int transformCapacity, int drawCapacity)
        {
            EnsureInstanceCapacity(instanceCapacity);
            EnsureTransformCapacity(transformCapacity);
            EnsureDrawCapacity(drawCapacity);
        }

        public NativeArray<MeshInstanceRecord> GetInstances() => m_Instances;
        public NativeArray<uint> GetInstanceGenerations() => m_InstanceGenerations;
        public NativeArray<TransformRecord> GetTransforms() => m_Transforms;
        public NativeArray<uint> GetTransformGenerations() => m_TransformGenerations;
        public NativeArray<MeshDrawRecord> GetDraws() => m_Draws;
        public NativeArray<uint> GetDrawGenerations() => m_DrawGenerations;
        public NativeArray<MeshSectionRecord> GetSections() => m_Sections;
        public NativeArray<uint> GetSectionGenerations() => m_SectionGenerations;
        public NativeArray<MaterialDataRecord> GetMaterials() => m_Materials;
        public NativeArray<uint> GetMaterialGenerations() => m_MaterialGenerations;

        public bool TryGetInstance(MeshInstanceId id, out MeshInstanceRecord record)
        {
            record = default;
            if (!IsInstanceAlive(id))
            {
                return false;
            }

            record = m_Instances[(int)id.Index];
            return true;
        }

        public bool TryGetTransform(TransformId id, out TransformRecord record)
        {
            record = default;
            if (!IsTransformAlive(id))
            {
                return false;
            }

            record = m_Transforms[(int)id.Index];
            return true;
        }

        public bool TryGetDraw(MeshDrawId id, out MeshDrawRecord record)
        {
            record = default;
            if (!IsDrawAlive(id))
            {
                return false;
            }

            record = m_Draws[(int)id.Index];
            return true;
        }

        public bool TryGetSection(MeshSectionId id, out MeshSectionRecord record)
        {
            record = default;
            if (!IsSectionAlive(id))
            {
                return false;
            }

            record = m_Sections[(int)id.Index];
            return true;
        }

        public bool TryGetMaterial(MaterialDataId id, out MaterialDataRecord record)
        {
            record = default;
            if (!IsMaterialAlive(id))
            {
                return false;
            }

            record = m_Materials[(int)id.Index];
            return true;
        }

        public bool IsInstanceAlive(MeshInstanceId id)
        {
            return id.IsValid
                && id.Index < (uint)m_InstanceCapacity
                && m_InstanceGenerations[(int)id.Index] == id.Generation;
        }

        public bool IsTransformAlive(TransformId id)
        {
            return id.IsValid
                && id.Index < (uint)m_TransformCapacity
                && m_TransformGenerations[(int)id.Index] == id.Generation;
        }

        internal bool IsTransformOwned(TransformId id)
        {
            if (!IsTransformAlive(id))
            {
                return false;
            }

            int owner = m_TransformOwners[(int)id.Index];
            return owner >= 0 && IsInstanceSlotLive(owner);
        }

        public bool IsDrawAlive(MeshDrawId id)
        {
            return id.IsValid
                && id.Index < (uint)m_DrawCapacity
                && m_DrawGenerations[(int)id.Index] == id.Generation
                && m_Draws[(int)id.Index].instance.IsValid;
        }

        public bool IsSectionAlive(MeshSectionId id)
        {
            return id.IsValid
                && id.Index < (uint)m_SectionCapacity
                && m_SectionGenerations[(int)id.Index] == id.Generation
                && m_Sections[(int)id.Index].meshUnityId != k_TombstoneId;
        }

        public bool IsMaterialAlive(MaterialDataId id)
        {
            return id.IsValid
                && id.Index < (uint)m_MaterialCapacity
                && m_MaterialGenerations[(int)id.Index] == id.Generation
                && m_Materials[(int)id.Index].materialUnityId != k_TombstoneId;
        }

        public bool IsDrawSlotLive(int index)
        {
            return index >= 0
                && index < m_DrawHighWater
                && m_Draws[index].instance.IsValid;
        }

        public bool IsInstanceSlotLive(int index)
        {
            return index >= 0
                && index < m_InstanceHighWater
                && m_Instances[index].transform.IsValid;
        }

        internal MeshSceneRevisionSnapshot SnapshotRevisions()
        {
            return new MeshSceneRevisionSnapshot
            {
                StructuralRevision = StructuralRevision,
                ContentRevision = ContentRevision,
                VisibilityRevision = VisibilityRevision
            };
        }

        internal void RestoreRevisions(in MeshSceneRevisionSnapshot snapshot)
        {
            StructuralRevision = snapshot.StructuralRevision;
            ContentRevision = snapshot.ContentRevision;
            VisibilityRevision = snapshot.VisibilityRevision;
        }

        internal MeshSceneStateSnapshot SnapshotState()
        {
            return new MeshSceneStateSnapshot
            {
                StructuralRevision = StructuralRevision,
                ContentRevision = ContentRevision,
                VisibilityRevision = VisibilityRevision,
                TransformDirtyBegin = m_TransformDirtyBegin,
                TransformDirtyEnd = m_TransformDirtyEnd,
                BoundsDirtyBegin = m_BoundsDirtyBegin,
                BoundsDirtyEnd = m_BoundsDirtyEnd
            };
        }

        internal TransformId AllocTransform(in TransformRecord record)
        {
            EnsureTransformCapacity(m_TransformHighWater + 1);
            int index = AllocIndexedSlot(ref m_TransformFreeList, ref m_TransformHighWater, ref m_TransformCount, m_TransformGenerations);
            m_Transforms[index] = record;
            MarkTransformDirty(index);
            StructuralRevision++;
            ContentRevision++;
            return new TransformId((uint)index, m_TransformGenerations[index]);
        }

        internal void WriteTransform(TransformId id, in TransformRecord record)
        {
            if (!IsTransformAlive(id))
            {
                return;
            }

            m_Transforms[(int)id.Index] = record;
            MarkTransformDirty((int)id.Index);
            ContentRevision++;
        }

        internal void FreeTransform(TransformId id)
        {
            if (!IsTransformAlive(id))
            {
                return;
            }

            FreeTransformSlot((int)id.Index);
            StructuralRevision++;
            ContentRevision++;
        }

        internal void RestoreTransform(TransformId id, in TransformRecord record)
        {
            int index = (int)id.Index;
            EnsureTransformCapacity(index + 1);
            if (IsTransformAlive(id))
            {
                m_Transforms[index] = record;
                MarkTransformDirty(index);
                return;
            }

            RemoveFromFreeList(ref m_TransformFreeList, index);
            m_TransformGenerations[index] = id.Generation;
            m_Transforms[index] = record;
            m_TransformHighWater = math.max(m_TransformHighWater, index + 1);
            m_TransformCount += 1;
            MarkTransformDirty(index);
        }

        internal MeshInstanceId AllocInstance(in MeshInstanceRecord record)
        {
            if (!IsTransformAlive(record.transform))
            {
                throw new ArgumentException("CreateInstance requires a live TransformId.", nameof(record));
            }

            int transformIndex = (int)record.transform.Index;
            int existingOwner = m_TransformOwners[transformIndex];
            if (existingOwner >= 0 && IsInstanceSlotLive(existingOwner))
            {
                throw new ArgumentException("TransformId already owned by another MeshInstance", nameof(record));
            }

            EnsureInstanceCapacity(m_InstanceHighWater + 1);
            int index = AllocIndexedSlot(ref m_InstanceFreeList, ref m_InstanceHighWater, ref m_LogicalInstanceCount, m_InstanceGenerations);

            MeshInstanceRecord stored = record;
            stored.drawStart = -1;
            stored.drawCount = 0;
            m_Instances[index] = stored;
            m_TransformOwners[transformIndex] = index;
            MarkBoundsDirty(index);

            StructuralRevision++;
            VisibilityRevision++;
            return new MeshInstanceId((uint)index, m_InstanceGenerations[index]);
        }

        internal void FreeInstance(MeshInstanceId id)
        {
            if (!IsInstanceAlive(id))
            {
                return;
            }

            FreeInstanceSlot((int)id.Index);
            StructuralRevision++;
            VisibilityRevision++;
        }

        internal void RestoreInstance(MeshInstanceId id, in MeshInstanceRecord record)
        {
            int index = (int)id.Index;
            EnsureInstanceCapacity(index + 1);
            if (IsInstanceAlive(id))
            {
                m_Instances[index] = record;
                AssignTransformOwner(record.transform, index);
                MarkBoundsDirty(index);
                return;
            }

            RemoveFromFreeList(ref m_InstanceFreeList, index);
            m_InstanceGenerations[index] = id.Generation;
            m_Instances[index] = record;
            m_InstanceHighWater = math.max(m_InstanceHighWater, index + 1);
            m_LogicalInstanceCount += 1;
            AssignTransformOwner(record.transform, index);
            MarkBoundsDirty(index);
        }

        internal void SetInstanceBounds(MeshInstanceId id, in FBound worldBounds)
        {
            if (!IsInstanceAlive(id))
            {
                return;
            }

            MeshInstanceRecord record = m_Instances[(int)id.Index];
            record.worldBounds = worldBounds;
            m_Instances[(int)id.Index] = record;
            MarkBoundsDirty((int)id.Index);
            ContentRevision++;
            VisibilityRevision++;
        }

        internal void SetInstanceFlags(MeshInstanceId id, EMeshInstanceFlags flags)
        {
            if (!IsInstanceAlive(id))
            {
                return;
            }

            MeshInstanceRecord record = m_Instances[(int)id.Index];
            record.flags = flags;
            m_Instances[(int)id.Index] = record;
            VisibilityRevision++;
        }

        internal void SetInstanceRendering(
            MeshInstanceId id,
            uint renderingLayerMask,
            EMotionType motionType,
            ECastShadowMethod castShadow)
        {
            if (!IsInstanceAlive(id))
            {
                return;
            }

            MeshInstanceRecord record = m_Instances[(int)id.Index];
            record.renderingLayerMask = renderingLayerMask;
            record.motionType = motionType;
            record.castShadow = castShadow;
            m_Instances[(int)id.Index] = record;
            ContentRevision++;
            VisibilityRevision++;
        }

        internal void WriteInstanceRecord(MeshInstanceId id, in MeshInstanceRecord record)
        {
            if (!IsInstanceAlive(id))
            {
                return;
            }

            m_Instances[(int)id.Index] = record;
            MarkBoundsDirty((int)id.Index);
            ContentRevision++;
            VisibilityRevision++;
        }

        /// <summary>
        /// Allocate a draw slot (free-list first). drawStart/drawCount are diagnostic only;
        /// Filter matches draws by draw.instance.
        /// </summary>
        internal MeshDrawId AllocDrawForInstance(MeshInstanceId instanceId, in MeshDrawRecord drawRecord)
        {
            if (!IsInstanceAlive(instanceId))
            {
                return MeshDrawId.Invalid;
            }

            EnsureDrawCapacity(m_DrawHighWater + 1);
            int drawIndex = AllocIndexedSlot(ref m_DrawFreeList, ref m_DrawHighWater, ref m_DrawCount, m_DrawGenerations);

            MeshInstanceRecord instance = m_Instances[(int)instanceId.Index];
            if (instance.drawStart < 0)
            {
                instance.drawStart = drawIndex;
            }

            instance.drawCount += 1;

            MeshDrawRecord stored = drawRecord;
            stored.instance = instanceId;
            m_Draws[drawIndex] = stored;
            m_Instances[(int)instanceId.Index] = instance;

            AddSectionRef(stored.section);
            AddMaterialRef(stored.material);

            StructuralRevision++;
            return new MeshDrawId((uint)drawIndex, m_DrawGenerations[drawIndex]);
        }

        internal void FreeDraw(MeshDrawId id)
        {
            if (!IsDrawAlive(id))
            {
                return;
            }

            MeshDrawRecord draw = m_Draws[(int)id.Index];
            if (IsInstanceAlive(draw.instance))
            {
                MeshInstanceRecord instance = m_Instances[(int)draw.instance.Index];
                instance.drawCount = math.max(0, instance.drawCount - 1);
                if (instance.drawCount == 0)
                {
                    instance.drawStart = -1;
                }

                m_Instances[(int)draw.instance.Index] = instance;
            }

            FreeDrawSlot((int)id.Index);
            StructuralRevision++;
        }

        internal void RestoreDraw(MeshDrawId id, in MeshDrawRecord record)
        {
            int index = (int)id.Index;
            EnsureDrawCapacity(index + 1);
            if (IsDrawAlive(id))
            {
                m_Draws[index] = record;
                return;
            }

            RemoveFromFreeList(ref m_DrawFreeList, index);
            m_DrawGenerations[index] = id.Generation;
            m_Draws[index] = record;
            m_DrawHighWater = math.max(m_DrawHighWater, index + 1);
            m_DrawCount += 1;
            // Section/material refCounts are restored by companion RestoreSection/Material
            // (or Restore*Record) undo entries from RemoveInstance. Do not AddRef here.
        }

        internal void SetDrawMaterial(MeshDrawId drawId, int materialUnityId, int renderQueue, MaterialDataId materialId)
        {
            if (!IsDrawAlive(drawId))
            {
                return;
            }

            MeshDrawRecord draw = m_Draws[(int)drawId.Index];
            if (!draw.material.Equals(materialId))
            {
                ReleaseMaterialRef(draw.material);
                AddMaterialRef(materialId);
            }

            draw.materialUnityId = materialUnityId;
            draw.renderQueue = renderQueue;
            draw.material = materialId;
            m_Draws[(int)drawId.Index] = draw;
            ContentRevision++;
        }

        internal void SetDrawPriority(MeshDrawId drawId, int priority)
        {
            if (!IsDrawAlive(drawId))
            {
                return;
            }

            MeshDrawRecord draw = m_Draws[(int)drawId.Index];
            draw.priority = priority;
            m_Draws[(int)drawId.Index] = draw;
            ContentRevision++;
        }

        internal void SetDrawEligibility(MeshDrawId drawId, EPassEligibility eligibility)
        {
            if (!IsDrawAlive(drawId))
            {
                return;
            }

            MeshDrawRecord draw = m_Draws[(int)drawId.Index];
            draw.eligibility = eligibility;
            m_Draws[(int)drawId.Index] = draw;
            ContentRevision++;
        }

        internal void WriteDrawRecord(MeshDrawId drawId, in MeshDrawRecord record)
        {
            if (!IsDrawAlive(drawId))
            {
                return;
            }

            MeshDrawRecord previous = m_Draws[(int)drawId.Index];
            if (!previous.material.Equals(record.material))
            {
                ReleaseMaterialRef(previous.material);
                AddMaterialRef(record.material);
            }

            if (!previous.section.Equals(record.section))
            {
                ReleaseSectionRef(previous.section);
                AddSectionRef(record.section);
            }

            m_Draws[(int)drawId.Index] = record;
            ContentRevision++;
        }

        internal MaterialDataId AllocOrUpdateMaterial(int materialUnityId, int renderQueue, out MaterialDataRecord previous, out bool created, out bool revised)
        {
            previous = default;
            created = false;
            revised = false;

            for (int i = 0; i < m_MaterialHighWater; ++i)
            {
                MaterialDataRecord existing = m_Materials[i];
                if (existing.materialUnityId == k_TombstoneId)
                {
                    continue;
                }

                if (existing.materialUnityId == materialUnityId)
                {
                    if (existing.renderQueue != renderQueue)
                    {
                        previous = existing;
                        uint oldRevision = existing.revision;
                        existing.renderQueue = renderQueue;
                        existing.revision += 1;
                        m_Materials[i] = existing;
                        ContentRevision++;
                        revised = true;
                        MeshPassDrawCache.NotifyMaterialRevision(materialUnityId, oldRevision);
                    }

                    return new MaterialDataId((uint)i, m_MaterialGenerations[i]);
                }
            }

            EnsureMaterialCapacity(m_MaterialHighWater + 1);
            int index = AllocIndexedSlot(ref m_MaterialFreeList, ref m_MaterialHighWater, ref m_MaterialCount, m_MaterialGenerations);
            m_Materials[index] = new MaterialDataRecord
            {
                materialUnityId = materialUnityId,
                renderQueue = renderQueue,
                revision = 1,
                refCount = 0
            };
            created = true;
            StructuralRevision++;
            return new MaterialDataId((uint)index, m_MaterialGenerations[index]);
        }

        internal void FreeMaterial(MaterialDataId id)
        {
            if (!IsMaterialAlive(id))
            {
                return;
            }

            FreeMaterialSlot((int)id.Index);
            StructuralRevision++;
        }

        internal void RestoreMaterial(MaterialDataId id, in MaterialDataRecord record)
        {
            int index = (int)id.Index;
            EnsureMaterialCapacity(index + 1);
            // Deferred reclaim leaves the slot alive at refCount 0; restore record only.
            if (IsMaterialAlive(id))
            {
                m_Materials[index] = record;
                return;
            }

            RemoveFromFreeList(ref m_MaterialFreeList, index);
            m_MaterialGenerations[index] = id.Generation;
            m_Materials[index] = record;
            m_MaterialHighWater = math.max(m_MaterialHighWater, index + 1);
            m_MaterialCount += 1;
        }

        internal void WriteMaterialRecord(MaterialDataId id, in MaterialDataRecord record)
        {
            if (!IsMaterialAlive(id))
            {
                return;
            }

            m_Materials[(int)id.Index] = record;
            ContentRevision++;
        }

        internal MeshSectionId AllocOrUpdateSection(
            int meshUnityId,
            int sectionIndex,
            EGeometrySourceKind geometrySource,
            uint geometryRevision,
            out MeshSectionRecord previous,
            out bool created,
            out bool revised)
        {
            previous = default;
            created = false;
            revised = false;

            for (int i = 0; i < m_SectionHighWater; ++i)
            {
                MeshSectionRecord existing = m_Sections[i];
                if (existing.meshUnityId == k_TombstoneId)
                {
                    continue;
                }

                if (existing.meshUnityId == meshUnityId && existing.sectionIndex == sectionIndex)
                {
                    if (existing.geometrySource != geometrySource || existing.geometryRevision != geometryRevision)
                    {
                        previous = existing;
                        existing.geometrySource = geometrySource;
                        existing.geometryRevision = geometryRevision;
                        existing.revision += 1;
                        m_Sections[i] = existing;
                        ContentRevision++;
                        revised = true;
                    }

                    return new MeshSectionId((uint)i, m_SectionGenerations[i]);
                }
            }

            EnsureSectionCapacity(m_SectionHighWater + 1);
            int index = AllocIndexedSlot(ref m_SectionFreeList, ref m_SectionHighWater, ref m_SectionCount, m_SectionGenerations);
            m_Sections[index] = new MeshSectionRecord
            {
                meshUnityId = meshUnityId,
                sectionIndex = sectionIndex,
                geometrySource = geometrySource,
                refCount = 0,
                revision = 1,
                geometryRevision = geometryRevision
            };
            created = true;
            StructuralRevision++;
            return new MeshSectionId((uint)index, m_SectionGenerations[index]);
        }

        internal void FreeSection(MeshSectionId id)
        {
            if (!IsSectionAlive(id))
            {
                return;
            }

            FreeSectionSlot((int)id.Index);
            StructuralRevision++;
        }

        internal void RestoreSection(MeshSectionId id, in MeshSectionRecord record)
        {
            int index = (int)id.Index;
            EnsureSectionCapacity(index + 1);
            // Deferred reclaim leaves the slot alive at refCount 0; restore record only.
            if (IsSectionAlive(id))
            {
                m_Sections[index] = record;
                return;
            }

            RemoveFromFreeList(ref m_SectionFreeList, index);
            m_SectionGenerations[index] = id.Generation;
            m_Sections[index] = record;
            m_SectionHighWater = math.max(m_SectionHighWater, index + 1);
            m_SectionCount += 1;
        }

        internal void WriteSectionRecord(MeshSectionId id, in MeshSectionRecord record)
        {
            if (!IsSectionAlive(id))
            {
                return;
            }

            m_Sections[(int)id.Index] = record;
            ContentRevision++;
        }

        internal void RemoveInstanceInternal(MeshInstanceId instanceId)
        {
            if (!IsInstanceAlive(instanceId))
            {
                return;
            }

            MeshInstanceRecord instance = m_Instances[(int)instanceId.Index];

            for (int i = 0; i < m_DrawHighWater; ++i)
            {
                if (!IsDrawSlotLive(i))
                {
                    continue;
                }

                if (m_Draws[i].instance.Equals(instanceId))
                {
                    FreeDrawSlot(i);
                }
            }

            // Clear owner before FreeTransform so unconditional transform release stays safe.
            ClearTransformOwnerForInstance((int)instanceId.Index);
            if (IsTransformAlive(instance.transform))
            {
                FreeTransformSlot((int)instance.transform.Index);
            }

            FreeInstanceSlot((int)instanceId.Index);
            StructuralRevision++;
            VisibilityRevision++;
        }

        public void ClearTransformDirtyRange()
        {
            m_TransformDirtyBegin = int.MaxValue;
            m_TransformDirtyEnd = -1;
        }

        public void ClearBoundsDirtyRange()
        {
            m_BoundsDirtyBegin = int.MaxValue;
            m_BoundsDirtyEnd = -1;
        }

        private void MarkTransformDirty(int index)
        {
            m_TransformDirtyBegin = math.min(m_TransformDirtyBegin, index);
            m_TransformDirtyEnd = math.max(m_TransformDirtyEnd, index);
        }

        private void MarkBoundsDirty(int instanceIndex)
        {
            m_BoundsDirtyBegin = math.min(m_BoundsDirtyBegin, instanceIndex);
            m_BoundsDirtyEnd = math.max(m_BoundsDirtyEnd, instanceIndex);
        }

        private void AddSectionRef(MeshSectionId id)
        {
            if (!IsSectionAlive(id))
            {
                return;
            }

            MeshSectionRecord record = m_Sections[(int)id.Index];
            record.refCount += 1;
            m_Sections[(int)id.Index] = record;
        }

        private void ReleaseSectionRef(MeshSectionId id)
        {
            if (!IsSectionAlive(id))
            {
                return;
            }

            MeshSectionRecord record = m_Sections[(int)id.Index];
            record.refCount = math.max(0, record.refCount - 1);
            m_Sections[(int)id.Index] = record;
            if (record.refCount == 0)
            {
                if (m_InTransaction)
                {
                    m_PendingSectionReclaims.Add(id);
                }
                else
                {
                    FreeSectionSlot((int)id.Index);
                }
            }
        }

        private void AddMaterialRef(MaterialDataId id)
        {
            if (!IsMaterialAlive(id))
            {
                return;
            }

            MaterialDataRecord record = m_Materials[(int)id.Index];
            record.refCount += 1;
            m_Materials[(int)id.Index] = record;
        }

        private void ReleaseMaterialRef(MaterialDataId id)
        {
            if (!IsMaterialAlive(id))
            {
                return;
            }

            MaterialDataRecord record = m_Materials[(int)id.Index];
            record.refCount = math.max(0, record.refCount - 1);
            m_Materials[(int)id.Index] = record;
            if (record.refCount == 0)
            {
                if (m_InTransaction)
                {
                    m_PendingMaterialReclaims.Add(id);
                }
                else
                {
                    FreeMaterialSlot((int)id.Index);
                }
            }
        }

        private void FlushPendingReclaims()
        {
            for (int i = 0; i < m_PendingSectionReclaims.Count; ++i)
            {
                MeshSectionId id = m_PendingSectionReclaims[i];
                if (IsSectionAlive(id) && m_Sections[(int)id.Index].refCount == 0)
                {
                    FreeSectionSlot((int)id.Index);
                }
            }

            for (int i = 0; i < m_PendingMaterialReclaims.Count; ++i)
            {
                MaterialDataId id = m_PendingMaterialReclaims[i];
                if (IsMaterialAlive(id) && m_Materials[(int)id.Index].refCount == 0)
                {
                    FreeMaterialSlot((int)id.Index);
                }
            }

            DiscardPendingReclaims();
        }

        private void DiscardPendingReclaims()
        {
            m_PendingSectionReclaims.Clear();
            m_PendingMaterialReclaims.Clear();
        }

        private static int AllocIndexedSlot(ref NativeList<int> freeList, ref int highWater, ref int liveCount, NativeArray<uint> generations)
        {
            int index;
            if (freeList.Length > 0)
            {
                index = freeList[freeList.Length - 1];
                freeList.RemoveAtSwapBack(freeList.Length - 1);
                if (generations[index] == 0)
                {
                    generations[index] = 1;
                }
            }
            else
            {
                index = highWater;
                highWater += 1;
                generations[index] = 1;
            }

            liveCount += 1;
            return index;
        }

        private void FreeInstanceSlot(int index)
        {
            ClearTransformOwnerForInstance(index);
            m_InstanceGenerations[index] = NextGeneration(m_InstanceGenerations[index]);
            m_Instances[index] = default;
            m_InstanceFreeList.Add(index);
            m_LogicalInstanceCount = math.max(0, m_LogicalInstanceCount - 1);
        }

        private void FreeTransformSlot(int index)
        {
            m_TransformOwners[index] = -1;
            m_TransformGenerations[index] = NextGeneration(m_TransformGenerations[index]);
            m_Transforms[index] = default;
            m_TransformFreeList.Add(index);
            m_TransformCount = math.max(0, m_TransformCount - 1);
            MarkTransformDirty(index);
        }

        private void ClearTransformOwnerForInstance(int instanceIndex)
        {
            if (instanceIndex < 0 || instanceIndex >= m_InstanceCapacity)
            {
                return;
            }

            TransformId transform = m_Instances[instanceIndex].transform;
            if (!transform.IsValid || transform.Index >= (uint)m_TransformCapacity)
            {
                return;
            }

            int transformIndex = (int)transform.Index;
            if (m_TransformOwners[transformIndex] == instanceIndex)
            {
                m_TransformOwners[transformIndex] = -1;
            }
        }

        private void AssignTransformOwner(TransformId transform, int instanceIndex)
        {
            if (!IsTransformAlive(transform))
            {
                return;
            }

            // Rollback restores transform then instance; overwrite keeps owner map consistent.
            m_TransformOwners[(int)transform.Index] = instanceIndex;
        }

        private void FillTransformOwners(int value, int begin, int end)
        {
            for (int i = begin; i < end; ++i)
            {
                m_TransformOwners[i] = value;
            }
        }

        private void FreeDrawSlot(int index)
        {
            if (index < 0 || index >= m_DrawCapacity || !m_Draws[index].instance.IsValid)
            {
                return;
            }

            MeshDrawRecord draw = m_Draws[index];
            ReleaseSectionRef(draw.section);
            ReleaseMaterialRef(draw.material);

            m_DrawGenerations[index] = NextGeneration(m_DrawGenerations[index]);
            m_Draws[index] = default;
            m_DrawFreeList.Add(index);
            m_DrawCount = math.max(0, m_DrawCount - 1);
        }

        private void FreeSectionSlot(int index)
        {
            m_SectionGenerations[index] = NextGeneration(m_SectionGenerations[index]);
            m_Sections[index] = new MeshSectionRecord
            {
                meshUnityId = k_TombstoneId,
                sectionIndex = 0,
                geometrySource = EGeometrySourceKind.IndexedMesh,
                refCount = 0,
                revision = 0
            };
            m_SectionFreeList.Add(index);
            m_SectionCount = math.max(0, m_SectionCount - 1);
        }

        private void FreeMaterialSlot(int index)
        {
            m_MaterialGenerations[index] = NextGeneration(m_MaterialGenerations[index]);
            m_Materials[index] = new MaterialDataRecord
            {
                materialUnityId = k_TombstoneId,
                renderQueue = 0,
                revision = 0,
                refCount = 0
            };
            m_MaterialFreeList.Add(index);
            m_MaterialCount = math.max(0, m_MaterialCount - 1);
        }

        private static void RemoveFromFreeList(ref NativeList<int> freeList, int index)
        {
            for (int i = 0; i < freeList.Length; ++i)
            {
                if (freeList[i] == index)
                {
                    freeList.RemoveAtSwapBack(i);
                    return;
                }
            }
        }

        private static uint NextGeneration(uint generation)
        {
            uint next = generation + 1;
            return next == 0 ? 1u : next;
        }

        private void EnsureInstanceCapacity(int needed)
        {
            if (needed <= m_InstanceCapacity) return;
            int newCapacity = GrowCapacity(m_InstanceCapacity, needed);
            ResizeArray(ref m_Instances, newCapacity);
            ResizeArray(ref m_InstanceGenerations, newCapacity);
            m_InstanceCapacity = newCapacity;
        }

        private void EnsureTransformCapacity(int needed)
        {
            if (needed <= m_TransformCapacity) return;
            int oldCapacity = m_TransformCapacity;
            int newCapacity = GrowCapacity(m_TransformCapacity, needed);
            ResizeArray(ref m_Transforms, newCapacity);
            ResizeArray(ref m_TransformGenerations, newCapacity);
            ResizeArray(ref m_TransformOwners, newCapacity);
            FillTransformOwners(-1, oldCapacity, newCapacity);
            m_TransformCapacity = newCapacity;
        }

        private void EnsureDrawCapacity(int needed)
        {
            if (needed <= m_DrawCapacity) return;
            int newCapacity = GrowCapacity(m_DrawCapacity, needed);
            ResizeArray(ref m_Draws, newCapacity);
            ResizeArray(ref m_DrawGenerations, newCapacity);
            m_DrawCapacity = newCapacity;
        }

        private void EnsureSectionCapacity(int needed)
        {
            if (needed <= m_SectionCapacity) return;
            int newCapacity = GrowCapacity(m_SectionCapacity, needed);
            ResizeArray(ref m_Sections, newCapacity);
            ResizeArray(ref m_SectionGenerations, newCapacity);
            m_SectionCapacity = newCapacity;
        }

        private void EnsureMaterialCapacity(int needed)
        {
            if (needed <= m_MaterialCapacity) return;
            int newCapacity = GrowCapacity(m_MaterialCapacity, needed);
            ResizeArray(ref m_Materials, newCapacity);
            ResizeArray(ref m_MaterialGenerations, newCapacity);
            m_MaterialCapacity = newCapacity;
        }

        private static int GrowCapacity(int current, int needed)
        {
            int capacity = math.max(16, current);
            while (capacity < needed)
            {
                capacity *= 2;
            }

            return capacity;
        }

        private static void ResizeArray<T>(ref NativeArray<T> array, int newCapacity) where T : struct
        {
            var next = new NativeArray<T>(newCapacity, Allocator.Persistent);
            if (array.IsCreated)
            {
                NativeArray<T>.Copy(array, next, array.Length);
                array.Dispose();
            }

            array = next;
        }

        public void Dispose()
        {
            if (!m_IsCreated)
            {
                return;
            }

            m_ActiveUpdate?.Rollback();
            m_ActiveUpdate = null;
            m_InTransaction = false;
            DiscardPendingReclaims();

            if (m_Instances.IsCreated) m_Instances.Dispose();
            if (m_InstanceGenerations.IsCreated) m_InstanceGenerations.Dispose();
            if (m_InstanceFreeList.IsCreated) m_InstanceFreeList.Dispose();

            if (m_Transforms.IsCreated) m_Transforms.Dispose();
            if (m_TransformGenerations.IsCreated) m_TransformGenerations.Dispose();
            if (m_TransformOwners.IsCreated) m_TransformOwners.Dispose();
            if (m_TransformFreeList.IsCreated) m_TransformFreeList.Dispose();

            if (m_Draws.IsCreated) m_Draws.Dispose();
            if (m_DrawGenerations.IsCreated) m_DrawGenerations.Dispose();
            if (m_DrawFreeList.IsCreated) m_DrawFreeList.Dispose();

            if (m_Sections.IsCreated) m_Sections.Dispose();
            if (m_SectionGenerations.IsCreated) m_SectionGenerations.Dispose();
            if (m_SectionFreeList.IsCreated) m_SectionFreeList.Dispose();

            if (m_Materials.IsCreated) m_Materials.Dispose();
            if (m_MaterialGenerations.IsCreated) m_MaterialGenerations.Dispose();
            if (m_MaterialFreeList.IsCreated) m_MaterialFreeList.Dispose();

            m_IsCreated = false;
        }
    }
}
