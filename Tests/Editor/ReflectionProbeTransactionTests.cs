using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ReflectionProbeTransactionTests
    {
        Cubemap m_Texture;
        ReflectionProbeAtlas m_Atlas;
        const ulong TextureId = 17;
        static FProbeCubeKey Key => new FProbeCubeKey(7, TextureId);

        [SetUp]
        public void SetUp()
        {
            m_Texture = new Cubemap(16, TextureFormat.RGBAHalf, true);
            m_Atlas = new ReflectionProbeAtlas();
            m_Atlas.Configure(32, 2);
            m_Atlas.BeginFrame();
        }

        [TearDown]
        public void TearDown()
        {
            try { m_Atlas.Dispose(); }
            finally { Object.DestroyImmediate(m_Texture); }
        }

        int Acquire(uint version, int frame = 1, bool realtime = false)
        {
            Assert.IsTrue(m_Atlas.Acquire(Key, m_Texture, version, realtime, frame, out int slot));
            return slot;
        }

        void AcceptQueuedUpload()
        {
            // The production pass takes this list at record time; its observer marks production only on queue acceptance.
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
            m_Atlas.Uploads.Clear();
            m_Atlas.MarkProduced(new[] { Key }, 1);
        }

        [Test]
        public void FailedCamera_RequeuesConsumedUnacceptedUploadInSameFrame()
        {
            int slot = Acquire(1);
            m_Atlas.Uploads.Clear();
            m_Atlas.DiscardUnproducedPending();
            Assert.IsFalse(m_Atlas.IsValid(Key));
            Assert.AreEqual(slot, Acquire(1));
            Assert.AreEqual(1, m_Atlas.Uploads.Count, "the next camera must produce the missing version");
        }

        [Test]
        public void FailedCamera_PreservesAcceptedProductionForSecondCamera()
        {
            int slot = Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.DiscardUnproducedPending();
            Assert.AreEqual(slot, Acquire(1));
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
            m_Atlas.CommitFrame();
            Assert.IsTrue(m_Atlas.IsValid(Key));
        }

        [Test]
        public void SharedProbe_DeduplicatesUnrecordedUploadWithinCamera()
        {
            int slot = Acquire(1);
            Assert.AreEqual(slot, Acquire(1));
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
        }

        [Test]
        public void Discard_RemovesUnrecordedUploadsButKeepsOtherAcceptedSlots()
        {
            Acquire(1);
            AcceptQueuedUpload();
            const ulong secondId = 18;
            var secondKey = new FProbeCubeKey(8, secondId);
            Assert.IsTrue(m_Atlas.Acquire(secondKey, m_Texture, 1, false, 1, out _));
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
            m_Atlas.DiscardUnproducedPending();
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
            m_Atlas.CommitFrame();
            Assert.IsTrue(m_Atlas.IsValid(Key));
            Assert.IsFalse(m_Atlas.IsValid(secondKey));
        }

        [Test]
        public void FailedUpdate_PreservesCommittedVersionAndRetriesNewVersion()
        {
            int slot = Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            Acquire(2, 2);
            m_Atlas.Uploads.Clear();
            m_Atlas.DiscardUnproducedPending();
            Assert.IsTrue(m_Atlas.IsValid(Key), "the unqueued update did not overwrite committed contents");
            Assert.AreEqual(slot, Acquire(1, 2));
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
            Assert.AreEqual(slot, Acquire(2, 2));
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
        }

        [Test]
        public void FailedLaterUpdate_DoesNotReviveOverwrittenCommittedVersion()
        {
            int slot = Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            Acquire(2, 2);
            AcceptQueuedUpload();
            Acquire(3, 2);
            m_Atlas.Uploads.Clear();
            m_Atlas.DiscardUnproducedPending();
            Assert.IsFalse(m_Atlas.IsValid(Key), "queued version 2 already overwrites the committed version 1 slot");
            Assert.AreEqual(slot, Acquire(1, 2));
            Assert.AreEqual(1, m_Atlas.Uploads.Count, "version 1 must be produced again after its block was overwritten");
        }

        [TestCase(true), TestCase(false)]
        public void UncertainQueueAcceptance_InvalidatesOldVersionAtFrameCompletion(bool submitted)
        {
            Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            Acquire(2, 2);
            // Queue failure may occur after the GPU write is accepted but before OnQueued.
            // The camera catch must retain pending state instead of discarding this uncertain write.
            m_Atlas.Uploads.Clear();
            if (submitted) m_Atlas.CommitFrame();
            else m_Atlas.RollbackPending();
            Assert.IsFalse(m_Atlas.IsValid(Key));
            m_Atlas.BeginFrame();
            Acquire(1, 3);
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
        }

        [Test]
        public void RealtimeProbe_FiltersOncePerFrameButAgainForChangedVersion()
        {
            Acquire(1, 1, true);
            AcceptQueuedUpload();
            Acquire(1, 1, true);
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
            Acquire(2, 1, true);
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            Acquire(2, 2, true);
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
        }
        [Test]
        public void CommittedCube_ReimportsWithChangedSamplingFormatAtSameVersion()
        {
            Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            var changed = new Cubemap(16, TextureFormat.RGBA32, true);
            try
            {
                Assert.AreNotEqual(m_Texture.graphicsFormat, changed.graphicsFormat);
                Assert.IsTrue(m_Atlas.Acquire(Key, changed, 1, false, 2, out _));
                Assert.AreEqual(1, m_Atlas.Uploads.Count);
                AcceptQueuedUpload();
                m_Atlas.CommitFrame();
                m_Atlas.BeginFrame();
                Assert.IsTrue(m_Atlas.Acquire(Key, changed, 1, false, 3, out _));
                Assert.AreEqual(0, m_Atlas.Uploads.Count);
            }
            finally { Object.DestroyImmediate(changed); }
        }

        [Test]
        public void FailedContentChange_PreservesUnwrittenContentsAndRetriesChangedHash()
        {
            Hash128 original = m_Texture.imageContentsHash;
            Hash128 changed = new Hash128(1, 2, 3, 4);
            Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            m_Texture.imageContentsHash = changed;
            Acquire(1, 2);
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
            m_Atlas.Uploads.Clear();
            m_Atlas.DiscardUnproducedPending();
            m_Texture.imageContentsHash = original;
            Acquire(1, 2);
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
            m_Texture.imageContentsHash = changed;
            Acquire(1, 2);
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            Acquire(1, 3);
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
        }
    }
}
