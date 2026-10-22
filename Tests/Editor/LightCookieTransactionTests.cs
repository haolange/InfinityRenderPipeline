using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightCookieTransactionTests
    {
        Texture2D m_Texture;
        LightCookieAtlas m_Atlas;
        const ulong TextureId = 17;
        static FCookieKey Key => new FCookieKey(TextureId, ECookieLayout.Projected);

        [SetUp]
        public void SetUp()
        {
            m_Texture = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            m_Atlas = new LightCookieAtlas();
            m_Atlas.Configure(64, 16);
            m_Atlas.BeginFrame();
        }

        [TearDown]
        public void TearDown()
        {
            try { m_Atlas.Dispose(); }
            finally { Object.DestroyImmediate(m_Texture); }
        }

        Vector4 Acquire(uint version, int frame = 1)
        {
            Assert.IsTrue(m_Atlas.Acquire(m_Texture, ECookieLayout.Projected, ECookieChannels.RGB,
                TextureId, version, 16, 16, frame, out Vector4 rect));
            return rect;
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
            Vector4 rect = Acquire(1);
            m_Atlas.Uploads.Clear();
            m_Atlas.DiscardUnproducedPending();
            Assert.IsFalse(m_Atlas.IsValid(Key));
            Assert.AreEqual(rect, Acquire(1));
            Assert.AreEqual(1, m_Atlas.Uploads.Count, "the next camera must produce the missing version");
        }

        [Test]
        public void FailedCamera_PreservesAcceptedProductionForSecondCamera()
        {
            Vector4 rect = Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.DiscardUnproducedPending();
            Assert.AreEqual(rect, Acquire(1));
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
            m_Atlas.CommitFrame();
            Assert.IsTrue(m_Atlas.IsValid(Key));
        }

        [Test]
        public void SharedTexture_DeduplicatesUnrecordedUploadWithinCamera()
        {
            Vector4 rect = Acquire(1);
            Assert.AreEqual(rect, Acquire(1));
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
        }

        [Test]
        public void Discard_RemovesUnrecordedUploadsButKeepsOtherAcceptedBlocks()
        {
            Acquire(1);
            AcceptQueuedUpload();
            const ulong secondId = 18;
            var secondKey = new FCookieKey(secondId, ECookieLayout.Projected);
            Assert.IsTrue(m_Atlas.Acquire(m_Texture, ECookieLayout.Projected, ECookieChannels.RGB,
                secondId, 1, 16, 16, 1, out _));
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
            Vector4 rect = Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            Acquire(2, 2);
            m_Atlas.Uploads.Clear();
            m_Atlas.DiscardUnproducedPending();
            Assert.IsTrue(m_Atlas.IsValid(Key), "the unqueued update did not overwrite committed contents");
            Assert.AreEqual(rect, Acquire(1, 2));
            Assert.AreEqual(0, m_Atlas.Uploads.Count);
            Assert.AreEqual(rect, Acquire(2, 2));
            Assert.AreEqual(1, m_Atlas.Uploads.Count);
        }

        [Test]
        public void FailedLaterUpdate_DoesNotReviveOverwrittenCommittedVersion()
        {
            Vector4 rect = Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            Acquire(2, 2);
            AcceptQueuedUpload();
            Acquire(3, 2);
            m_Atlas.Uploads.Clear();
            m_Atlas.DiscardUnproducedPending();
            Assert.IsFalse(m_Atlas.IsValid(Key), "queued version 2 already overwrites the committed version 1 block");
            Assert.AreEqual(rect, Acquire(1, 2));
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
        public void CommittedTexture_ReuploadsWhenImageContentsChangeWithoutUpdateCount()
        {
            Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            m_Texture.imageContentsHash = new Hash128(1, 2, 3, 4);
            Acquire(1, 2);
            Assert.AreEqual(1, m_Atlas.Uploads.Count, "asset reimport need not increment Texture.updateCount");
        }

        [Test]
        public void CommittedTexture_ReuploadsWhenSamplingFormatChangesWithoutUpdateCount()
        {
            Acquire(1);
            AcceptQueuedUpload();
            m_Atlas.CommitFrame();
            m_Atlas.BeginFrame();
            var srgb = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
            try
            {
                Assert.AreNotEqual(m_Texture.graphicsFormat, srgb.graphicsFormat);
                Assert.IsTrue(m_Atlas.Acquire(srgb, ECookieLayout.Projected, ECookieChannels.RGB,
                    TextureId, 1, 16, 16, 2, out _));
                Assert.AreEqual(1, m_Atlas.Uploads.Count, "sRGB decoding changes the atlas contents");
            }
            finally { Object.DestroyImmediate(srgb); }
        }

        [Test]
        public void FailedContentChange_RetriesNewContentsAndPreservesUnwrittenOldContents()
        {
            Hash128 original = m_Texture.imageContentsHash;
            var changed = new Hash128(5, 6, 7, 8);
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
            Assert.AreEqual(0, m_Atlas.Uploads.Count, "the committed content state must be reused");
        }
    }
}
