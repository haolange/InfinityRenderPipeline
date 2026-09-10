using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshBakedLightingTests
    {
        [Test]
        public void BakedState_RollbackRestoresDataRevisionAndDirtyRanges()
        {
            using (var scene = new MeshScene(16))
            {
                TransformId transform;
                MeshInstanceId instance;
                var first = new FMeshBakedLighting { metadata = new float4(1, 1, 0, 0), scaleOffset = new float4(0.25f, 0.5f, 0.1f, 0.2f), occlusion = new float4(0.1f, 0.2f, 0.3f, 0.4f) };
                using (var update = scene.BeginUpdate())
                {
                    transform = update.CreateTransform(float4x4.identity);
                    instance = update.CreateInstance(transform, new FBound(float3.zero, new float3(1)), ~0, 1,
                        EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                    update.SetInstanceBakedLighting(instance, first); update.Commit();
                }
                scene.ClearTransformDirtyRange(); scene.ClearBoundsDirtyRange();
                var revision = scene.ContentRevision;
                var second = first; second.metadata.x = 2; second.occlusion = new float4(1);
                using (var update = scene.BeginUpdate())
                {
                    update.SetInstanceBakedLighting(instance, second);
                    Assert.IsTrue(scene.HasTransformDirtyRange);
                    Assert.IsTrue(scene.GetTransformBakedLighting((int)transform.Index).Equals(second));
                }
                Assert.IsTrue(scene.GetTransformBakedLighting((int)transform.Index).Equals(first));
                Assert.AreEqual(revision, scene.ContentRevision);
                Assert.IsFalse(scene.HasTransformDirtyRange);
                using (var update = scene.BeginUpdate()) { update.SetInstanceBakedLighting(instance, first); update.Commit(); }
                Assert.AreEqual(revision, scene.ContentRevision, "No-op must not dirty or invalidate the scene.");
                Assert.IsFalse(scene.HasTransformDirtyRange);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void DifferentTextureSets_NeverShareOneCommandEvenWithCachedPass(bool cached)
        {
            using (var visible = new NativeList<VisibleMeshDraw>(3, Allocator.Temp))
            using (var draws = new NativeArray<MeshDrawRecord>(new[] {
                new MeshDrawRecord { meshUnityId = 10, sectionIndex = 0, materialUnityId = 20 },
                new MeshDrawRecord { meshUnityId = 10, sectionIndex = 0, materialUnityId = 20 },
                new MeshDrawRecord { meshUnityId = 10, sectionIndex = 0, materialUnityId = 20 } }, Allocator.Temp))
            using (var commands = new NativeList<MeshDrawCommand>(3, Allocator.Temp))
            using (var indices = new NativeArray<int>(3, Allocator.Temp))
            using (var slots = new NativeArray<int>(3, Allocator.Temp))
            {
                for (int i = 0; i < 3; i++)
                {
                    int set = i == 0 ? 1 : 2;
                    visible.Add(new VisibleMeshDraw { grouping = new MeshGroupingKey(10, 0, 20, 0, set),
                        passDrawId = cached ? new MeshPassDrawId(7, 1) : MeshPassDrawId.Invalid,
                        instance = new MeshInstanceId((uint)i, 1), transformIndex = i + 5, drawIndex = i });
                }
                new MeshPassBuildJob { visibleDraws = visible, draws = draws, drawCommands = commands, instanceIndices = indices, instanceSlotIndices = slots }.Execute();
                Assert.AreEqual(2, commands.Length);
                Assert.AreEqual(1, commands[0].bakedTextureSet); Assert.AreEqual(1, commands[0].countOffset.x);
                Assert.AreEqual(2, commands[1].bakedTextureSet); Assert.AreEqual(2, commands[1].countOffset.x);
                Assert.AreEqual(5, indices[0]); Assert.AreEqual(7, indices[2]);
                Assert.AreNotEqual(new MeshGroupingKey(10, 0, 20, 0, 1), new MeshGroupingKey(10, 0, 20, 0, 2));
            }
        }
    }
}
