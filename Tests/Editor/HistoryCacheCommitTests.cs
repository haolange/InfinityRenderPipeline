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
        public void ResizeRead_PreservesAcceptedView_UntilWriterCommits()
        {
            var small = MakeDescriptor(8, 8, "Accepted");
            var large = MakeDescriptor(17, 13, "Pending");
            FTextureRef first = m_Cache.GetTexture(11, small, out bool created);
            Assert.IsTrue(created);
            FTextureRef read = m_Cache.GetTexture(11, large, out bool invalid);
            Assert.IsTrue(invalid);
            Assert.AreSame(first.texture, read.texture);
            Assert.AreEqual(8, read.descriptor.width);
            Assert.AreEqual(0, m_Cache.RetiredQueuedCount);
            FTextureRef pending = m_Cache.GetWriteTexture(11, large);
            m_Cache.MarkProduced(11);
            m_Cache.CommitFrame();
            Assert.AreSame(pending.texture, m_Cache.GetTexture(11, large).texture);
            // The old accepted target becomes the spare; replacing it retires only that spare.
            m_Cache.GetWriteTexture(11, large);
            Assert.AreEqual(1, m_Cache.RetiredQueuedCount);
            Assert.IsTrue(first.texture.rt.IsCreated());
            m_Cache.FlushRetired();
            Assert.AreEqual(0, m_Cache.RetiredQueuedCount);
        }

        [Test]
        public void CapacityOnlyChange_PreservesHistoryValidityAndPreviousView()
        {
            var descriptor = MakeDescriptor(17, 13, "History");
            descriptor.sizingDomain = ETextureSizingDomain.Internal;
            m_Cache.SetTextureCapacity(new FTextureCapacityContext(new Unity.Mathematics.int2(64), new Unity.Mathematics.int2(64)));
            FTextureRef accepted = m_Cache.GetTexture(12, descriptor);
            m_Cache.SetTextureCapacity(new FTextureCapacityContext(new Unity.Mathematics.int2(128), new Unity.Mathematics.int2(128)));
            FTextureRef read = m_Cache.GetTexture(12, descriptor, out bool invalid);
            Assert.IsFalse(invalid);
            Assert.AreSame(accepted.texture, read.texture);
            Assert.AreEqual(64, read.descriptor.physicalWidth);
            FTextureRef pending = m_Cache.GetWriteTexture(12, descriptor);
            Assert.AreEqual(128, pending.descriptor.physicalWidth);
            m_Cache.MarkProduced(12);
            m_Cache.CommitFrame();
            Assert.AreEqual(128, m_Cache.GetTexture(12, descriptor).descriptor.physicalWidth);
        }

        [Test]
        public void FailedResize_PreservesCommittedTextureAndDescriptor()
        {
            var small = MakeDescriptor(8, 8, "Committed");
            var large = MakeDescriptor(17, 13, "Pending");
            FTextureRef original = m_Cache.GetTexture(55, small);
            FTextureRef readDuringResize = m_Cache.GetTexture(55, large, out bool invalid);
            Assert.IsTrue(invalid);
            Assert.AreSame(original.texture, readDuringResize.texture);
            Assert.AreEqual(8, readDuringResize.descriptor.width);
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
