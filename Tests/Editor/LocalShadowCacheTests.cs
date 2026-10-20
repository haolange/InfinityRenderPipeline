using NUnit.Framework;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Slot state of the cached local shadow atlas; no texture is touched.
    public class LocalShadowCacheTests
    {
        LocalShadowCache m_Cache;

        [SetUp]
        public void SetUp()
        {
            m_Cache = new LocalShadowCache();
            // 2 x 1 slots of 512.
            m_Cache.Configure(1024, 512, 512);
        }

        [TearDown]
        public void TearDown() => m_Cache.Dispose();

        static readonly FShadowCacheKey A = new FShadowCacheKey(1, 0);
        static readonly FShadowCacheKey B = new FShadowCacheKey(2, 0);
        static readonly FShadowCacheKey C = new FShadowCacheKey(3, 0);

        [TestCase(EShadowUpdateMode.OnEnable)]
        [TestCase(EShadowUpdateMode.OnDemand)]
        public void DifferentSceneMasks_NeverReuseTheOtherCamerasTile(EShadowUpdateMode mode)
        {
            var preview = new FShadowCacheKey(1, 0, 1);
            var game = new FShadowCacheKey(1, 0, 1ul << 63);
            Assert.AreNotEqual(preview, game);
            int first = Frame(preview, mode, 10, 0, 1, true, out bool firstRender);
            Assert.IsTrue(firstRender);
            m_Cache.CommitFrame();
            int second = Frame(game, mode, 10, 0, 2, true, out bool secondRender);
            Assert.IsTrue(secondRender);
            Assert.AreNotEqual(first, second);
            m_Cache.CommitFrame();
            Assert.AreEqual(first, Frame(preview, mode, 10, 0, 3, true, out bool reusedRender));
            Assert.IsFalse(reusedRender);
        }

        int Frame(FShadowCacheKey key, EShadowUpdateMode mode, int hash, int version, int frame, bool produce, out bool render)
        {
            m_Cache.BeginFrame();
            int slot = m_Cache.Acquire(key, mode, hash, version, frame, out render);
            if (produce && render) m_Cache.MarkProduced(new[] { key }, 1);
            return slot;
        }

        [Test]
        [Category("Unverified_0_5")]
        public void EveryFrame_GetsNoSlot()
        {
            int slot = m_Cache.Acquire(A, EShadowUpdateMode.EveryFrame, 1, 0, 1, out bool render);
            Assert.AreEqual(-1, slot);
            Assert.IsTrue(render);
        }

        [Test]
        [Category("Unverified_0_5")]
        public void OnEnable_RendersOnce_ThenOnParameterChange()
        {
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 1, true, out bool render);
            Assert.IsTrue(render, "first use");
            m_Cache.CommitFrame();
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 2, true, out render);
            Assert.IsFalse(render, "unchanged");
            m_Cache.CommitFrame();
            Frame(A, EShadowUpdateMode.OnEnable, 11, 1, 3, true, out render);
            Assert.IsTrue(render, "moved");
            m_Cache.CommitFrame();
            Frame(A, EShadowUpdateMode.OnEnable, 11, 2, 4, true, out render);
            Assert.IsTrue(render, "re-enabled");
        }

        [Test]
        [Category("Unverified_0_5")]
        public void OnDemand_IgnoresParameters_UntilRequested()
        {
            Frame(A, EShadowUpdateMode.OnDemand, 10, 0, 1, true, out bool render);
            Assert.IsTrue(render);
            m_Cache.CommitFrame();
            Frame(A, EShadowUpdateMode.OnDemand, 99, 0, 2, true, out render);
            Assert.IsFalse(render, "a moved OnDemand light keeps its tile");
            m_Cache.CommitFrame();
            Frame(A, EShadowUpdateMode.OnDemand, 99, 1, 3, true, out render);
            Assert.IsTrue(render, "requested");
        }

        [Test]
        [Category("Unverified_0_5")]
        public void FailedFrame_RollsTheTileBack()
        {
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 1, true, out _);
            m_Cache.RollbackPending();
            Assert.IsFalse(m_Cache.IsValid(A));
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 2, true, out bool render);
            Assert.IsTrue(render, "re-rendered after a failed frame");
        }

        [Test]
        [Category("Unverified_0_5")]
        public void UnqueuedPass_DoesNotCommit()
        {
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 1, false, out _);
            m_Cache.CommitFrame();
            Assert.IsFalse(m_Cache.IsValid(A));
        }

        [Test]
        [Category("Unverified_0_5")]
        public void SecondCameraOfTheFrame_ReusesThePendingTile()
        {
            m_Cache.BeginFrame();
            m_Cache.Acquire(A, EShadowUpdateMode.OnEnable, 10, 1, 1, out bool first);
            m_Cache.MarkProduced(new[] { A }, 1);
            m_Cache.Acquire(A, EShadowUpdateMode.OnEnable, 10, 1, 1, out bool second);
            Assert.IsTrue(first);
            Assert.IsFalse(second);
            m_Cache.CommitFrame();
            Assert.IsTrue(m_Cache.IsValid(A));
        }

        [Test]
        [Category("Unverified_0_5")]
        public void FullAtlas_EvictsLeastRecentlyUsed_NeverThisFrame()
        {
            int slotA = Frame(A, EShadowUpdateMode.OnEnable, 1, 0, 1, true, out _);
            m_Cache.CommitFrame();
            int slotB = Frame(B, EShadowUpdateMode.OnEnable, 1, 0, 2, true, out _);
            m_Cache.CommitFrame();
            Assert.AreNotEqual(slotA, slotB);

            m_Cache.BeginFrame();
            int slotC = m_Cache.Acquire(C, EShadowUpdateMode.OnEnable, 1, 0, 3, out bool render);
            Assert.AreEqual(slotA, slotC, "A was used least recently");
            Assert.IsTrue(render);
            m_Cache.Acquire(B, EShadowUpdateMode.OnEnable, 1, 0, 3, out _);
            int slotA2 = m_Cache.Acquire(A, EShadowUpdateMode.OnEnable, 1, 0, 3, out _);
            Assert.AreEqual(-1, slotA2, "both slots are in use this frame");
        }

        [Test]
        [Category("Unverified_0_5")]
        public void Resize_EmptiesTheCache()
        {
            Frame(A, EShadowUpdateMode.OnEnable, 1, 0, 1, true, out _);
            m_Cache.CommitFrame();
            m_Cache.Configure(2048, 512, 512);
            Assert.AreEqual(0, m_Cache.EntryCount);
            Assert.AreEqual(4, m_Cache.SlotCount);
        }
    }
}
