using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshOcclusionGraphTests
    {
        [Test]
        public void LogicalHistoryKeyChanges_PhysicalCapacityDoesNotGrowPerHistoryVersion()
        {
            var pool = new ResourcePool();
            var storage = new MeshGpuVisibilityCache(pool);
            var first = new MeshView { viewKey = 101, visibilityPhase = EMeshVisibilityPhase.First, visibilityHistory = 1 };
            var next = first; next.visibilityHistory = 2;
            var final = first; final.visibilityPhase = EMeshVisibilityPhase.Final;
            try
            {
                Assert.AreNotEqual(MeshGpuVisibilityCache.MakeKey(first, true), MeshGpuVisibilityCache.MakeKey(next, true));
                Assert.AreEqual(MeshGpuVisibilityCache.MakeKey(first), MeshGpuVisibilityCache.MakeKey(next));
                var a = storage.GetBufferHandle(first, 16); var b = storage.GetBufferHandle(next, 16);
                Assert.AreSame(a.buffer, b.buffer);
                Assert.AreNotSame(a.buffer, storage.GetBufferHandle(final, 16).buffer);
                Assert.AreEqual(2, storage.allocatedViewCount);
                storage.ReleaseView(first.viewKey);
                Assert.AreEqual(0, storage.allocatedViewCount);
                Assert.IsTrue(a.buffer.IsValid(), "View removal logically retires storage before the submission owner flushes it.");
                storage.FlushRetired();
                Assert.AreEqual(2, pool.buffers.allocationCount);
            }
            finally { storage.Dispose(); pool.Dispose(); }
        }
    }
}
