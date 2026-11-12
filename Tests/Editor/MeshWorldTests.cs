using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshWorldTests
    {
        static readonly MeshMaterialState s_Unrouted = MeshTestUtility.Unrouted;

        struct ElementRecord
        {
            public int index;
            public MeshSectionId section;
            public MeshMaterialId material;
            public int priority;
        }

        sealed class WorldSnapshot
        {
            public int entityCount, elementCount, sectionCount, materialCount;
            public int structuralRevision;
            public ulong transformRevision;
            public MeshEntityDesc desc;
            public readonly List<ElementRecord> chain = new List<ElementRecord>();
            public readonly List<MeshSection> sections = new List<MeshSection>();
            public readonly List<MeshMaterial> materials = new List<MeshMaterial>();
        }

        static WorldSnapshot Capture(MeshWorld world, MeshEntityId entity)
        {
            var snapshot = new WorldSnapshot
            {
                entityCount = world.entityCount,
                elementCount = world.elementCount,
                sectionCount = world.sectionCount,
                materialCount = world.materialCount,
                structuralRevision = world.structuralRevision,
                transformRevision = world.transformRevision
            };
            Assert.IsTrue(world.TryGetEntity(entity, out MeshEntity record));
            snapshot.desc = record.desc;
            int count = 0;
            for (int index = record.firstElement; index >= 0; index = world.elements[index].nextElement)
            {
                MeshElement element = world.elements[index];
                snapshot.chain.Add(new ElementRecord { index = index, section = element.section, material = element.material, priority = element.priority });
                Assert.IsTrue(world.TryGetSection(element.section, out MeshSection section));
                snapshot.sections.Add(section);
                if (element.material.IsValid)
                {
                    Assert.IsTrue(world.TryGetMaterial(element.material, out MeshMaterial material));
                    snapshot.materials.Add(material);
                }
                Assert.Less(++count, 64, "Element chain does not terminate.");
            }
            Assert.AreEqual(record.elementCount, snapshot.chain.Count);
            return snapshot;
        }

        static void AssertEqual(WorldSnapshot expected, WorldSnapshot actual)
        {
            Assert.AreEqual(expected.entityCount, actual.entityCount, "entityCount");
            Assert.AreEqual(expected.elementCount, actual.elementCount, "elementCount");
            Assert.AreEqual(expected.sectionCount, actual.sectionCount, "sectionCount");
            Assert.AreEqual(expected.materialCount, actual.materialCount, "materialCount");
            Assert.AreEqual(expected.structuralRevision, actual.structuralRevision, "structuralRevision");
            Assert.AreEqual(expected.transformRevision, actual.transformRevision, "transformRevision");
            Assert.AreEqual(expected.desc, actual.desc, "entity desc");
            Assert.AreEqual(expected.chain.Count, actual.chain.Count, "element chain length");
            for (int i = 0; i < expected.chain.Count; ++i)
            {
                Assert.AreEqual(expected.chain[i].index, actual.chain[i].index, "element chain order");
                Assert.AreEqual(expected.chain[i].section, actual.chain[i].section);
                Assert.AreEqual(expected.chain[i].material, actual.chain[i].material);
                Assert.AreEqual(expected.chain[i].priority, actual.chain[i].priority);
            }
            for (int i = 0; i < expected.sections.Count; ++i)
                Assert.AreEqual(expected.sections[i].refCount, actual.sections[i].refCount, "section refCount");
            for (int i = 0; i < expected.materials.Count; ++i)
            {
                Assert.AreEqual(expected.materials[i].refCount, actual.materials[i].refCount, "material refCount");
                Assert.AreEqual(expected.materials[i].state, actual.materials[i].state, "material state");
            }
        }

        [Test]
        public void Rollback_RestoresCountsChainsRevisionsAndLookups()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshEntityId kept, removed;
                MeshElementId keptFirst, keptSecond;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    kept = MeshTestUtility.CreateEntity(update, float3.zero);
                    keptFirst = update.AddElement(kept, 100, 0, 0, 1, s_Unrouted, 0);
                    keptSecond = update.AddElement(kept, 100, 1, 0, 2, s_Unrouted, 0);
                    removed = MeshTestUtility.CreateEntity(update, new float3(2, 0, 0));
                    update.AddElement(removed, 100, 0, 0, 1, s_Unrouted, 0);
                    update.Commit();
                }
                Assert.IsTrue(world.TryGetElement(keptFirst, out MeshElement first));
                Assert.IsTrue(world.TryGetElement(keptSecond, out MeshElement second));
                WorldSnapshot before = Capture(world, kept);
                Assert.AreEqual(2, before.sections[1].refCount, "Two elements share the (mesh 100, submesh 0) section.");

                MeshEntityId created;
                MeshSectionId createdSection;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    MeshElementId added = update.AddElement(kept, 300, 0, 0, 3, s_Unrouted, 0);
                    update.SetElementMaterial(keptSecond, 1, s_Unrouted);
                    update.SetElementPriority(keptFirst, 9);
                    update.SetEntityDesc(kept, MeshTestUtility.Desc(EMeshEntityFlags.Visible | EMeshEntityFlags.CastShadow, layerMask: 4));
                    // The same material id with a new state edits the shared material record.
                    update.SetElementMaterial(keptFirst, 1, new MeshMaterialState(MeshPassTable.TranslucentQueueMax + 2, 0, 0, 0));
                    update.RemoveEntity(removed);
                    created = MeshTestUtility.CreateEntity(update, new float3(4, 0, 0));
                    update.AddElement(created, 100, 0, 0, 1, s_Unrouted, 0);
                    Assert.IsTrue(world.TryGetElement(added, out MeshElement addedRecord));
                    createdSection = addedRecord.section;

                    // The edits really happened inside the transaction.
                    Assert.IsFalse(world.IsAlive(removed));
                    Assert.AreEqual(before.sectionCount + 1, world.sectionCount);
                    Assert.AreNotEqual(before.structuralRevision, world.structuralRevision);
                    Assert.IsTrue(world.TryGetMaterial(second.material, out MeshMaterial released));
                    Assert.AreEqual(0, released.refCount, "A material whose last reference was released stays addressable until commit.");
                    // Dispose without Commit rolls back.
                }

                AssertEqual(before, Capture(world, kept));
                Assert.IsTrue(world.IsAlive(removed), "Rollback revives a removed entity under its original id.");
                Assert.IsFalse(world.IsAlive(created));
                Assert.IsFalse(world.TryGetSection(createdSection, out _));

                // Lookups follow the restored tables: existing keys resolve to their records, rolled-back keys are new.
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    Assert.IsTrue(world.TryGetElement(update.AddElement(kept, 100, 0, 0, 1, s_Unrouted, 0), out MeshElement shared));
                    Assert.AreEqual(first.section, shared.section);
                    Assert.AreEqual(first.material, shared.material);
                    Assert.IsTrue(world.TryGetElement(update.AddElement(kept, 300, 0, 0, 3, s_Unrouted, 0), out MeshElement fresh));
                    Assert.AreEqual(before.sectionCount + 1, world.sectionCount);
                    Assert.AreEqual(before.materialCount + 1, world.materialCount);
                    Assert.IsTrue(world.TryGetSection(fresh.section, out MeshSection freshSection));
                    Assert.AreEqual(1, freshSection.refCount);
                }
                AssertEqual(before, Capture(world, kept));
            }
        }

        [Test]
        public void Commit_ReclaimsSectionsAndMaterialsWhoseLastReferenceWasReleased()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshEntityId entity;
                MeshElementId element;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    entity = MeshTestUtility.CreateEntity(update, float3.zero);
                    element = update.AddElement(entity, 500, 0, 0, 7, s_Unrouted, 0);
                    update.Commit();
                }
                Assert.IsTrue(world.TryGetElement(element, out MeshElement original));

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.SetElementMaterial(element, 8, s_Unrouted);
                    update.Commit();
                }
                Assert.IsFalse(world.TryGetMaterial(original.material, out _));
                Assert.AreEqual(1, world.materialCount);
                Assert.IsTrue(world.TryGetElement(element, out MeshElement switched));
                Assert.IsTrue(world.TryGetMaterial(switched.material, out MeshMaterial material));
                Assert.AreEqual(8ul, material.materialId);

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(entity);
                    update.Commit();
                }
                Assert.AreEqual(0, world.elementCount);
                Assert.AreEqual(0, world.sectionCount);
                Assert.AreEqual(0, world.materialCount);
                Assert.IsFalse(world.TryGetSection(original.section, out _));
                Assert.IsFalse(world.IsAlive(element));
            }
        }

        [Test]
        public void EntityIds_NeverAliasAcrossRollbackAndSlotReuse()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshEntityId rolledBack;
                using (MeshWorldUpdate update = world.BeginUpdate()) rolledBack = MeshTestUtility.CreateEntity(update, float3.zero);
                Assert.AreEqual(1u, rolledBack.Generation & 1u, "Live generations are odd.");
                Assert.IsFalse(world.IsAlive(rolledBack));

                MeshEntityId reused;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    reused = MeshTestUtility.CreateEntity(update, float3.zero);
                    update.Commit();
                }
                Assert.AreEqual(rolledBack.Index, reused.Index, "Rollback returns an allocated slot to the free list.");
                Assert.AreNotEqual(rolledBack.Generation, reused.Generation);
                Assert.IsFalse(world.IsAlive(rolledBack));
                Assert.IsTrue(world.IsAlive(reused));

                MeshEntityId other;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(reused);
                    other = MeshTestUtility.CreateEntity(update, float3.zero);
                    Assert.AreNotEqual(reused.Index, other.Index, "A slot killed inside a transaction is reclaimed only at commit.");
                }
                Assert.IsTrue(world.IsAlive(reused));
                Assert.IsFalse(world.IsAlive(other));

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(reused);
                    update.Commit();
                }
                Assert.IsFalse(world.TryGetEntity(reused, out _));
                Assert.IsFalse(world.TryGetPose(reused, out _, out _));

                MeshEntityId next;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    next = MeshTestUtility.CreateEntity(update, float3.zero);
                    Assert.Throws<ArgumentException>(() => update.RemoveEntity(reused));
                    Assert.Throws<ArgumentException>(() => update.AddElement(reused, 100, 0, 0, 0, s_Unrouted, 0));
                    Assert.Throws<ArgumentException>(() => update.SetEntityDesc(other, MeshTestUtility.Desc()));
                    update.Commit();
                }
                Assert.AreEqual(reused.Index, next.Index);
                Assert.Greater(next.Generation, reused.Generation);
                Assert.IsFalse(world.IsAlive(reused));
                Assert.IsTrue(world.IsAlive(next));
            }
        }

        [Test]
        public void Lease_CannotEditOrCancelAnotherTransaction()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshWorldUpdate first = world.BeginUpdate();
                MeshWorldUpdate copy = first;
                first.Commit();
                using (MeshWorldUpdate current = world.BeginUpdate())
                {
                    Assert.Throws<InvalidOperationException>(() => MeshTestUtility.CreateEntity(copy, float3.zero));
                    copy.Dispose();
                    Assert.Throws<InvalidOperationException>(() => world.BeginUpdate(), "One transaction at a time.");
                    MeshTestUtility.CreateEntity(current, float3.zero);
                    current.Commit();
                }
                Assert.AreEqual(1, world.entityCount);

                MeshWorldUpdate transaction = world.BeginUpdate();
                MeshWorldUpdate duplicate = transaction;
                MeshTestUtility.CreateEntity(transaction, float3.zero);
                duplicate.Dispose();
                Assert.AreEqual(1, world.entityCount, "Either copy of the lease rolls the transaction back.");
                using (MeshWorldUpdate next = world.BeginUpdate())
                {
                    MeshTestUtility.CreateEntity(next, float3.zero);
                    transaction.Dispose();
                    next.Commit();
                }
                Assert.AreEqual(2, world.entityCount, "A closed lease cannot roll back the transaction that followed it.");
                Assert.Throws<InvalidOperationException>(() => transaction.Commit());
                Assert.Throws<InvalidOperationException>(() => default(MeshWorldUpdate).Commit());
                Assert.DoesNotThrow(() => default(MeshWorldUpdate).Dispose());
            }
        }

        [Test]
        public void Poses_AreWrittenOutsideTransactions_AndDirtyOnlyTheirPages()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                var ids = new MeshEntityId[130];
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    for (int i = 0; i < ids.Length; ++i) ids[i] = MeshTestUtility.CreateEntity(update, new float3(i, 0, 0));
                    update.Commit();
                }
                world.UpdateResidency();
                // Treat the first version as produced so the next one holds only its own dirty pages.
                world.transformVersions.current.queued = true;
                ulong transformRevision = world.transformRevision;
                int structuralRevision = world.structuralRevision;

                Assert.IsTrue(world.TryGetPose(ids[0], out float4x4 unchanged, out BoundingBox unchangedBound));
                world.WriteEntityPose(ids[0], unchanged, unchangedBound);
                Assert.AreEqual(transformRevision, world.transformRevision, "Rewriting the current pose is not a change.");

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    Assert.Throws<InvalidOperationException>(() => world.WriteEntityPose(ids[0], float4x4.identity, default));
                    Assert.Throws<InvalidOperationException>(() => world.UpdateDynamicPoses());
                }

                float4x4 moved = float4x4.Translate(new float3(0, 5, 0));
                BoundingBox movedBound = MeshTestUtility.Bound(new float3(0, 5, 0));
                world.WriteEntityPose(ids[0], moved, movedBound);
                world.WriteEntityPose(ids[129], float4x4.Translate(new float3(129, 5, 0)), MeshTestUtility.Bound(new float3(129, 5, 0)));
                Assert.Greater(world.transformRevision, transformRevision);
                Assert.AreEqual(structuralRevision, world.structuralRevision, "A pose is not a structural edit.");

                using (MeshWorldUpdate update = world.BeginUpdate())
                    update.SetEntityDesc(ids[0], MeshTestUtility.Desc(EMeshEntityFlags.None));
                Assert.IsTrue(world.TryGetPose(ids[0], out float4x4 kept, out BoundingBox keptBound));
                Assert.IsTrue(kept.Equals(moved), "Poses do not take part in undo.");
                Assert.AreEqual(movedBound, keptBound);

                var runs = new NativeList<int2>(4, Allocator.Temp);
                try
                {
                    world.CollectTransformDirtyRuns(runs);
                    CollectionAssert.AreEqual(new[] { new int2(0, 64), new int2(128, 130) }, runs.AsArray().ToArray());
                    world.CollectEntityDirtyRuns(runs);
                    CollectionAssert.AreEqual(new[] { new int2(0, 64), new int2(128, 130) }, runs.AsArray().ToArray());
                }
                finally { runs.Dispose(); }

                world.UpdateResidency();
                MeshTransformVersion version = world.transformVersions.current;
                Assert.AreEqual(64 + 2, version.changeCount, "Only the two dirty pages are patched.");
                long uploaded = world.stats.uploadedBytes;
                world.UpdateResidency();
                Assert.AreSame(version, world.transformVersions.current);
                Assert.AreEqual(uploaded, world.stats.uploadedBytes, "Residency without edits uploads nothing.");
            }
        }

        [Test]
        public void RepeatedPoseWrites_AllocateNoManagedMemoryAfterWarmup()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshEntityId entity;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    entity = MeshTestUtility.CreateEntity(update, float3.zero);
                    update.Commit();
                }
                for (int i = 0; i < 8; ++i) world.WriteEntityPose(entity, float4x4.Translate(new float3(i, 0, 0)), MeshTestUtility.Bound(new float3(i, 0, 0)));
                using var allocations = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame);
                allocations.Start();
                var calibration = new byte[1024];
                allocations.Stop();
                GC.KeepAlive(calibration);
                Assert.Greater(allocations.GetSample(0).Count, 0, "The allocation recorder must observe a known allocation.");
                allocations.Reset();
                allocations.Start();
                for (int i = 0; i < 512; ++i) world.WriteEntityPose(entity, float4x4.Translate(new float3(i, 1, 0)), MeshTestUtility.Bound(new float3(i, 1, 0)));
                allocations.Stop();
                Assert.AreEqual(0, allocations.Count == 0 ? 0 : allocations.GetSample(0).Count);
            }
        }

        [Test]
        public void DynamicEntities_RegisterAtCommit_AndReadTheirTransformEachUpdate()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                GameObject owner = fixture.Own(new GameObject("MeshWorldTests.Dynamic") { hideFlags = HideFlags.DontSave });
                owner.transform.position = new Vector3(1, 2, 3);
                var localBounds = new BoundingBox(float3.zero, new float3(0.5f));
                MeshEntityId entity;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    entity = MeshTestUtility.CreateEntity(update, float3.zero);
                    update.SetEntityMobility(entity, owner.transform, localBounds);
                    Assert.IsFalse(world.IsDynamic(entity), "Mobility is published at commit.");
                    update.Commit();
                }
                Assert.IsTrue(world.IsDynamic(entity));
                Assert.AreEqual(1, world.dynamicEntityCount);

                ulong revision = world.transformRevision;
                world.UpdateDynamicPoses();
                Assert.Greater(world.transformRevision, revision);
                Assert.IsTrue(world.TryGetPose(entity, out float4x4 matrix, out BoundingBox bounds));
                Assert.AreEqual(new float3(1, 2, 3), matrix.c3.xyz);
                Assert.AreEqual(new float3(1, 2, 3), bounds.center);
                Assert.AreEqual(localBounds.extents, bounds.extents);

                revision = world.transformRevision;
                world.UpdateDynamicPoses();
                Assert.AreEqual(revision, world.transformRevision, "An unchanged transform is not a pose change.");

                using (MeshWorldUpdate update = world.BeginUpdate()) update.SetEntityMobility(entity, null, localBounds);
                Assert.IsTrue(world.IsDynamic(entity), "A rolled-back mobility change is discarded.");

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(entity);
                    update.Commit();
                }
                Assert.AreEqual(0, world.dynamicEntityCount);
            }
        }

        [Test]
        public void SelectBackend_FollowsOverrideThenDebuggerThenAsset_AndRecordsCpuPassesOnCpu()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                InfinityDebugDisplaySettingsMesh debug = InfinityDebugDisplaySettings.current.mesh;
                EMeshDrawBackendPolicy? previousDebugOverride = debug.backendOverride;
                EMeshDrawBackendPolicy indirect = world.gpuBackend.isSupported ? EMeshDrawBackendPolicy.GPUIndirect : EMeshDrawBackendPolicy.CPUDirect;
                try
                {
                    world.assetBackendPolicy = EMeshDrawBackendPolicy.GPUIndirect;
                    debug.backendOverride = EMeshDrawBackendPolicy.CPUDirect;
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, world.SelectBackend(MeshPassTable.GBuffer), "The debugger precedes the asset.");
                    world.SetBackendOverride(EMeshDrawBackendPolicy.GPUIndirect);
                    Assert.AreEqual(indirect, world.SelectBackend(MeshPassTable.GBuffer), "An explicit override precedes the debugger.");
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, world.SelectBackend(MeshPassTable.TranslucentT0), "A pass that records on the CPU stays there.");
                    Assert.AreEqual(indirect, world.SelectBackend(MeshPassTable.TranslucentDepth), "Translucent depth follows device capability.");

                    world.SetBackendOverride(null);
                    debug.backendOverride = null;
                    world.assetBackendPolicy = EMeshDrawBackendPolicy.CPUDirect;
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, world.SelectBackend(MeshPassTable.GBuffer));
                    world.assetBackendPolicy = EMeshDrawBackendPolicy.Auto;
                    Assert.AreEqual(indirect, world.SelectBackend(MeshPassTable.GBuffer), "Auto follows device capability.");
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, world.SelectBackend(MeshPassTable.TranslucentT0));
                    Assert.AreEqual(indirect, world.SelectBackend(MeshPassTable.TranslucentDepth));
                }
                finally { debug.backendOverride = previousDebugOverride; }
            }
        }
    }
}
