using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowAtlasLayoutTests
    {
        static bool RectsOverlap(Vector4 a, Vector4 b)
        {
            return a.x < b.x + b.z && a.x + a.z > b.x && a.y < b.y + b.w && a.y + a.w > b.y;
        }

        [Test]
        public void MixedPowerOfTwoTiles_PackWithoutOverlapOrFragmentation_WhenSortedDescending()
        {
            var layout = new ShadowAtlasLayout();
            Assert.IsTrue(layout.Configure(2048, 2048, 128, 1024));
            Assert.AreEqual(4194304L, layout.TotalTexels);

            // Mix filling 100% of 2048x2048 (4M texels):
            // 1x1024 (1M) + 6x512 (1.5M) + 16x256 (1M) + 48x128 (0.786M? wait: 48 * 16384 = 786432; 64 * 16384 = 1048576).
            // Exact 4M: 1x1024 (1M) + 6x512 (1.5M) + 16x256 (1M) + 32x128 (0.5M) => wait: 1 + 1.5 + 1 + 0.5 = 4.0 * 1024^2!
            // 1*1024^2 = 1048576; 6*512^2 = 1572864; 16*256^2 = 1048576; 32*128^2 = 524288 -> sum = 4194304!
            var requests = new List<int>();
            requests.Add(1024);
            for (int i = 0; i < 6; ++i) requests.Add(512);
            for (int i = 0; i < 16; ++i) requests.Add(256);
            for (int i = 0; i < 32; ++i) requests.Add(128);

            var rects = new List<Vector4>(requests.Count);
            foreach (int size in requests)
            {
                Assert.IsTrue(layout.TryAllocate(size, out int handle, out Vector4 rect), $"Failed to allocate {size}");
                Assert.GreaterOrEqual(handle, 0);
                Assert.AreEqual(size, rect.z);
                Assert.AreEqual(size, rect.w);
                Assert.GreaterOrEqual(rect.x, 0f);
                Assert.GreaterOrEqual(rect.y, 0f);
                Assert.LessOrEqual(rect.x + rect.z, 2048f);
                Assert.LessOrEqual(rect.y + rect.w, 2048f);
                foreach (Vector4 existing in rects)
                    Assert.IsFalse(RectsOverlap(existing, rect), $"Tile {rect} overlaps {existing}");
                rects.Add(rect);
            }

            Assert.AreEqual(55, layout.AllocatedTileCount);
            Assert.AreEqual(1.0f, layout.Occupancy, 1e-6f);
            Assert.IsFalse(layout.TryAllocate(128, out _, out _));
        }

        [Test]
        public void Free_CoalescesFourBuddySiblingsIntoLargerParentSlot()
        {
            var layout = new ShadowAtlasLayout();
            layout.Configure(2048, 2048, 128, 1024);

            // Fill all 4 1024x1024 roots with 16 x 512 tiles.
            var handles = new int[16];
            for (int i = 0; i < 16; ++i)
                Assert.IsTrue(layout.TryAllocate(512, out handles[i], out _));

            Assert.IsFalse(layout.CanAllocate(1024));

            // Free 3 siblings from one root -> still cannot allocate a 1024.
            for (int i = 0; i < 3; ++i)
                layout.Free(handles[i]);
            Assert.IsFalse(layout.CanAllocate(1024));

            // Freeing the 4th sibling coalesces the 1024 root back together.
            layout.Free(handles[3]);
            Assert.IsTrue(layout.CanAllocate(1024));
            Assert.IsTrue(layout.TryAllocate(1024, out int mergedHandle, out Vector4 mergedRect));
            Assert.AreEqual(1024f, mergedRect.z);
            Assert.GreaterOrEqual(mergedHandle, 0);
        }

        [Test]
        public void RectangularAndNonPowerOfTwoMultipleCacheAtlases_PackCleanly()
        {
            var layout = new ShadowAtlasLayout();
            layout.Configure(4096, 2048, 128, 1024);
            for (int i = 0; i < 8; ++i)
                Assert.IsTrue(layout.TryAllocate(1024, out _, out _));
            Assert.AreEqual(1.0f, layout.Occupancy, 1e-6f);

            // Legacy 3072x512 (width = 6 * 512) must support 6 x 512 faces.
            layout.Configure(3072, 512, 128, 1024);
            Assert.AreEqual(512, layout.RootBlockSize);
            for (int i = 0; i < 6; ++i)
                Assert.IsTrue(layout.TryAllocate(512, out _, out _));
            Assert.AreEqual(1.0f, layout.Occupancy, 1e-6f);
        }

        [Test]
        public void SteadyStateAllocationAndReset_AllocatesZeroManagedBytes()
        {
            var layout = new ShadowAtlasLayout();
            layout.Configure(2048, 2048, 128, 1024);

            // Warmup JIT paths.
            for (int w = 0; w < 2; ++w)
            {
                layout.Reset();
                layout.TryAllocate(512, out int h0, out _);
                layout.TryAllocate(256, out int h1, out _);
                layout.Free(h0);
                layout.Free(h1);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 200; ++frame)
            {
                layout.Reset();
                for (int i = 0; i < 4; ++i)
                {
                    layout.TryAllocate(512, out int h, out _);
                    layout.Free(h);
                }
                layout.TryAllocate(1024, out _, out _);
                layout.TryAllocate(512, out _, out _);
                layout.TryAllocate(256, out _, out _);
                layout.TryAllocate(128, out _, out _);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0L, after - before, "Steady-state ShadowAtlasLayout operations must allocate 0 managed bytes.");
        }
    }
}
