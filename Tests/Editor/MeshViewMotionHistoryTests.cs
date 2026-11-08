using NUnit.Framework;
using Unity.Mathematics;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshViewMotionHistoryTests
    {
        ResourcePool m_Pool;
        MeshWorld m_World;
        readonly MeshViewMotionHistory m_A = new MeshViewMotionHistory();
        readonly MeshViewMotionHistory m_B = new MeshViewMotionHistory();

        [SetUp]
        public void SetUp()
        {
            var shader = MeshTestUtility.LoadMeshDrawShader();
            m_Pool = new ResourcePool();
            m_World = new MeshWorld(m_Pool, shader);
        }

        [TearDown]
        public void TearDown()
        {
            m_A.Clear(); m_B.Clear();
            m_World?.Dispose(); m_Pool?.Dispose();
            m_World = null; m_Pool = null;
        }

        MeshEntityId Create(float x)
        {
            using (var update = m_World.BeginUpdate())
            {
                var id = MeshTestUtility.CreateEntity(update, new float3(x, 0, 0));
                update.Commit(); return id;
            }
        }

        void Move(MeshEntityId id, float x)
            => m_World.WriteEntityPose(id, float4x4.Translate(new float3(x, 0, 0)), MeshTestUtility.Bound(new float3(x, 0, 0)));

        void Remove(MeshEntityId id)
        {
            using (var update = m_World.BeginUpdate()) { update.RemoveEntity(id); update.Commit(); }
        }

        MeshTransformVersion PrepareReference()
        {
            m_World.UpdateResidency();
            // These tests verify reference transactions only. GPU production is covered separately.
            var version = m_World.transformVersions.current; version.queued = true; return version;
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
            Create(0); m_World.UpdateResidency(); var version = m_World.transformVersions.current;
            m_A.Prepare(version); m_A.Commit();
            Assert.IsNull(m_A.accepted); Assert.AreEqual(1, version.references);
        }

        [Test]
        public void SubmitFailure_RequiresReproduction_AndRetainsDirtyPatches()
        {
            var id = Create(0); var first = PrepareReference(); first.submitted = true;
            m_A.Prepare(first); m_A.Commit();
            Move(id, 1); m_World.UpdateResidency(); var pending = m_World.transformVersions.current;
            pending.OnQueued(); m_A.Prepare(pending);
            m_World.transformVersions.RollbackUnsubmitted(); m_A.Rollback();
            Assert.IsFalse(pending.queued); Assert.IsTrue(first.queued);
            Assert.AreEqual(1, pending.changeCount); Assert.AreSame(first, m_A.accepted);
        }

        [Test]
        public void RepeatedPoseAndRolledBackStructure_DoNotPublishAVersion()
        {
            var id = Create(3); var first = PrepareReference(); ulong revision = m_World.transformRevision;
            Move(id, 3); m_World.UpdateResidency();
            Assert.AreEqual(revision, m_World.transformRevision); Assert.AreSame(first, m_World.transformVersions.current);
            using (var update = m_World.BeginUpdate())
            {
                MeshTestUtility.CreateEntity(update, new float3(5, 0, 0));
                Assert.AreNotEqual(revision, m_World.transformRevision, "Creation advances the revision inside the transaction.");
            }
            Assert.AreEqual(revision, m_World.transformRevision);
            m_World.UpdateResidency(); Assert.AreSame(first, m_World.transformVersions.current);
        }

        [Test]
        public void ResidencyRejectsOpenTransaction_WithoutPublishingVersion()
        {
            using (var update = m_World.BeginUpdate())
            {
                MeshTestUtility.CreateEntity(update, float3.zero);
                Assert.Throws<System.InvalidOperationException>(() => m_World.UpdateResidency());
                Assert.IsNull(m_World.transformVersions.current);
            }
            Assert.DoesNotThrow(() => m_World.UpdateResidency());
        }

        [Test]
        public void SlotReuseAndGrowth_ProduceNewVersion_WithoutReplacingPinnedReferences()
        {
            var id = Create(3); var first = PrepareReference(); m_A.Prepare(first); m_A.Commit();
            Remove(id); var reused = Create(100);
            Assert.AreEqual(id.Index, reused.Index); Assert.AreNotEqual(id.Generation, reused.Generation);
            for (int i = 0; i < 20; ++i) Create(i);
            var next = PrepareReference();
            Assert.AreNotSame(first, next); Assert.AreSame(first, m_A.Prepare(next).previous);
            Assert.Greater(next.count, first.count);
        }

        [Test]
        public void WorldIdentityRejectsAnotherWorldsAcceptedVersion()
        {
            Create(1); var first = PrepareReference(); m_A.Prepare(first); m_A.Commit();
            var other = new MeshWorld(m_Pool, MeshTestUtility.LoadMeshDrawShader());
            try
            {
                other.UpdateResidency(); Assert.IsNull(m_A.Prepare(other.transformVersions.current).previous);
                m_A.Rollback();
            }
            finally { other.Dispose(); }
        }
    }
}
