using NUnit.Framework;
using Unity.Mathematics;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshViewMotionHistoryTests
    {
        static TransformId Create(MeshScene scene, float x)
        {
            using (var update = scene.BeginUpdate())
            {
                var id = update.CreateTransform(float4x4.Translate(new float3(x, 0, 0)));
                update.Commit(); return id;
            }
        }
        static void Move(MeshScene scene, TransformId id, float x)
        {
            using (var update = scene.BeginUpdate())
            { update.SetTransform(id, float4x4.Translate(new float3(x, 0, 0))); update.Commit(); }
        }
        [Test]
        public void SparseView_UsesItsOwnLastSubmission_NotInterveningView()
        {
            using (var scene = new MeshScene(16))
            {
                var id = Create(scene, 0);
                var a = new MeshViewMotionHistory(); var b = new MeshViewMotionHistory();
                a.Prepare(scene); a.Commit();
                Move(scene, id, 1); b.Prepare(scene); b.Commit();
                Move(scene, id, 2);
                Assert.AreEqual(0, a.Prepare(scene)[id.Index].c3.x);
                Assert.AreEqual(1, b.Prepare(scene)[id.Index].c3.x);
                a.Commit();
                Assert.AreEqual(2, a.Prepare(scene)[id.Index].c3.x);
                Assert.AreEqual(1, b.Prepare(scene)[id.Index].c3.x);
            }
        }
        [Test]
        public void FailedSubmission_DoesNotAdvanceObjectHistory()
        {
            using (var scene = new MeshScene(16))
            {
                var id = Create(scene, 0); var history = new MeshViewMotionHistory();
                history.Prepare(scene); history.Commit();
                Move(scene, id, 1); history.Prepare(scene); history.Rollback();
                Move(scene, id, 2);
                Assert.AreEqual(float4x4.identity, history.Prepare(scene)[id.Index]);
            }
        }
        [Test]
        public void ReusedSlot_CannotInheritDestroyedObjectsMotion()
        {
            using (var scene = new MeshScene(16))
            {
                var id = Create(scene, 0); var history = new MeshViewMotionHistory();
                history.Prepare(scene); history.Commit(); scene.FreeTransform(id);
                var reused = Create(scene, 100);
                Assert.AreEqual(id.Index, reused.Index); Assert.AreNotEqual(id.Generation, reused.Generation);
                Assert.AreEqual(default(float4x4), history.Prepare(scene)[reused.Index]);
            }
        }
        [Test]
        public void CapacityGrowthAndRollback_PreserveExistingViewPoses()
        {
            using (var scene = new MeshScene(16))
            {
                var id = Create(scene, 3); var history = new MeshViewMotionHistory();
                history.Prepare(scene); history.Commit();
                for (int i = 0; i < 20; ++i) Create(scene, i + 10);
                var previous = history.Prepare(scene);
                Assert.AreEqual(3, previous[id.Index].c3.x);
                Assert.AreEqual(default(float4x4), previous[20]);
                history.Rollback();
                Assert.AreEqual(3, history.Prepare(scene)[id.Index].c3.x);
            }
        }
        [Test]
        public void DifferentScene_WithSameSlotAndGeneration_HasNoHistory()
        {
            using (var a = new MeshScene(16)) using (var b = new MeshScene(16))
            {
                Create(a, 1); var id = Create(b, 2); var history = new MeshViewMotionHistory();
                history.Prepare(a); history.Commit();
                Assert.AreEqual(default(float4x4), history.Prepare(b)[id.Index]);
            }
        }
    }
}
