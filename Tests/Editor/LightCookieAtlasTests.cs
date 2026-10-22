using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Block allocation and slot state of the light cookie atlas; no texture is uploaded.
    public class LightCookieAtlasTests
    {
        [Test]
        public void Allocator_BlocksAreAlignedToTheirSize_AndMergeBack()
        {
            var allocator = new AtlasQuadAllocator(256, 16);
            int full = allocator.FreeArea();
            Assert.IsTrue(allocator.TryAllocate(64, out Vector2Int a));
            Assert.IsTrue(allocator.TryAllocate(16, out Vector2Int b));
            Assert.IsTrue(allocator.TryAllocate(128, out Vector2Int c));
            Assert.AreEqual(0, a.x % 64); Assert.AreEqual(0, a.y % 64);
            Assert.AreEqual(0, b.x % 16); Assert.AreEqual(0, b.y % 16);
            Assert.AreEqual(0, c.x % 128); Assert.AreEqual(0, c.y % 128);
            Assert.AreEqual(full - 64 * 64 - 16 * 16 - 128 * 128, allocator.FreeArea());
            allocator.Free(b, 16);
            allocator.Free(a, 64);
            allocator.Free(c, 128);
            Assert.AreEqual(full, allocator.FreeArea());
            Assert.IsTrue(allocator.TryAllocate(256, out Vector2Int whole), "buddies merged back into the whole atlas");
            Assert.AreEqual(Vector2Int.zero, whole);
        }

        [Test]
        public void Allocator_RejectsWhenFull()
        {
            var allocator = new AtlasQuadAllocator(64, 16);
            for (int i = 0; i < 4; ++i) Assert.IsTrue(allocator.TryAllocate(32, out _));
            Assert.IsFalse(allocator.TryAllocate(16, out _));
        }

        [Test]
        public void BlockSize_RoundsUpAndClamps()
        {
            Assert.AreEqual(128, LightCookieAtlas.RequestedSize(100, 60, ECookieLayout.Projected, 512));
            Assert.AreEqual(256, LightCookieAtlas.RequestedSize(128, 128, ECookieLayout.Octahedral, 512), "octahedral squares are twice the cube face");
            Assert.AreEqual(512, LightCookieAtlas.RequestedSize(4096, 4096, ECookieLayout.Projected, 512));
            Assert.AreEqual(LightCookieAtlas.MinBlockSize, LightCookieAtlas.RequestedSize(1, 1, ECookieLayout.Projected, 512));
        }

        static bool Acquire(LightCookieAtlas atlas, ulong id, uint updateCount, int frame, out Vector4 rect)
            => atlas.Acquire(null, ECookieLayout.Projected, ECookieChannels.RGB, id, updateCount, 64, 64, frame, out rect);

        [Test]
        public void Atlas_UploadsOnce_ReuploadsOnTextureChange_RollsBackOnFailure()
        {
            var atlas = new LightCookieAtlas();
            try
            {
                atlas.Configure(256, 128);
                var key = new FCookieKey(7, ECookieLayout.Projected);

                atlas.BeginFrame();
                Assert.IsTrue(Acquire(atlas, 7, 1, 1, out Vector4 rect));
                Assert.AreEqual(64.0f / 256.0f, rect.z, 1e-6f);
                atlas.MarkProduced(new[] { key }, 1);
                atlas.CommitFrame();
                Assert.IsTrue(atlas.IsValid(key));

                atlas.BeginFrame();
                Assert.IsTrue(Acquire(atlas, 7, 1, 2, out Vector4 again));
                Assert.AreEqual(rect, again, "same block while the texture is unchanged");
                atlas.CommitFrame();
                Assert.IsTrue(atlas.IsValid(key), "an unchanged texture needs no upload");

                atlas.BeginFrame();
                Acquire(atlas, 7, 2, 3, out _);
                atlas.RollbackPending();
                Assert.IsFalse(atlas.IsValid(key), "a failed re-upload leaves the block invalid");
            }
            finally
            {
                atlas.Dispose();
            }
        }

        [Test]
        public void Atlas_EvictsBlocksUnusedThisFrame()
        {
            var atlas = new LightCookieAtlas();
            try
            {
                // A 256 atlas holds four 128 blocks.
                atlas.Configure(256, 128);
                atlas.BeginFrame();
                for (ulong id = 1; id <= 4; ++id)
                    Assert.IsTrue(atlas.Acquire(null, ECookieLayout.Projected, ECookieChannels.RGB, id, 1, 128, 128, 1, out _));
                Assert.IsFalse(atlas.Acquire(null, ECookieLayout.Projected, ECookieChannels.RGB, 5, 1, 128, 128, 1, out _), "full with blocks of this frame");
                atlas.CommitFrame();

                atlas.BeginFrame();
                Assert.IsTrue(atlas.Acquire(null, ECookieLayout.Projected, ECookieChannels.RGB, 5, 1, 128, 128, 2, out _), "a block of an earlier frame is evicted");
                Assert.AreEqual(4, atlas.EntryCount);
            }
            finally
            {
                atlas.Dispose();
            }
        }
    }
}
