using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class PassBinTests
    {
        [Test]
        public void Bin_IncludesEligibilityAndQueue_NotLayer()
        {
            using (var scene = new MeshScene(16))
            using (var bins = new PassBinStore(scene, new PassRegistry(), 0))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                MeshInstanceId visible = Create(update, layerMask: 0, eligibility: EPassEligibility.GBuffer, queue: 2000);
                Create(update, layerMask: ~0, eligibility: EPassEligibility.Depth, queue: 2000);
                Create(update, layerMask: ~0, eligibility: EPassEligibility.GBuffer, queue: 4000);
                update.Commit();

                NativeArray<int> members = bins.GetDrawIndices(MeshPassId.GBuffer);
                Assert.AreEqual(1, members.Length);
            }
        }

        [Test]
        public void StaticRebuild_DoesNotIncrementWhenEpochUnchanged()
        {
            using (var scene = new MeshScene(16))
            using (var bins = new PassBinStore(scene, new PassRegistry(), 0))
            {
                MeshSceneUpdate update = scene.BeginUpdate();
                Create(update, ~0, EPassEligibility.GBuffer, 2000);
                update.Commit();

                MeshPipelineDiagnostics.Reset();
                bins.EnsureRebuilt(MeshPassId.GBuffer);
                int first = MeshPipelineDiagnostics.PassBinRebuilds;
                Assert.AreEqual(1, first);
                bins.EnsureRebuilt(MeshPassId.GBuffer);
                Assert.AreEqual(1, MeshPipelineDiagnostics.PassBinRebuilds);
            }
        }

        [Test]
        public void Rollback_RestoresBinMembership()
        {
            using (var scene = new MeshScene(16))
            using (var bins = new PassBinStore(scene, new PassRegistry(), 0))
            {
                MeshSceneUpdate create = scene.BeginUpdate();
                Create(create, ~0, EPassEligibility.GBuffer, 2000);
                create.Commit();
                bins.EnsureRebuilt(MeshPassId.GBuffer);
                int before = bins.GetDrawIndices(MeshPassId.GBuffer).Length;

                MeshSceneUpdate extra = scene.BeginUpdate();
                Create(extra, ~0, EPassEligibility.GBuffer, 2100);
                extra.Rollback();

                bins.EnsureRebuilt(MeshPassId.GBuffer);
                Assert.AreEqual(before, bins.GetDrawIndices(MeshPassId.GBuffer).Length);
            }
        }

        static MeshInstanceId Create(MeshSceneUpdate update, int layerMask, EPassEligibility eligibility, int queue)
        {
            TransformId transform = update.CreateTransform(float4x4.identity);
            MeshInstanceId instance = update.CreateInstance(
                transform,
                new FBound(float3.zero, new float3(1, 1, 1)),
                layerMask,
                renderingLayerMask: 1,
                flags: EMeshInstanceFlags.Visible,
                motionType: EMotionType.Object,
                castShadow: ECastShadowMethod.Off);
            update.CreateDraw(instance, 10, 0, 20, eligibility, queue, 0);
            return instance;
        }
    }
}
