using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshSceneTests
    {
        [Test]
        public void ThreeSubmeshObject_SharesOneTransform()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);

                update.CreateDraw(instance, meshUnityId: 100, sectionIndex: 0, materialUnityId: 1, EPassEligibility.GBuffer, renderQueue: 2000, priority: 0);
                update.CreateDraw(instance, meshUnityId: 100, sectionIndex: 1, materialUnityId: 2, EPassEligibility.GBuffer, renderQueue: 2000, priority: 0);
                update.CreateDraw(instance, meshUnityId: 100, sectionIndex: 2, materialUnityId: 3, EPassEligibility.GBuffer, renderQueue: 2000, priority: 0);
                update.Commit();

                Assert.AreEqual(1, scene.TransformCount);
                Assert.AreEqual(1, scene.LogicalInstanceCount);
                Assert.AreEqual(3, scene.DrawCount);
                Assert.AreEqual(1.0f, scene.MatrixDuplicateRatio, 0.0001f);
                Assert.IsTrue(scene.IsTransformOwned(transform));
            }
        }

        [Test]
        public void CreateInstance_RejectsTransformAlreadyOwned()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);

                var ex = Assert.Throws<ArgumentException>(() =>
                {
                    update.CreateInstance(
                        transform,
                        new FBound(float3.zero, new float3(1, 1, 1)),
                        layerMask: ~0,
                        renderingLayerMask: 1,
                        flags: EMeshInstanceFlags.Visible,
                        motionType: EMotionType.Object,
                        castShadow: ECastShadowMethod.Off);
                });
                StringAssert.Contains("TransformId already owned by another MeshInstance", ex.Message);
                update.Rollback();
            }
        }

        [Test]
        public void RemoveInstance_Rollback_RestoresTransformOwner()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                update.Commit();

                Assert.IsTrue(scene.IsTransformOwned(transform));

                update = scene.BeginUpdate();
                update.RemoveInstance(instance);
                Assert.IsFalse(scene.IsTransformAlive(transform));
                Assert.IsFalse(scene.IsTransformOwned(transform));
                update.Rollback();

                Assert.IsTrue(scene.IsInstanceAlive(instance));
                Assert.IsTrue(scene.IsTransformAlive(transform));
                Assert.IsTrue(scene.IsTransformOwned(transform));

                // Owner restored: same transform cannot be claimed by a second instance.
                update = scene.BeginUpdate();
                Assert.Throws<ArgumentException>(() =>
                {
                    update.CreateInstance(
                        transform,
                        new FBound(float3.zero, new float3(1, 1, 1)),
                        layerMask: ~0,
                        renderingLayerMask: 1,
                        flags: EMeshInstanceFlags.Visible,
                        motionType: EMotionType.Object,
                        castShadow: ECastShadowMethod.Off);
                });
                update.Rollback();
            }
        }

        [Test]
        public void Rollback_CreateTransformInstanceDraw_RestoresCountsAndRevisions()
        {
            using (var scene = new MeshScene(64))
            {
                int structural = scene.StructuralRevision;
                int content = scene.ContentRevision;
                int visibility = scene.VisibilityRevision;

                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                MeshDrawId draw = update.CreateDraw(instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0);

                Assert.IsTrue(draw.IsValid);
                Assert.AreEqual(1, scene.TransformCount);
                Assert.AreEqual(1, scene.LogicalInstanceCount);
                Assert.AreEqual(1, scene.DrawCount);

                update.Rollback();

                Assert.AreEqual(0, scene.TransformCount);
                Assert.AreEqual(0, scene.LogicalInstanceCount);
                Assert.AreEqual(0, scene.DrawCount);
                Assert.AreEqual(0, scene.SectionCount);
                Assert.AreEqual(0, scene.MaterialCount);
                Assert.AreEqual(structural, scene.StructuralRevision);
                Assert.AreEqual(content, scene.ContentRevision);
                Assert.AreEqual(visibility, scene.VisibilityRevision);
                Assert.IsFalse(scene.IsTransformAlive(transform));
                Assert.IsFalse(scene.IsInstanceAlive(instance));
                Assert.IsFalse(scene.IsDrawAlive(draw));
            }
        }

        [Test]
        public void Rollback_AtomicallyRestoresCountsAndRevisions()
        {
            using (var scene = new MeshScene(64))
            {
                int structural = scene.StructuralRevision;
                int content = scene.ContentRevision;
                int visibility = scene.VisibilityRevision;

                // Dispose without Commit must roll back the full undo log.
                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    TransformId transform = update.CreateTransform(float4x4.identity);
                    MeshInstanceId instance = update.CreateInstance(
                        transform,
                        new FBound(float3.zero, new float3(1, 1, 1)),
                        layerMask: ~0,
                        renderingLayerMask: 1,
                        flags: EMeshInstanceFlags.Visible,
                        motionType: EMotionType.Object,
                        castShadow: ECastShadowMethod.Off);
                    update.CreateDraw(instance, 100, 0, 1, EPassEligibility.GBuffer, 2000, 0);
                    update.CreateDraw(instance, 100, 1, 2, EPassEligibility.GBuffer, 2000, 0);
                    update.CreateDraw(instance, 100, 2, 3, EPassEligibility.GBuffer, 2000, 0);

                    Assert.AreEqual(1, scene.TransformCount);
                    Assert.AreEqual(1, scene.LogicalInstanceCount);
                    Assert.AreEqual(3, scene.DrawCount);
                    // Intentionally no Commit — Dispose triggers atomic Rollback.
                }

                Assert.AreEqual(0, scene.TransformCount);
                Assert.AreEqual(0, scene.LogicalInstanceCount);
                Assert.AreEqual(0, scene.DrawCount);
                Assert.AreEqual(structural, scene.StructuralRevision);
                Assert.AreEqual(content, scene.ContentRevision);
                Assert.AreEqual(visibility, scene.VisibilityRevision);
            }
        }

        [Test]
        public void CreateInstance_RejectsDeadTransform()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                Assert.Throws<ArgumentException>(() =>
                {
                    update.CreateInstance(
                        TransformId.Invalid,
                        new FBound(float3.zero, new float3(1, 1, 1)),
                        layerMask: ~0,
                        renderingLayerMask: 1,
                        flags: EMeshInstanceFlags.Visible,
                        motionType: EMotionType.Object,
                        castShadow: ECastShadowMethod.Off);
                });
                update.Rollback();
            }
        }

        [Test]
        public void CreateInstance_RejectsStaleOrInvalidTransform()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                Assert.Throws<ArgumentException>(() =>
                {
                    update.CreateInstance(
                        TransformId.Invalid,
                        new FBound(float3.zero, new float3(1, 1, 1)),
                        ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                });
                update.Rollback();

                update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                update.Commit();

                TransformId staleTransform = transform;

                update = scene.BeginUpdate();
                update.RemoveInstance(instance);
                update.Commit();

                Assert.IsFalse(scene.IsTransformAlive(staleTransform));

                update = scene.BeginUpdate();
                TransformId reused = update.CreateTransform(float4x4.identity);
                Assert.AreEqual(staleTransform.Index, reused.Index);
                Assert.AreNotEqual(staleTransform.Generation, reused.Generation);

                Assert.Throws<ArgumentException>(() =>
                {
                    update.CreateInstance(
                        staleTransform,
                        new FBound(float3.zero, new float3(1, 1, 1)),
                        ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                });
                update.Rollback();
            }
        }

        [Test]
        public void RemoveInstance_RejectsStaleGenerationOnReuse()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                MeshDrawId draw = update.CreateDraw(instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0);
                update.Commit();

                MeshInstanceId staleInstance = instance;
                MeshDrawId staleDraw = draw;

                update = scene.BeginUpdate();
                update.RemoveInstance(instance);
                update.Commit();

                Assert.IsFalse(scene.IsInstanceAlive(staleInstance));
                Assert.IsFalse(scene.TryGetInstance(staleInstance, out _));
                Assert.IsFalse(scene.TryGetDraw(staleDraw, out _));

                // Reuse slot with a new generation; stale ids must still be rejected.
                update = scene.BeginUpdate();
                TransformId transform2 = update.CreateTransform(float4x4.identity);
                MeshInstanceId reused = update.CreateInstance(
                    transform2,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                update.Commit();

                Assert.IsTrue(scene.IsInstanceAlive(reused));
                Assert.IsFalse(scene.IsInstanceAlive(staleInstance));
                Assert.AreNotEqual(staleInstance.Generation, reused.Generation);
            }
        }

        [Test]
        public void RemoveInstance_Rollback_RestoresDrawsAndSharedResources()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                MeshDrawId draw = update.CreateDraw(instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0);
                update.Commit();

                Assert.AreEqual(1, scene.DrawCount);
                Assert.AreEqual(1, scene.SectionCount);
                Assert.AreEqual(1, scene.MaterialCount);

                update = scene.BeginUpdate();
                update.RemoveInstance(instance);
                Assert.AreEqual(0, scene.DrawCount);
                Assert.AreEqual(0, scene.LogicalInstanceCount);
                update.Rollback();

                Assert.IsTrue(scene.IsInstanceAlive(instance));
                Assert.IsTrue(scene.IsDrawAlive(draw));
                Assert.AreEqual(1, scene.DrawCount);
                Assert.AreEqual(1, scene.SectionCount);
                Assert.AreEqual(1, scene.MaterialCount);
            }
        }

        [Test]
        public void SetMaterial_UniqueOldMaterial_Rollback_KeepsAliveRefCount()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                MeshDrawId draw = update.CreateDraw(instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0);
                update.Commit();

                Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw committedDraw));
                MaterialDataId oldMaterial = committedDraw.material;
                Assert.IsTrue(scene.IsMaterialAlive(oldMaterial));
                Assert.IsTrue(scene.TryGetMaterial(oldMaterial, out MaterialDataRecord oldRecord));
                Assert.AreEqual(1, oldRecord.refCount);
                Assert.AreEqual(1, scene.MaterialCount);

                update = scene.BeginUpdate();
                update.SetMaterial(draw, materialUnityId: 99, renderQueue: 3000);
                Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw midDraw));
                Assert.AreEqual(99, midDraw.materialUnityId);
                Assert.AreNotEqual(oldMaterial, midDraw.material);
                // Unique old material must remain addressable during the transaction (deferred reclaim).
                Assert.IsTrue(scene.IsMaterialAlive(oldMaterial));
                update.Rollback();

                Assert.IsTrue(scene.IsDrawAlive(draw));
                Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw restoredDraw));
                Assert.AreEqual(20, restoredDraw.materialUnityId);
                Assert.AreEqual(oldMaterial, restoredDraw.material);
                Assert.IsTrue(scene.IsMaterialAlive(oldMaterial));
                Assert.IsTrue(scene.TryGetMaterial(oldMaterial, out MaterialDataRecord restoredMaterial));
                Assert.AreEqual(1, restoredMaterial.refCount);
                Assert.AreEqual(1, scene.MaterialCount);
            }
        }

        [Test]
        public void SetMaterial_SameId_NewRenderQueue_UpdatesDrawQueueAndMaterialRevision()
        {
            // Lightweight MeshComponent path: same material instance id, new renderQueue must
            // refresh draw.renderQueue and bump MaterialData.revision (not priority-only).
            using (var scene = new MeshScene(64))
            {
                const int materialUnityId = 20;
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                MeshDrawId draw = update.CreateDraw(instance, 10, 0, materialUnityId, EPassEligibility.Depth, 2450, 0);
                update.Commit();

                Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw beforeDraw));
                Assert.IsTrue(scene.TryGetMaterial(beforeDraw.material, out MaterialDataRecord beforeMaterial));
                Assert.AreEqual(2450, beforeDraw.renderQueue);
                Assert.AreEqual(2450, beforeMaterial.renderQueue);
                uint revisionBefore = beforeMaterial.revision;
                int contentBefore = scene.ContentRevision;

                update = scene.BeginUpdate();
                update.SetMaterial(draw, materialUnityId, renderQueue: 3000);
                update.SetDrawPriority(draw, priority: 100 + 3000);
                update.Commit();

                Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw afterDraw));
                Assert.IsTrue(scene.TryGetMaterial(afterDraw.material, out MaterialDataRecord afterMaterial));
                Assert.AreEqual(materialUnityId, afterDraw.materialUnityId);
                Assert.AreEqual(beforeDraw.material, afterDraw.material);
                Assert.AreEqual(3000, afterDraw.renderQueue);
                Assert.AreEqual(3000, afterMaterial.renderQueue);
                Assert.AreEqual(100 + 3000, afterDraw.priority);
                Assert.Greater(afterMaterial.revision, revisionBefore);
                Assert.Greater(scene.ContentRevision, contentBefore);
            }
        }

        [Test]
        public void Rollback_RestoresRevisionsCountsAndDirtyRanges()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                MeshDrawId draw = update.CreateDraw(instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0);
                update.Commit();

                scene.ClearTransformDirtyRange();
                scene.ClearBoundsDirtyRange();

                var before = CaptureBookkeeping(scene);

                update = scene.BeginUpdate();
                TransformId extraTransform = update.CreateTransform(float4x4.identity);
                MeshInstanceId extraInstance = update.CreateInstance(
                    extraTransform,
                    new FBound(new float3(2, 0, 0), new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                update.CreateDraw(extraInstance, 11, 0, 21, EPassEligibility.GBuffer, 2000, 0);
                update.SetMaterial(draw, materialUnityId: 99, renderQueue: 3000);
                update.SetBounds(instance, new FBound(new float3(5, 0, 0), new float3(2, 2, 2)));
                update.Rollback();

                var after = CaptureBookkeeping(scene);
                // Snapshot restores revisions + dirty only. highWater / free-list may grow monotonically
                // (owned by Free*/Restore*/deferred reclaim — not truncated on rollback).
                AssertLiveBookkeepingEqual(before, after);
                Assert.IsTrue(scene.IsDrawAlive(draw));
                Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw restoredDraw));
                Assert.AreEqual(20, restoredDraw.materialUnityId);
            }
        }

        [Test]
        public void DrawFreeList_ReusesSlotWithBumpedGeneration()
        {
            using (var scene = new MeshScene(64))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                TransformId transform = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance = update.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                MeshDrawId first = update.CreateDraw(instance, 1, 0, 2, EPassEligibility.Depth, 2450, 0);
                update.Commit();

                update = scene.BeginUpdate();
                update.RemoveInstance(instance);
                update.Commit();

                update = scene.BeginUpdate();
                TransformId transform2 = update.CreateTransform(float4x4.identity);
                MeshInstanceId instance2 = update.CreateInstance(
                    transform2,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                MeshDrawId second = update.CreateDraw(instance2, 1, 0, 2, EPassEligibility.Depth, 2450, 0);
                update.Commit();

                Assert.AreEqual(first.Index, second.Index);
                Assert.AreNotEqual(first.Generation, second.Generation);
                Assert.IsFalse(scene.IsDrawAlive(first));
                Assert.IsTrue(scene.IsDrawAlive(second));
            }
        }

        [Test]
        public void SortKey_DescendingFlipsOrderRelativeToAscending()
        {
            AssertDistanceSortOrderFlipsWithDirection();
        }

        [Test]
        public void SortPlan_DescendingChangesOrderRelativeToAscending()
        {
            AssertDistanceSortOrderFlipsWithDirection();
        }

        [Test]
        public void SortKey_SignedPriority_MonotonicForNegZeroPosMinMax()
        {
            var plan = MeshSortPlan.Create(new MeshSortField(EMeshSortSemantic.PassPriority, ESortDirection.Ascending));
            var instance = new MeshInstanceRecord
            {
                worldBounds = new FBound(float3.zero, new float3(0.1f, 0.1f, 0.1f))
            };
            float3 view = float3.zero;

            int[] priorities = { int.MinValue, -1, 0, 1, int.MaxValue };
            ulong previous = 0;
            for (int i = 0; i < priorities.Length; ++i)
            {
                var draw = new MeshDraw { priority = priorities[i] };
                ulong key = MeshSortKey.PackSortKey(plan, draw, instance, view, drawIndex: i);
                if (i > 0)
                {
                    Assert.Less(previous, key, $"Expected key(priority={priorities[i - 1]}) < key(priority={priorities[i]})");
                }

                previous = key;
            }
        }

        [Test]
        public void SortKey_Distance_DoesNotSaturateAtGivenScale()
        {
            var draw = new MeshDraw();
            var near = new MeshInstanceRecord
            {
                worldBounds = new FBound(new float3(0, 0, 1000), new float3(0.1f, 0.1f, 0.1f))
            };
            var far = new MeshInstanceRecord
            {
                worldBounds = new FBound(new float3(0, 0, 2000), new float3(0.1f, 0.1f, 0.1f))
            };
            float3 view = float3.zero;

            // Meter scale keeps 1000m / 2000m distinct within 16-bit.
            var meterPlan = MeshSortPlan.Create(
                new MeshSortField(EMeshSortSemantic.Distance, ESortDirection.Ascending, quantizeScale: 1f));
            ulong meterNear = MeshSortKey.PackSortKey(meterPlan, draw, near, view, 0);
            ulong meterFar = MeshSortKey.PackSortKey(meterPlan, draw, far, view, 1);
            Assert.Less(meterNear, meterFar);

            // Default cm scale (100) saturates both beyond ~655.35m.
            var cmPlan = MeshSortPlan.Create(
                new MeshSortField(EMeshSortSemantic.Distance, ESortDirection.Ascending, quantizeScale: 100f));
            ulong cmNear = MeshSortKey.PackSortKey(cmPlan, draw, near, view, 0);
            ulong cmFar = MeshSortKey.PackSortKey(cmPlan, draw, far, view, 1);
            Assert.AreEqual(cmNear, cmFar);
        }

        [Test]
        public void CommandKey_HashCollision_DoesNotMergeUnequalKeys()
        {
            var keyA = new MeshDrawCommandKey { meshUnityId = 1, sectionIndex = 0, materialUnityId = 2, shaderPassIndex = 3 };
            var keyB = new MeshDrawCommandKey { meshUnityId = 1, sectionIndex = 0, materialUnityId = 2, shaderPassIndex = 4 };
            Assert.AreNotEqual(keyA, keyB);
            Assert.IsFalse(keyA.Equals(keyB));
        }

        [Test]
        public void CreateDraw_StaticFlags_StoredOnRecord()
        {
            using (var scene = new MeshScene(16))
            {
                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    TransformId transform = update.CreateTransform(float4x4.identity);
                    MeshInstanceId instance = update.CreateInstance(
                        transform,
                        new FBound(float3.zero, new float3(1, 1, 1)),
                        ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                    MeshDrawId draw = update.CreateDraw(
                        instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0,
                        EGeometrySourceKind.IndexedMesh, geometryRevision: 0, staticFlags: 1u);
                    update.Commit();

                    Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw record));
                    Assert.AreEqual(1u, record.staticFlags);
                }
            }
        }

        [Test]
        public void MeshVisibilityShare_DifferentFrustumHash_DoesNotShare()
        {
            using (var scene = new MeshScene(16))
            using (var share = new MeshVisibilityShare())
            {
                Plane[] planesA = CreateUnitFrustumPlanes();
                Plane[] planesB = CreateUnitFrustumPlanes();
                planesB[0] = new Plane(Vector3.right, 20f);

                Assert.AreNotEqual(MeshVisibilityShare.HashFrustum(planesA), MeshVisibilityShare.HashFrustum(planesB));

                share.BeginFrame(scene.VisibilityRevision);
                ulong viewKey = 0xABCDu;
                MeshVisibilityHandle first = share.Acquire(scene, viewKey, planesA, MeshVisibilityShare.PolicyMainFrustum, enable: true);
                MeshVisibilityHandle second = share.Acquire(scene, viewKey, planesB, MeshVisibilityShare.PolicyMainFrustum, enable: true);

                Assert.IsTrue(first.IsValid);
                Assert.IsTrue(second.IsValid);
                Assert.AreNotEqual(first, second);

                share.Release(first);
                share.Release(second);
            }
        }

        [Test]
        public void Section_GeometryRevisionChange_BumpsSectionRevision()
        {
            using (var scene = new MeshScene(16))
            {
                MeshSceneUpdate create = scene.BeginUpdate();
                TransformId transform = create.CreateTransform(float4x4.identity);
                MeshInstanceId instance = create.CreateInstance(
                    transform,
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                MeshDrawId draw = create.CreateDraw(
                    instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0,
                    EGeometrySourceKind.IndexedMesh, geometryRevision: 11u);
                create.Commit();

                Assert.IsTrue(scene.TryGetDraw(draw, out MeshDraw drawRecord));
                Assert.IsTrue(scene.TryGetSection(drawRecord.section, out MeshSectionRecord section));
                Assert.AreEqual(11u, section.geometryRevision);
                uint revisionBefore = section.revision;

                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    update.CreateDraw(
                        instance, 10, 0, 20, EPassEligibility.Depth, 2450, 0,
                        EGeometrySourceKind.IndexedMesh, geometryRevision: 22u);
                    update.Commit();
                }

                Assert.IsTrue(scene.TryGetSection(drawRecord.section, out section));
                Assert.AreEqual(22u, section.geometryRevision);
                Assert.Greater(section.revision, revisionBefore);
            }
        }

        [Test]
        public void MeshVisibilityShare_SameKeySharesOneResult_DifferentCascadeIsolated()
        {
            using (var scene = new MeshScene(16))
            using (var share = new MeshVisibilityShare())
            {
                MeshPipelineDiagnostics.Reset();
                Plane[] planes = CreateUnitFrustumPlanes();
                share.BeginFrame(scene.VisibilityRevision);

                ulong mainKey = 0xABCDu;
                MeshVisibilityHandle first = share.Acquire(scene, mainKey, planes, MeshVisibilityShare.PolicyMainFrustum, enable: true);
                MeshVisibilityHandle second = share.Acquire(scene, mainKey, planes, MeshVisibilityShare.PolicyMainFrustum, enable: true);

                Assert.IsTrue(first.IsValid);
                Assert.IsTrue(second.IsValid);
                Assert.AreEqual(first, second);
                Assert.AreEqual(1, MeshPipelineDiagnostics.VisibilityProductionsPerFrame);

                // Native generation and subview identity are separate fields.
                ulong cascade0 = (768ul << 32) | 42ul;
                ulong cascade1 = cascade0;
                MeshVisibilityHandle cascadeHandle = share.Acquire(
                    scene, cascade0, planes, MeshVisibilityShare.PolicyCascadeShadow, enable: true);
                Assert.IsTrue(cascadeHandle.IsValid);
                Assert.AreNotEqual(first, cascadeHandle);

                MeshVisibilityHandle cascadeHandleB = share.Acquire(
                    scene, cascade1, planes, MeshVisibilityShare.PolicyCascadeShadow, enable: true, subviewIndex: 1);
                Assert.IsTrue(cascadeHandleB.IsValid);
                Assert.AreNotEqual(cascadeHandle, cascadeHandleB);

                ulong localFace0 = cascade0;
                MeshVisibilityHandle localHandle = share.Acquire(
                    scene, localFace0, planes, MeshVisibilityShare.PolicyLocalShadow, enable: true);
                Assert.IsTrue(localHandle.IsValid);
                Assert.AreNotEqual(cascadeHandle, localHandle);
                Assert.AreEqual(4, MeshPipelineDiagnostics.VisibilityProductionsPerFrame);

                share.Release(first);
                share.Release(second);
                share.Release(cascadeHandle);
                share.Release(cascadeHandleB);
                share.Release(localHandle);
                // using Dispose(share) is the safety net against leaks.
            }
        }

        [Test]
        public void MeshInstanceCulling_AppliesVisibleFlagAndLayerMasks()
        {
            using (var scene = new MeshScene(16))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                MeshInstanceId visible = update.CreateInstance(
                    update.CreateTransform(float4x4.identity),
                    new FBound(float3.zero, new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                MeshInstanceId hiddenFlag = update.CreateInstance(
                    update.CreateTransform(float4x4.Translate(new float3(0.1f, 0, 0))),
                    new FBound(new float3(0.1f, 0, 0), new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.None,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                MeshInstanceId hiddenLayer = update.CreateInstance(
                    update.CreateTransform(float4x4.Translate(new float3(0.2f, 0, 0))),
                    new FBound(new float3(0.2f, 0, 0), new float3(1, 1, 1)),
                    layerMask: 0,
                    renderingLayerMask: 1,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                MeshInstanceId hiddenRendering = update.CreateInstance(
                    update.CreateTransform(float4x4.Translate(new float3(0.3f, 0, 0))),
                    new FBound(new float3(0.3f, 0, 0), new float3(1, 1, 1)),
                    layerMask: ~0,
                    renderingLayerMask: 2,
                    flags: EMeshInstanceFlags.Visible,
                    motionType: EMotionType.Object,
                    castShadow: ECastShadowMethod.Off);
                update.Commit();

                MeshViewCullingResult result = MeshVisibilityUtility.CullInstances(
                    scene,
                    CreateUnitFrustumPlanes(),
                    enable: true,
                    viewLayerMask: ~0,
                    viewRenderingLayerMask: 1,
                    filterRenderingLayers: true);
                try
                {
                    Assert.IsTrue(result.isValid);
                    Assert.AreEqual(1, result.instanceVisibility[(int)visible.Index]);
                    Assert.AreEqual(0, result.instanceVisibility[(int)hiddenFlag.Index]);
                    Assert.AreEqual(0, result.instanceVisibility[(int)hiddenLayer.Index]);
                    Assert.AreEqual(0, result.instanceVisibility[(int)hiddenRendering.Index]);
                }
                finally
                {
                    result.Release();
                }
            }
        }

        [Test]
        public void SelectPolicy_FallsBackWithoutComputeShader()
        {
            MeshDrawGPUBackend.SetShader(null);
            try
            {
                Assert.AreEqual(EMeshBackendPolicy.CpuDirect, MeshDrawGPUBackend.SelectPolicy(EMeshBackendPolicy.Auto));
                Assert.AreEqual(EMeshBackendPolicy.CpuDirect, MeshDrawGPUBackend.SelectPolicy(EMeshBackendPolicy.GpuIndirect));
                Assert.AreEqual(EMeshBackendPolicy.CpuDirect, MeshDrawGPUBackend.SelectPolicy(EMeshBackendPolicy.CpuDirect));
            }
            finally
            {
                MeshDrawGPUBackend.SetShader(null);
            }
        }

        [Test]
        public void TryPlanBatches_FailsWhenSingleCommandExceedsMaxInstances()
        {
            var counts = new uint[] { (uint)(MeshDrawGPUBackend.MaxInstances + 1) };
            var batches = new List<(int commandBegin, int batchCommands)>();

            Assert.IsFalse(MeshDrawGPUBackend.TryPlanBatches(counts, 1, batches));
            Assert.AreEqual(0, batches.Count);
        }

        [Test]
        public void TryPlanBatches_SplitsWhenTotalExceedsPayloadCaps()
        {
            // Two commands that individually fit, but together exceed MaxInstances.
            uint half = (uint)(MeshDrawGPUBackend.MaxInstances / 2 + 1);
            var counts = new uint[] { half, half };
            var batches = new List<(int commandBegin, int batchCommands)>();

            Assert.IsTrue(MeshDrawGPUBackend.TryPlanBatches(counts, 2, batches));
            Assert.AreEqual(2, batches.Count);
            Assert.AreEqual((0, 1), batches[0]);
            Assert.AreEqual((1, 1), batches[1]);
        }

        [Test]
        public void TryPlanBatches_SingleBatchWhenWithinCaps()
        {
            var counts = new uint[] { 10u, 20u, 30u };
            var batches = new List<(int commandBegin, int batchCommands)>();

            Assert.IsTrue(MeshDrawGPUBackend.TryPlanBatches(counts, 3, batches));
            Assert.AreEqual(1, batches.Count);
            Assert.AreEqual((0, 3), batches[0]);
        }

        [Test]
        public void ComputePayloadBudget_TakesMaxAcrossSplitBatches()
        {
            uint half = (uint)(MeshDrawGPUBackend.MaxInstances / 2 + 1);
            var counts = new uint[] { half, half };
            const int boundsCount = 100;

            MeshDrawGPUBackend.ComputePayloadBudget(counts, 2, boundsCount, out int maxCommands, out int maxInstances);

            Assert.AreEqual(1, maxCommands);
            Assert.AreEqual((int)half, maxInstances);
        }

        [Test]
        public void ComputePayloadBudget_UsesBoundsWhenLargerThanCandidates()
        {
            var counts = new uint[] { 10u, 20u, 30u };
            const int boundsCount = 1000;

            MeshDrawGPUBackend.ComputePayloadBudget(counts, 3, boundsCount, out int maxCommands, out int maxInstances);

            Assert.AreEqual(3, maxCommands);
            Assert.AreEqual(boundsCount, maxInstances);
        }

        [Test]
        public void ComputePayloadBudget_FailsWhenSingleCommandExceedsMaxInstances()
        {
            var counts = new uint[] { (uint)(MeshDrawGPUBackend.MaxInstances + 1) };

            MeshDrawGPUBackend.ComputePayloadBudget(counts, 1, boundsCount: 1, out int maxCommands, out int maxInstances);

            Assert.AreEqual(0, maxCommands);
            Assert.AreEqual(0, maxInstances);
        }

        [Test]
        public void RetirePayload_StaysOutOfPoolUntilFlush()
        {
            MeshDrawGPUBackend.FlushRetiredPayloads();

            MeshDrawGpuPayload payload = MeshDrawGPUBackend.RentPayload();
            MeshDrawGPUBackend.RetirePayload(payload);

            MeshDrawGpuPayload other = MeshDrawGPUBackend.RentPayload();
            Assert.AreNotSame(payload, other);

            MeshDrawGPUBackend.ReturnPayload(other);
            MeshDrawGPUBackend.FlushRetiredPayloads();

            MeshDrawGpuPayload again = MeshDrawGPUBackend.RentPayload();
            Assert.AreSame(payload, again);
            MeshDrawGPUBackend.ReturnPayload(again);
        }

        /// <summary>
        /// CpuDirect extract writes transform indices for Submit and instance slot indices for GPU candidates.
        /// </summary>
        [Test]
        public void MeshPassExtractJob_WritesTransformAndInstanceSlotIndices()
        {
            var passCommands = new NativeArray<MeshPassCommand>(1, Allocator.Temp);
            var memberDrawIndices = new NativeArray<int>(2, Allocator.Temp);
            var draws = new NativeArray<MeshDraw>(2, Allocator.Temp);
            var instances = new NativeArray<MeshInstanceRecord>(8, Allocator.Temp);
            var instanceGenerations = new NativeArray<uint>(8, Allocator.Temp);
            var transformGenerations = new NativeArray<uint>(8, Allocator.Temp);
            var visibility = new NativeArray<byte>(8, Allocator.Temp);
            var commands = new NativeList<MeshDrawCommand>(2, Allocator.Temp);
            var transformIndices = new NativeList<int>(2, Allocator.Temp);
            var slotIndices = new NativeList<int>(2, Allocator.Temp);
            try
            {
                passCommands[0] = new MeshPassCommand
                {
                    key = new MeshDrawCommandKey { meshUnityId = 10, sectionIndex = 0, materialUnityId = 20 },
                    memberBegin = 0,
                    memberCount = 2
                };
                memberDrawIndices[0] = 0;
                memberDrawIndices[1] = 1;
                draws[0] = new MeshDraw { meshUnityId = 10, sectionIndex = 0, materialUnityId = 20, instance = new MeshInstanceId(3u, 1u) };
                draws[1] = new MeshDraw { meshUnityId = 10, sectionIndex = 0, materialUnityId = 20, instance = new MeshInstanceId(5u, 1u) };
                instances[3] = new MeshInstanceRecord { transform = new TransformId(7u, 1u) };
                instances[5] = new MeshInstanceRecord { transform = new TransformId(7u, 1u) };
                instanceGenerations[3] = 1;
                instanceGenerations[5] = 1;
                transformGenerations[7] = 1;
                visibility[3] = 1;
                visibility[5] = 1;

                new MeshPassExtractJob
                {
                    commands = passCommands,
                    memberDrawIndices = memberDrawIndices,
                    draws = draws,
                    instances = instances,
                    instanceGenerations = instanceGenerations,
                    transformGenerations = transformGenerations,
                    instanceVisibility = visibility,
                    drawCommands = commands,
                    instanceIndices = transformIndices,
                    instanceSlotIndices = slotIndices
                }.Execute();

                Assert.AreEqual(7, transformIndices[0]);
                Assert.AreEqual(7, transformIndices[1]);
                Assert.AreEqual(3, slotIndices[0]);
                Assert.AreEqual(5, slotIndices[1]);
                Assert.AreEqual(1, commands.Length);
                Assert.AreEqual(2, commands[0].countOffset.x);
            }
            finally
            {
                passCommands.Dispose();
                memberDrawIndices.Dispose();
                draws.Dispose();
                instances.Dispose();
                instanceGenerations.Dispose();
                transformGenerations.Dispose();
                visibility.Dispose();
                commands.Dispose();
                transformIndices.Dispose();
                slotIndices.Dispose();
            }
        }

        private static void AssertDistanceSortOrderFlipsWithDirection()
        {
            var drawNear = new MeshDraw { priority = 0, renderQueue = 2000, materialUnityId = 1, meshUnityId = 1, sectionIndex = 0 };
            var drawFar = new MeshDraw { priority = 0, renderQueue = 2000, materialUnityId = 1, meshUnityId = 1, sectionIndex = 0 };
            var instanceNear = new MeshInstanceRecord { worldBounds = new FBound(new float3(0, 0, 1), new float3(0.1f, 0.1f, 0.1f)) };
            var instanceFar = new MeshInstanceRecord { worldBounds = new FBound(new float3(0, 0, 100), new float3(0.1f, 0.1f, 0.1f)) };
            float3 view = float3.zero;

            var ascending = MeshSortPlan.Create(new MeshSortField(EMeshSortSemantic.Distance, ESortDirection.Ascending));
            var descending = MeshSortPlan.Create(new MeshSortField(EMeshSortSemantic.Distance, ESortDirection.Descending));

            ulong nearAsc = MeshSortKey.PackSortKey(ascending, drawNear, instanceNear, view, 0);
            ulong farAsc = MeshSortKey.PackSortKey(ascending, drawFar, instanceFar, view, 1);
            ulong nearDesc = MeshSortKey.PackSortKey(descending, drawNear, instanceNear, view, 0);
            ulong farDesc = MeshSortKey.PackSortKey(descending, drawFar, instanceFar, view, 1);

            Assert.Less(nearAsc, farAsc);
            Assert.Greater(nearDesc, farDesc);
        }

        // Live bookkeeping compared on rollback. highWater / free-list lengths are intentionally omitted:
        // they are allowed to grow monotonically across rollback (Free*/Restore*/deferred reclaim).
        private struct MeshSceneBookkeeping
        {
            public int StructuralRevision;
            public int ContentRevision;
            public int VisibilityRevision;
            public int LogicalInstanceCount;
            public int TransformCount;
            public int DrawCount;
            public int SectionCount;
            public int MaterialCount;
            public int TransformDirtyBegin;
            public int TransformDirtyEnd;
            public int BoundsDirtyBegin;
            public int BoundsDirtyEnd;
        }

        private static MeshSceneBookkeeping CaptureBookkeeping(MeshScene scene)
        {
            return new MeshSceneBookkeeping
            {
                StructuralRevision = scene.StructuralRevision,
                ContentRevision = scene.ContentRevision,
                VisibilityRevision = scene.VisibilityRevision,
                LogicalInstanceCount = scene.LogicalInstanceCount,
                TransformCount = scene.TransformCount,
                DrawCount = scene.DrawCount,
                SectionCount = scene.SectionCount,
                MaterialCount = scene.MaterialCount,
                TransformDirtyBegin = scene.TransformDirtyBegin,
                TransformDirtyEnd = scene.TransformDirtyEnd,
                BoundsDirtyBegin = scene.BoundsDirtyBegin,
                BoundsDirtyEnd = scene.BoundsDirtyEnd
            };
        }

        private static void AssertLiveBookkeepingEqual(in MeshSceneBookkeeping expected, in MeshSceneBookkeeping actual)
        {
            Assert.AreEqual(expected.StructuralRevision, actual.StructuralRevision);
            Assert.AreEqual(expected.ContentRevision, actual.ContentRevision);
            Assert.AreEqual(expected.VisibilityRevision, actual.VisibilityRevision);
            Assert.AreEqual(expected.LogicalInstanceCount, actual.LogicalInstanceCount);
            Assert.AreEqual(expected.TransformCount, actual.TransformCount);
            Assert.AreEqual(expected.DrawCount, actual.DrawCount);
            Assert.AreEqual(expected.SectionCount, actual.SectionCount);
            Assert.AreEqual(expected.MaterialCount, actual.MaterialCount);
            Assert.AreEqual(expected.TransformDirtyBegin, actual.TransformDirtyBegin);
            Assert.AreEqual(expected.TransformDirtyEnd, actual.TransformDirtyEnd);
            Assert.AreEqual(expected.BoundsDirtyBegin, actual.BoundsDirtyBegin);
            Assert.AreEqual(expected.BoundsDirtyEnd, actual.BoundsDirtyEnd);
        }

        private static Plane[] CreateUnitFrustumPlanes()
        {
            // Six planes that enclose the origin with generous distance — empty scene culls fine.
            return new[]
            {
                new Plane(Vector3.right, 10f),
                new Plane(Vector3.left, 10f),
                new Plane(Vector3.up, 10f),
                new Plane(Vector3.down, 10f),
                new Plane(Vector3.forward, 10f),
                new Plane(Vector3.back, 10f)
            };
        }

    }
}
