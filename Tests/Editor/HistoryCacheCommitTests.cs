using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.GPUResource.Tests
{
    public class HistoryCacheCommitTests
    {
        HistoryCache m_Cache;

        [SetUp]
        public void SetUp()
        {
            RTHandles.Initialize(64, 64);
            m_Cache = new HistoryCache();
        }

        [TearDown]
        public void TearDown()
        {
            m_Cache?.Release();
            m_Cache?.ForceFlushForTeardown();
            m_Cache = null;
        }

        static TextureDescriptor MakeDescriptor(int width, int height, string name)
        {
            return new TextureDescriptor(width, height)
            {
                name = name,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = false
            };
        }

        [Test]
        public void DescriptorChange_QueuesRetired_WithoutImmediateRelease()
        {
            const int id = 11;
            TextureDescriptor small = MakeDescriptor(8, 8, "HistorySmall");
            TextureDescriptor large = MakeDescriptor(16, 16, "HistoryLarge");

            FTextureRef first = m_Cache.GetTexture(id, small, out bool created);
            Assert.IsTrue(created);
            Assert.IsNotNull(first.texture);
            Assert.AreEqual(0, m_Cache.RetiredQueuedCount);

            FTextureRef second = m_Cache.GetTexture(id, large, out bool recreated);
            Assert.IsTrue(recreated);
            Assert.IsNotNull(second.texture);
            Assert.AreNotSame(first.texture, second.texture);
            Assert.Greater(m_Cache.RetiredQueuedCount, 0);

            int queued = m_Cache.RetiredQueuedCount;
            m_Cache.FlushRetired();
            Assert.AreEqual(0, m_Cache.RetiredQueuedCount);
            Assert.Greater(queued, 0);
        }

        [Test]
        public void FailedResize_PreservesCommittedTextureAndDescriptor()
        {
            var small = MakeDescriptor(8, 8, "Committed");
            var large = MakeDescriptor(17, 13, "Pending");
            FTextureRef original = m_Cache.GetTexture(55, small);
            FTextureRef pending = m_Cache.GetWriteTexture(55, large);
            m_Cache.MarkProduced(55);
            m_Cache.RollbackPending();
            Assert.AreSame(original.texture, m_Cache.GetTexture(55, small, out bool recreated).texture);
            Assert.IsFalse(recreated);
            Assert.IsTrue(original.texture.rt.IsCreated());
            Assert.IsTrue(pending.texture.rt.IsCreated(), "Rollback must retain GPU allocations until retirement.");
            Assert.AreEqual(0, m_Cache.TextureGeneration(55));
        }

        [Test]
        public void FailedResize_PreservesCommittedBufferAndDescriptor()
        {
            var small = new BufferDescriptor(1, 16, ComputeBufferType.Structured);
            var large = new BufferDescriptor(8, 16, ComputeBufferType.Structured);
            FBufferRef original = m_Cache.GetBuffer(56, small);
            FBufferRef pending = m_Cache.GetWriteBuffer(56, large);
            m_Cache.MarkProduced(56);
            m_Cache.RollbackPending();
            Assert.AreSame(original.buffer, m_Cache.GetBuffer(56, small).buffer);
            Assert.IsTrue(original.buffer.IsValid());
            Assert.IsTrue(pending.buffer.IsValid());
            m_Cache.FlushRetired();
            Assert.IsFalse(pending.buffer.IsValid());
            Assert.IsTrue(original.buffer.IsValid());
        }

        [Test]
        public void CommitAfterRollbackPending_IsNoOp()
        {
            const int id = 22;
            TextureDescriptor descriptor = MakeDescriptor(8, 8, "HistoryCommit");

            m_Cache.GetTexture(id, descriptor, out _);
            m_Cache.GetWriteTexture(id, descriptor);
            m_Cache.MarkProduced(id);
            int generationBefore = m_Cache.TextureGeneration(id);

            m_Cache.RollbackPending();
            int retiredAfterRollback = m_Cache.RetiredQueuedCount;
            m_Cache.CommitFrame();

            Assert.AreEqual(generationBefore, m_Cache.TextureGeneration(id));
            Assert.AreEqual(retiredAfterRollback, m_Cache.RetiredQueuedCount);
        }

        [Test]
        public void Commit_SwapsPendingIntoCommitted()
        {
            const int id = 33;
            TextureDescriptor descriptor = MakeDescriptor(8, 8, "HistorySwap");

            FTextureRef committed = m_Cache.GetTexture(id, descriptor, out _);
            FTextureRef pending = m_Cache.GetWriteTexture(id, descriptor);
            Assert.AreNotSame(committed.texture, pending.texture);

            m_Cache.MarkProduced(id);
            m_Cache.CommitFrame();

            FTextureRef after = m_Cache.GetTexture(id, descriptor, out bool created);
            Assert.IsFalse(created);
            Assert.AreSame(pending.texture, after.texture);
            Assert.AreEqual(1, m_Cache.TextureGeneration(id));
        }
    }
}
