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

        static LocalShadowCache.SamplingState State(int face, int count, long production, float x) =>
            new LocalShadowCache.SamplingState
            {
                faceCount = count, production = production,
                slice = new FLocalShadowSlice { face = face, valid = true, shadowMatrix = UnityEngine.Matrix4x4.Translate(new UnityEngine.Vector3(x, 2, 3)) },
                gpu = new FLocalShadowSliceGPU { depthParams = new UnityEngine.Vector4(x, 2, 3, 4),
                    momentParams = new UnityEngine.Vector4(1 / x, 0, 0, 0), shadowOriginSize = new UnityEngine.Vector4(x, 2, 3, .5f) }
            };

        void ProduceGroup(int count, int version, int frame, long generation, bool queued = true)
        {
            for (int face = 0; face < count; ++face)
            {
                var key = new FShadowCacheKey(1, face);
                m_Cache.Acquire(key, EShadowUpdateMode.OnDemand, frame, version, frame, out bool render);
                Assert.IsTrue(render);
                m_Cache.SetPendingSampling(key, State(face, count, generation, frame));
                if (queued) m_Cache.MarkProduced(new[] { key }, 1);
            }
        }

        [Test]
        public void OnEnable_OverwrittenPendingDepthCannotFallBackToCommittedHash()
        {
            Frame(A, EShadowUpdateMode.OnEnable, 10, 0, 1, true, out _);
            m_Cache.CommitFrame();
            m_Cache.Acquire(A, EShadowUpdateMode.OnEnable, 20, 0, 2, out bool moved);
            Assert.IsTrue(moved);
            m_Cache.MarkProduced(new[] { A }, 1);
            m_Cache.Acquire(A, EShadowUpdateMode.OnEnable, 10, 0, 2, out bool restored);
            Assert.IsTrue(restored, "queued B has overwritten committed A's physical slot");
        }

        [Test]
        public void SameFrame_UnqueuedTileIsRenderedAgain()
        {
            m_Cache.Acquire(A, EShadowUpdateMode.OnDemand, 1, 0, 1, out bool first);
            m_Cache.SetPendingSampling(A, State(0, 1, 10, 1));
            m_Cache.Acquire(A, EShadowUpdateMode.OnDemand, 1, 0, 1, out bool second);
            Assert.IsTrue(first);
            Assert.IsTrue(second, "Acquire alone never publishes a depth tile");
        }

        [Test]
        public void QueuedOnDemandSampling_IgnoresSameFrameHashChanges()
        {
            ProduceGroup(1, 0, 1, 10);
            Assert.IsNotNull(m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 1));
            m_Cache.Acquire(A, EShadowUpdateMode.OnDemand, 999, 0, 1, out bool render);
            Assert.IsFalse(render);
            Assert.AreEqual(10, m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 1)[0].production);
        }

        [Test]
        public void IncompletePendingCube_IsInvalidatedBeforeAnyFaceCanHit()
        {
            m_Cache.Configure(3072, 512, 512);
            m_Cache.Acquire(A, EShadowUpdateMode.OnDemand, 1, 0, 1, out _);
            m_Cache.SetPendingSampling(A, State(0, 6, 10, 1));
            m_Cache.MarkProduced(new[] { A }, 1);
            Assert.IsNull(m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 1));
            m_Cache.Acquire(A, EShadowUpdateMode.OnDemand, 1, 0, 1, out bool render);
            Assert.IsTrue(render);
        }

        [Test]
        public void OnDemandSampling_IsPairedWithQueuedDepth_AndRequestReplacesIt()
        {
            ProduceGroup(1, 0, 1, 10);
            var pending = m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 1);
            Assert.AreEqual(1, pending.Length);
            m_Cache.CommitFrame();
            var hit = m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 2);
            Assert.AreEqual(State(0, 1, 10, 1).slice.shadowMatrix, hit[0].slice.shadowMatrix);
            Assert.AreEqual(State(0, 1, 10, 1).gpu.shadowOriginSize, hit[0].gpu.shadowOriginSize);
            Assert.AreEqual(State(0, 1, 10, 1).gpu.depthParams, hit[0].gpu.depthParams);
            Assert.AreEqual(State(0, 1, 10, 1).gpu.momentParams, hit[0].gpu.momentParams);
            Assert.IsNull(m_Cache.ResolveSampling(1, ulong.MaxValue, 1, 2));
            ProduceGroup(1, 1, 2, 11);
            m_Cache.CommitFrame();
            Assert.AreEqual(2, m_Cache.ResolveSampling(1, ulong.MaxValue, 1, 3)[0].gpu.shadowOriginSize.x);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Sampling_UnqueuedOrRolledBackDepth_IsNeverPublished(bool queued)
        {
            ProduceGroup(1, 0, 1, 10, queued);
            if (queued) m_Cache.RollbackPending();
            Assert.IsNull(m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 2));
        }

        [Test]
        public void CubeSampling_RequiresAllSixFacesOfTheSameProduction()
        {
            m_Cache.Configure(3072, 512, 512);
            ProduceGroup(6, 0, 1, 10);
            Assert.AreEqual(6, m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 1).Length);
            m_Cache.CommitFrame();
            var face = new FShadowCacheKey(1, 3);
            m_Cache.Acquire(face, EShadowUpdateMode.OnEnable, 99, 0, 2, out bool render);
            Assert.IsTrue(render);
            m_Cache.SetPendingSampling(face, State(3, 6, 11, 2));
            m_Cache.MarkProduced(new[] { face }, 1);
            m_Cache.CommitFrame();
            Assert.IsNull(m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 3), "five old plus one new is not one cube production");
            for (int i = 0; i < 6; ++i) Assert.IsFalse(m_Cache.IsValid(new FShadowCacheKey(1, i)));
        }

        [Test]
        public void CubeSampling_OrphanFacesAreInvalidated_AndInsufficientCapacityReservesNone()
        {
            Assert.IsFalse(m_Cache.CanAcquireGroup(1, ulong.MaxValue, 6, 1));
            Assert.AreEqual(0, m_Cache.EntryCount);
            var orphan = new FShadowCacheKey(1, 1);
            m_Cache.Acquire(orphan, EShadowUpdateMode.OnDemand, 1, 0, 1, out _);
            m_Cache.SetPendingSampling(orphan, State(1, 6, 10, 1));
            m_Cache.MarkProduced(new[] { orphan }, 1);
            m_Cache.CommitFrame();
            Assert.IsNull(m_Cache.ResolveSampling(1, ulong.MaxValue, 0, 2));
            Assert.IsFalse(m_Cache.IsValid(orphan));
        }

        [Test]
        public void EveryFrame_GetsNoSlot()
        {
            int slot = m_Cache.Acquire(A, EShadowUpdateMode.EveryFrame, 1, 0, 1, out bool render);
            Assert.AreEqual(-1, slot);
            Assert.IsTrue(render);
        }

        [Test]
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
        public void FailedFrame_RollsTheTileBack()
        {
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 1, true, out _);
            m_Cache.RollbackPending();
            Assert.IsFalse(m_Cache.IsValid(A));
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 2, true, out bool render);
            Assert.IsTrue(render, "re-rendered after a failed frame");
        }

        [Test]
        public void UnqueuedPass_DoesNotCommit()
        {
            Frame(A, EShadowUpdateMode.OnEnable, 10, 1, 1, false, out _);
            m_Cache.CommitFrame();
            Assert.IsFalse(m_Cache.IsValid(A));
        }

        [Test]
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
