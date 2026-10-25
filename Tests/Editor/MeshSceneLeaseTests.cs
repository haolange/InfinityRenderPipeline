using System;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Profiling;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshSceneLeaseTests
    {
        [Test]
        public void CopiedExpiredLeaseCannotMutateOrCancelAnotherTransaction()
        {
            using (var scene = new MeshScene(16))
            {
                var first = scene.BeginUpdate();
                var copy = first;
                first.Commit();
                using (var current = scene.BeginUpdate())
                {
                    Assert.Throws<ObjectDisposedException>(() => copy.CreateTransform(float4x4.identity));
                    copy.Dispose();
                    current.CreateTransform(float4x4.identity);
                    current.Commit();
                }
                Assert.AreEqual(1, scene.TransformCount);
            }
        }

        [Test]
        public void CopiedLeaseRollbackIsExactlyOnce()
        {
            using (var scene = new MeshScene(16))
            {
                var transaction = scene.BeginUpdate();
                var copy = transaction;
                transaction.CreateTransform(float4x4.identity);
                copy.Dispose();
                transaction.Dispose();
                Assert.AreEqual(0, scene.TransformCount);
                Assert.Throws<ObjectDisposedException>(() => transaction.Commit());
            }
        }

        [Test]
        public void RepeatedMotionTransactionsAllocateNoManagedMemoryAfterWarmup()
        {
            using (var scene = new MeshScene(16))
            {
                TransformId id;
                using (var update = scene.BeginUpdate()) { id = update.CreateTransform(float4x4.identity); update.Commit(); }
                for (int i = 0; i < 8; ++i)
                    using (var update = scene.BeginUpdate()) { update.SetTransform(id,float4x4.Translate(new float3(i,0,0))); update.Commit(); }
                using var allocations = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame);
                allocations.Start();
                var calibration = new byte[1024];
                allocations.Stop();
                GC.KeepAlive(calibration);
                Assert.Greater(allocations.GetSample(0).Count, 0, "Allocation recorder must observe a known allocation.");
                allocations.Reset(); allocations.Start();
                for (int i = 0; i < 512; ++i)
                    using (var update = scene.BeginUpdate()) { update.SetTransform(id,float4x4.Translate(new float3(i,0,0))); update.Commit(); }
                allocations.Stop();
                Assert.AreEqual(0, allocations.Count == 0 ? 0 : allocations.GetSample(0).Count);
            }
        }
    }
}
