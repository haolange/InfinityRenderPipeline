using System;
using System.Collections.Generic;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    public enum EMeshSceneUndoOp : byte
    {
        FreeTransform = 0,
        RestoreTransform = 1,
        FreeInstance = 2,
        RestoreInstance = 3,
        FreeDraw = 4,
        RestoreDraw = 5,
        FreeSection = 6,
        RestoreSection = 7,
        FreeMaterial = 8,
        RestoreMaterial = 9,
        RestoreRevisions = 10,
        RestoreTransformRecord = 11,
        RestoreInstanceRecord = 12,
        RestoreDrawRecord = 13,
        RestoreSectionRecord = 14,
        RestoreMaterialRecord = 15
    }

    public struct MeshSceneUndoEntry
    {
        public EMeshSceneUndoOp op;
        public uint index;
        public uint generation;
        public TransformRecord transformRecord;
        public MeshInstanceRecord instanceRecord;
        public MeshDrawRecord drawRecord;
        public MeshSectionRecord sectionRecord;
        public MaterialDataRecord materialRecord;
        public MeshSceneRevisionSnapshot revisions;
    }

    /// <summary>
    /// Structural transaction against MeshScene. Dispose without Commit rolls back via undo log.
    /// </summary>
    public sealed class MeshSceneUpdate : IDisposable
    {
        private readonly MeshScene m_Scene;
        private readonly List<MeshSceneUndoEntry> m_UndoLog = new List<MeshSceneUndoEntry>(32);
        private readonly MeshSceneRevisionSnapshot m_RevisionSnapshot;
        private bool m_Committed;
        private bool m_Disposed;

        internal MeshSceneUpdate(MeshScene scene)
        {
            m_Scene = scene;
            m_RevisionSnapshot = scene.SnapshotRevisions();
            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreRevisions,
                revisions = m_RevisionSnapshot
            });
        }

        public TransformId CreateTransform(in float4x4 current)
        {
            return CreateTransform(current, current);
        }

        public TransformId CreateTransform(in float4x4 current, in float4x4 previous)
        {
            ThrowIfClosed();
            TransformId id = m_Scene.AllocTransform(new TransformRecord
            {
                current = current,
                previous = previous
            });
            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.FreeTransform,
                index = id.Index,
                generation = id.Generation
            });
            return id;
        }

        public void SetTransform(TransformId id, in float4x4 current)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetTransform(id, out TransformRecord record))
            {
                return;
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreTransformRecord,
                index = id.Index,
                generation = id.Generation,
                transformRecord = record
            });

            record.previous = record.current;
            record.current = current;
            m_Scene.WriteTransform(id, record);
        }

        public void SetTransform(TransformId id, in float4x4 current, in float4x4 previous)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetTransform(id, out TransformRecord previousRecord))
            {
                return;
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreTransformRecord,
                index = id.Index,
                generation = id.Generation,
                transformRecord = previousRecord
            });

            m_Scene.WriteTransform(id, new TransformRecord
            {
                current = current,
                previous = previous
            });
        }

        public MeshInstanceId CreateInstance(
            TransformId transform,
            in FBound worldBounds,
            int layerMask,
            uint renderingLayerMask,
            EMeshInstanceFlags flags,
            EMotionType motionType,
            ECastShadowMethod castShadow,
            EGeometrySourceKind geometrySource = EGeometrySourceKind.IndexedMesh,
            uint deformationDataId = 0)
        {
            ThrowIfClosed();

            if (!m_Scene.IsTransformAlive(transform))
            {
                throw new ArgumentException("CreateInstance requires a live TransformId.", nameof(transform));
            }

            if (m_Scene.IsTransformOwned(transform))
            {
                throw new ArgumentException("TransformId already owned by another MeshInstance", nameof(transform));
            }

            MeshInstanceId id = m_Scene.AllocInstance(new MeshInstanceRecord
            {
                transform = transform,
                worldBounds = worldBounds,
                layerMask = layerMask,
                renderingLayerMask = renderingLayerMask,
                flags = flags,
                motionType = motionType,
                castShadow = castShadow,
                drawStart = -1,
                drawCount = 0,
                geometrySource = geometrySource,
                deformationDataId = deformationDataId
            });

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.FreeInstance,
                index = id.Index,
                generation = id.Generation
            });
            return id;
        }

        public void SetBounds(MeshInstanceId id, in FBound worldBounds)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetInstance(id, out MeshInstanceRecord previous))
            {
                return;
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreInstanceRecord,
                index = id.Index,
                generation = id.Generation,
                instanceRecord = previous
            });

            m_Scene.SetInstanceBounds(id, worldBounds);
        }

        public void SetInstanceFlags(MeshInstanceId id, EMeshInstanceFlags flags)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetInstance(id, out MeshInstanceRecord previous))
            {
                return;
            }

            if (previous.flags == flags)
            {
                return;
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreInstanceRecord,
                index = id.Index,
                generation = id.Generation,
                instanceRecord = previous
            });

            m_Scene.SetInstanceFlags(id, flags);
        }

        public void SetInstanceRendering(
            MeshInstanceId id,
            uint renderingLayerMask,
            EMotionType motionType,
            ECastShadowMethod castShadow)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetInstance(id, out MeshInstanceRecord previous))
            {
                return;
            }

            if (previous.renderingLayerMask == renderingLayerMask
                && previous.motionType == motionType
                && previous.castShadow == castShadow)
            {
                return;
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreInstanceRecord,
                index = id.Index,
                generation = id.Generation,
                instanceRecord = previous
            });

            m_Scene.SetInstanceRendering(id, renderingLayerMask, motionType, castShadow);
        }

        public MeshDrawId CreateDraw(
            MeshInstanceId instance,
            int meshUnityId,
            int sectionIndex,
            int materialUnityId,
            EPassEligibility eligibility,
            int renderQueue,
            int priority,
            EGeometrySourceKind geometrySource = EGeometrySourceKind.IndexedMesh,
            uint geometryRevision = 0,
            uint staticFlags = 0)
        {
            ThrowIfClosed();

            MeshSectionId sectionId = m_Scene.AllocOrUpdateSection(
                meshUnityId, sectionIndex, geometrySource, geometryRevision,
                out MeshSectionRecord previousSection, out bool sectionCreated, out bool sectionRevised);

            MaterialDataId materialId = m_Scene.AllocOrUpdateMaterial(
                materialUnityId, renderQueue,
                out MaterialDataRecord previousMaterial, out bool materialCreated, out bool materialRevised);

            MeshDrawId drawId = m_Scene.AllocDrawForInstance(instance, new MeshDrawRecord
            {
                instance = instance,
                section = sectionId,
                material = materialId,
                eligibility = eligibility,
                renderQueue = renderQueue,
                priority = priority,
                meshUnityId = meshUnityId,
                materialUnityId = materialUnityId,
                sectionIndex = sectionIndex,
                staticFlags = staticFlags
            });

            if (!drawId.IsValid)
            {
                if (sectionCreated)
                {
                    m_Scene.FreeSection(sectionId);
                }
                else if (sectionRevised)
                {
                    m_Scene.WriteSectionRecord(sectionId, previousSection);
                }

                if (materialCreated)
                {
                    m_Scene.FreeMaterial(materialId);
                }
                else if (materialRevised)
                {
                    m_Scene.WriteMaterialRecord(materialId, previousMaterial);
                }

                return MeshDrawId.Invalid;
            }

            // FreeDraw releases section/material refs (and recycles at zero).
            // Revision restores are recorded only after the draw exists.
            if (sectionRevised)
            {
                Push(new MeshSceneUndoEntry
                {
                    op = EMeshSceneUndoOp.RestoreSectionRecord,
                    index = sectionId.Index,
                    generation = sectionId.Generation,
                    sectionRecord = previousSection
                });
            }

            if (materialRevised)
            {
                Push(new MeshSceneUndoEntry
                {
                    op = EMeshSceneUndoOp.RestoreMaterialRecord,
                    index = materialId.Index,
                    generation = materialId.Generation,
                    materialRecord = previousMaterial
                });
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.FreeDraw,
                index = drawId.Index,
                generation = drawId.Generation
            });
            return drawId;
        }

        public void SetMaterial(MeshDrawId drawId, int materialUnityId, int renderQueue)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetDraw(drawId, out MeshDrawRecord previousDraw))
            {
                return;
            }

            MaterialDataId materialId = m_Scene.AllocOrUpdateMaterial(
                materialUnityId, renderQueue,
                out MaterialDataRecord previousMaterial, out _, out bool materialRevised);

            if (materialRevised)
            {
                Push(new MeshSceneUndoEntry
                {
                    op = EMeshSceneUndoOp.RestoreMaterialRecord,
                    index = materialId.Index,
                    generation = materialId.Generation,
                    materialRecord = previousMaterial
                });
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreDrawRecord,
                index = drawId.Index,
                generation = drawId.Generation,
                drawRecord = previousDraw
            });

            // Created material is owned by the draw refcount; RestoreDrawRecord releases it on rollback.
            // Unique old material stays alive via deferred reclaim until EndUpdate.
            m_Scene.SetDrawMaterial(drawId, materialUnityId, renderQueue, materialId);
        }

        public void SetDrawPriority(MeshDrawId drawId, int priority)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetDraw(drawId, out MeshDrawRecord previousDraw))
            {
                return;
            }

            if (previousDraw.priority == priority)
            {
                return;
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreDrawRecord,
                index = drawId.Index,
                generation = drawId.Generation,
                drawRecord = previousDraw
            });

            m_Scene.SetDrawPriority(drawId, priority);
        }

        public void SetDrawEligibility(MeshDrawId drawId, EPassEligibility eligibility)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetDraw(drawId, out MeshDrawRecord previousDraw))
            {
                return;
            }

            if (previousDraw.eligibility == eligibility)
            {
                return;
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreDrawRecord,
                index = drawId.Index,
                generation = drawId.Generation,
                drawRecord = previousDraw
            });

            m_Scene.SetDrawEligibility(drawId, eligibility);
        }

        public void RemoveInstance(MeshInstanceId id)
        {
            ThrowIfClosed();
            if (!m_Scene.TryGetInstance(id, out MeshInstanceRecord instance))
            {
                return;
            }

            // Push restores so Rollback runs: transform → instance → sections/materials → draws.
            PushRemoveInstanceUndo(id, instance);
            m_Scene.RemoveInstanceInternal(id);
        }

        public void Commit()
        {
            ThrowIfClosed();
            m_Committed = true;
            m_UndoLog.Clear();
            m_Scene.EndUpdate(this, true);
            m_Disposed = true;
        }

        public void Rollback()
        {
            if (m_Disposed || m_Committed)
            {
                return;
            }

            for (int i = m_UndoLog.Count - 1; i >= 0; --i)
            {
                ApplyUndo(m_UndoLog[i]);
            }

            m_UndoLog.Clear();
            // ApplyUndo restores slots/refCounts; state snapshot corrects highWater/counts/free-list Length/dirty.
            m_Scene.RestoreTransactionState();
            m_Scene.EndUpdate(this, false);
            m_Disposed = true;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            if (!m_Committed)
            {
                Rollback();
            }
            else
            {
                m_Disposed = true;
            }
        }

        private void PushRemoveInstanceUndo(MeshInstanceId instanceId, in MeshInstanceRecord instance)
        {
            var draws = m_Scene.GetDraws();
            var drawGenerations = m_Scene.GetDrawGenerations();
            int highWater = m_Scene.DrawHighWater;

            var sectionRefFromInstance = new Dictionary<MeshSectionId, int>(8);
            var materialRefFromInstance = new Dictionary<MaterialDataId, int>(8);
            var sectionSnapshots = new Dictionary<MeshSectionId, MeshSectionRecord>(8);
            var materialSnapshots = new Dictionary<MaterialDataId, MaterialDataRecord>(8);

            for (int i = 0; i < highWater; ++i)
            {
                if (!m_Scene.IsDrawSlotLive(i))
                {
                    continue;
                }

                MeshDrawRecord draw = draws[i];
                if (!draw.instance.Equals(instanceId))
                {
                    continue;
                }

                Push(new MeshSceneUndoEntry
                {
                    op = EMeshSceneUndoOp.RestoreDraw,
                    index = (uint)i,
                    generation = drawGenerations[i],
                    drawRecord = draw
                });

                if (m_Scene.TryGetSection(draw.section, out MeshSectionRecord sectionRecord))
                {
                    sectionSnapshots[draw.section] = sectionRecord;
                    sectionRefFromInstance.TryGetValue(draw.section, out int sectionCount);
                    sectionRefFromInstance[draw.section] = sectionCount + 1;
                }

                if (m_Scene.TryGetMaterial(draw.material, out MaterialDataRecord materialRecord))
                {
                    materialSnapshots[draw.material] = materialRecord;
                    materialRefFromInstance.TryGetValue(draw.material, out int materialCount);
                    materialRefFromInstance[draw.material] = materialCount + 1;
                }
            }

            foreach (var pair in sectionRefFromInstance)
            {
                MeshSectionRecord snapshot = sectionSnapshots[pair.Key];
                if (snapshot.refCount <= pair.Value)
                {
                    Push(new MeshSceneUndoEntry
                    {
                        op = EMeshSceneUndoOp.RestoreSection,
                        index = pair.Key.Index,
                        generation = pair.Key.Generation,
                        sectionRecord = snapshot
                    });
                }
                else
                {
                    Push(new MeshSceneUndoEntry
                    {
                        op = EMeshSceneUndoOp.RestoreSectionRecord,
                        index = pair.Key.Index,
                        generation = pair.Key.Generation,
                        sectionRecord = snapshot
                    });
                }
            }

            foreach (var pair in materialRefFromInstance)
            {
                MaterialDataRecord snapshot = materialSnapshots[pair.Key];
                if (snapshot.refCount <= pair.Value)
                {
                    Push(new MeshSceneUndoEntry
                    {
                        op = EMeshSceneUndoOp.RestoreMaterial,
                        index = pair.Key.Index,
                        generation = pair.Key.Generation,
                        materialRecord = snapshot
                    });
                }
                else
                {
                    Push(new MeshSceneUndoEntry
                    {
                        op = EMeshSceneUndoOp.RestoreMaterialRecord,
                        index = pair.Key.Index,
                        generation = pair.Key.Generation,
                        materialRecord = snapshot
                    });
                }
            }

            Push(new MeshSceneUndoEntry
            {
                op = EMeshSceneUndoOp.RestoreInstance,
                index = instanceId.Index,
                generation = instanceId.Generation,
                instanceRecord = instance
            });

            if (m_Scene.TryGetTransform(instance.transform, out TransformRecord transformRecord))
            {
                Push(new MeshSceneUndoEntry
                {
                    op = EMeshSceneUndoOp.RestoreTransform,
                    index = instance.transform.Index,
                    generation = instance.transform.Generation,
                    transformRecord = transformRecord
                });
            }
        }

        private void ApplyUndo(in MeshSceneUndoEntry entry)
        {
            switch (entry.op)
            {
                case EMeshSceneUndoOp.FreeTransform:
                    m_Scene.FreeTransform(new TransformId(entry.index, entry.generation));
                    break;
                case EMeshSceneUndoOp.RestoreTransform:
                    m_Scene.RestoreTransform(new TransformId(entry.index, entry.generation), entry.transformRecord);
                    break;
                case EMeshSceneUndoOp.RestoreTransformRecord:
                    m_Scene.WriteTransform(new TransformId(entry.index, entry.generation), entry.transformRecord);
                    break;
                case EMeshSceneUndoOp.FreeInstance:
                    m_Scene.FreeInstance(new MeshInstanceId(entry.index, entry.generation));
                    break;
                case EMeshSceneUndoOp.RestoreInstance:
                    m_Scene.RestoreInstance(new MeshInstanceId(entry.index, entry.generation), entry.instanceRecord);
                    break;
                case EMeshSceneUndoOp.RestoreInstanceRecord:
                    m_Scene.WriteInstanceRecord(new MeshInstanceId(entry.index, entry.generation), entry.instanceRecord);
                    break;
                case EMeshSceneUndoOp.FreeDraw:
                    m_Scene.FreeDraw(new MeshDrawId(entry.index, entry.generation));
                    break;
                case EMeshSceneUndoOp.RestoreDraw:
                    m_Scene.RestoreDraw(new MeshDrawId(entry.index, entry.generation), entry.drawRecord);
                    break;
                case EMeshSceneUndoOp.RestoreDrawRecord:
                    m_Scene.WriteDrawRecord(new MeshDrawId(entry.index, entry.generation), entry.drawRecord);
                    break;
                case EMeshSceneUndoOp.FreeSection:
                    m_Scene.FreeSection(new MeshSectionId(entry.index, entry.generation));
                    break;
                case EMeshSceneUndoOp.RestoreSection:
                    m_Scene.RestoreSection(new MeshSectionId(entry.index, entry.generation), entry.sectionRecord);
                    break;
                case EMeshSceneUndoOp.RestoreSectionRecord:
                    m_Scene.WriteSectionRecord(new MeshSectionId(entry.index, entry.generation), entry.sectionRecord);
                    break;
                case EMeshSceneUndoOp.FreeMaterial:
                    m_Scene.FreeMaterial(new MaterialDataId(entry.index, entry.generation));
                    break;
                case EMeshSceneUndoOp.RestoreMaterial:
                    m_Scene.RestoreMaterial(new MaterialDataId(entry.index, entry.generation), entry.materialRecord);
                    break;
                case EMeshSceneUndoOp.RestoreMaterialRecord:
                    m_Scene.WriteMaterialRecord(new MaterialDataId(entry.index, entry.generation), entry.materialRecord);
                    break;
                case EMeshSceneUndoOp.RestoreRevisions:
                    m_Scene.RestoreRevisions(entry.revisions);
                    break;
            }
        }

        private void Push(in MeshSceneUndoEntry entry)
        {
            m_UndoLog.Add(entry);
        }

        private void ThrowIfClosed()
        {
            if (m_Disposed || m_Committed)
            {
                throw new ObjectDisposedException(nameof(MeshSceneUpdate));
            }
        }
    }
}
