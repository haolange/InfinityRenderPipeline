using NUnit.Framework;
using Unity.Mathematics;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshViewMotionHistoryTests
    {
        MeshScene m_Scene;
        ResourcePool m_Pool;
        GpuScene m_Gpu;
        readonly MeshViewMotionHistory m_A = new MeshViewMotionHistory();
        readonly MeshViewMotionHistory m_B = new MeshViewMotionHistory();

        [SetUp]
        public void SetUp()
        {
            m_Scene = new MeshScene(16); m_Pool = new ResourcePool(); m_Gpu = new GpuScene(m_Pool, m_Scene);
        }
        [TearDown]
        public void TearDown()
        {
            m_A.Clear(); m_B.Clear(); m_Gpu.Dispose(); m_Pool.Dispose(); m_Scene.Dispose();
        }
        TransformId Create(float x)
        {
            using (var update = m_Scene.BeginUpdate())
            {
                var id = update.CreateTransform(float4x4.Translate(new float3(x, 0, 0)));
                update.Commit(); return id;
            }
        }
        void Move(TransformId id, float x)
        {
            using (var update = m_Scene.BeginUpdate())
            { update.SetTransform(id, float4x4.Translate(new float3(x, 0, 0))); update.Commit(); }
        }
        MeshTransformVersion PrepareReference()
        {
            m_Gpu.Update();
            // These tests verify reference transactions only. GPU production is covered separately.
            var version = m_Gpu.transformVersions.current; version.queued = true; return version;
        }
        [Test]
        public void SparseViews_RetainIndependentAcceptedReferences_ToSharedVersions()
        {
            var id = Create(0); var first = PrepareReference();
            m_A.Prepare(first); m_A.Commit();
            Move(id, 1); var second = PrepareReference(); m_B.Prepare(second); m_B.Commit();
            Move(id, 2); var third = PrepareReference();
            Assert.AreSame(first, m_A.Prepare(third).previous);
            Assert.AreSame(second, m_B.Prepare(third).previous);
            m_A.Commit();
            Assert.AreSame(third, m_A.accepted); Assert.AreSame(second, m_B.accepted);
            Assert.AreSame(third, m_B.Prepare(third).current);
        }
        [Test]
        public void FailedSubmission_LeavesAcceptedVersionUnchanged()
        {
            var id = Create(0); var first = PrepareReference(); m_A.Prepare(first); m_A.Commit();
            Move(id, 1); m_A.Prepare(PrepareReference()); m_A.Rollback();
            Assert.AreSame(first, m_A.accepted);
        }
        [Test]
        public void UnproducedVersion_CannotAdvanceViewHistory()
        {
            Create(0); m_Gpu.Update(); var version = m_Gpu.transformVersions.current;
            m_A.Prepare(version); m_A.Commit();
            Assert.IsNull(m_A.accepted); Assert.AreEqual(1, version.references);
        }
        [Test]
        public void SubmitFailure_RequiresReproduction_AndRetainsDirtyPatches()
        {
            var id = Create(0); var first = PrepareReference(); first.submitted = true;
            m_A.Prepare(first); m_A.Commit();
            Move(id, 1); m_Gpu.Update(); var pending = m_Gpu.transformVersions.current;
            pending.OnQueued(); m_A.Prepare(pending);
            m_Gpu.transformVersions.RollbackUnsubmitted(); m_A.Rollback();
            Assert.IsFalse(pending.queued); Assert.IsTrue(first.queued);
            Assert.AreEqual(1, pending.changes.Length); Assert.AreSame(first, m_A.accepted);
        }
        [Test]
        public void RepeatedMatrixAndRollback_DoNotAdvanceTransformRevision()
        {
            var id = Create(3); var first = PrepareReference(); ulong revision = m_Scene.TransformRevision;
            Move(id, 3); m_Gpu.Update();
            Assert.AreEqual(revision, m_Scene.TransformRevision); Assert.AreSame(first, m_Gpu.transformVersions.current);
            using (var update = m_Scene.BeginUpdate()) update.SetTransform(id, float4x4.identity);
            Assert.AreEqual(revision, m_Scene.TransformRevision);
            m_Gpu.Update(); Assert.AreSame(first, m_Gpu.transformVersions.current);
        }
        [Test]
        public void ResidencyRejectsOpenTransaction_WithoutPublishingVersion()
        {
            using (var update = m_Scene.BeginUpdate())
            {
                update.CreateTransform(float4x4.identity);
                Assert.Throws<System.InvalidOperationException>(() => m_Gpu.Update());
                Assert.IsNull(m_Gpu.transformVersions.current);
            }
            Assert.DoesNotThrow(() => m_Gpu.Update());
        }
        [Test]
        public void SlotReuseAndGrowth_ProduceNewVersion_WithoutReplacingPinnedReferences()
        {
            var id = Create(3); var first = PrepareReference(); m_A.Prepare(first); m_A.Commit();
            m_Scene.FreeTransform(id); var reused = Create(100);
            Assert.AreEqual(id.Index, reused.Index); Assert.AreNotEqual(id.Generation, reused.Generation);
            for (int i = 0; i < 20; ++i) Create(i);
            var next = PrepareReference();
            Assert.AreNotSame(first, next); Assert.AreSame(first, m_A.Prepare(next).previous);
            Assert.Greater(next.count, first.count);
        }
        [Test]
        public void SceneIdentityRejectsOtherScenesAcceptedVersion()
        {
            Create(1); var first = PrepareReference(); m_A.Prepare(first); m_A.Commit();
            using (var other = new MeshScene(16))
            {
                var otherGpu = new GpuScene(m_Pool, other);
                try
                {
                    otherGpu.Update(); Assert.IsNull(m_A.Prepare(otherGpu.transformVersions.current).previous);
                    m_A.Rollback();
                }
                finally { otherGpu.Dispose(); }
            }
        }
    }
}
