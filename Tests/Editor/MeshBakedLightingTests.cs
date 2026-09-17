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
        [Test]
        public void DifferentTextureSets_NeverShareOneCommand()
        {
            using (var scene = new MeshScene(16))
            using (var bins = new PassBinStore(scene, new PassRegistry(), 0))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                for (int i = 0; i < 3; ++i)
                {
                    TransformId transform = update.CreateTransform(float4x4.identity);
                    MeshInstanceId instance = update.CreateInstance(
                        transform, new FBound(float3.zero, new float3(1)), ~0, 1,
                        EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                    update.SetInstanceBakedLighting(instance, new FMeshBakedLighting
                    {
                        metadata = new float4(i == 0 ? 1 : 2, 0, 0, 0)
                    });
                    update.CreateDraw(instance, 10, 0, 20, EPassEligibility.GBuffer, 2000, 0);
                }

                update.Commit();
                NativeArray<MeshPassCommand> commands = bins.GetCommands(MeshPassId.GBuffer);
                Assert.AreEqual(2, commands.Length);
                Assert.AreEqual(1, commands[0].key.bakedTextureSet);
                Assert.AreEqual(1, commands[0].memberCount);
                Assert.AreEqual(2, commands[1].key.bakedTextureSet);
                Assert.AreEqual(2, commands[1].memberCount);
            }
        }
    }
}
