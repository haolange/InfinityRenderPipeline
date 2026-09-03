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
