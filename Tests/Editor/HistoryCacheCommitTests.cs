using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.GPUResource.Tests
{
    public class HistoryCacheCommitTests
    {
        ResourcePool m_Pool;
        HistoryCache m_Cache;

        [SetUp]
        public void SetUp()
        {
            RTHandles.Initialize(64, 64);
            m_Pool = new ResourcePool();
            m_Cache = new HistoryCache(m_Pool);
        }

        [TearDown]
        public void TearDown()
        {
            m_Cache?.Dispose();
            m_Cache = null;
            m_Pool?.Dispose();
            m_Pool = null;
        }

        static TextureDescriptor MakeDescriptor(int width, int height, string name)
        {
            return new TextureDescriptor(width, height)
            {
                name = name,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthStencilFormat = GraphicsFormat.None,
                enableRandomWrite = false
            };
        }

        [Test]
        public void ResizeRead_PreservesAcceptedView_UntilWriterCommits()
        {
            var small = MakeDescriptor(8, 8, "Accepted");
            var large = MakeDescriptor(17, 13, "Pending");
            TextureHandle firstHandle = m_Cache.GetTexture(11, small, out bool created);
            Assert.IsTrue(created);
            TextureHandle readHandle = m_Cache.GetTexture(11, large, out bool invalid);
            Assert.IsTrue(invalid);
            Assert.AreSame(firstHandle.texture, readHandle.texture);
            Assert.AreEqual(8, readHandle.descriptor.width);
            Assert.AreEqual(0, m_Cache.RetiredQueuedCount);
            TextureHandle pendingHandle = m_Cache.GetWriteTexture(11, large);
            m_Cache.MarkProduced(11);
            m_Cache.CommitFrame();
            Assert.AreSame(pendingHandle.texture, m_Cache.GetTexture(11, large).texture);
            // The old accepted target becomes the spare; replacing it retires only that spare.
            m_Cache.GetWriteTexture(11, large);
            Assert.AreEqual(1, m_Cache.RetiredQueuedCount);
            Assert.IsTrue(firstHandle.texture.rt.IsCreated());
            m_Cache.FlushRetired(default);
            Assert.AreEqual(0, m_Cache.RetiredQueuedCount);
        }

        [Test]
        public void CapacityOnlyChange_PreservesHistoryValidityAndPreviousView()
        {
            var descriptor = MakeDescriptor(17, 13, "History");
            descriptor.sizingDomain = ETextureSizingDomain.Internal;
            m_Cache.SetTextureCapacity(new TextureCapacityContext(new Unity.Mathematics.int2(64), new Unity.Mathematics.int2(64)));
            TextureHandle acceptedHandle = m_Cache.GetTexture(12, descriptor);
            m_Cache.SetTextureCapacity(new TextureCapacityContext(new Unity.Mathematics.int2(128), new Unity.Mathematics.int2(128)));
            TextureHandle readHandle = m_Cache.GetTexture(12, descriptor, out bool invalid);
            Assert.IsFalse(invalid);
            Assert.AreSame(acceptedHandle.texture, readHandle.texture);
            Assert.AreEqual(64, readHandle.descriptor.physicalWidth);
            TextureHandle pendingHandle = m_Cache.GetWriteTexture(12, descriptor);
            Assert.AreEqual(128, pendingHandle.descriptor.physicalWidth);
            m_Cache.MarkProduced(12);
            m_Cache.CommitFrame();
            Assert.AreEqual(128, m_Cache.GetTexture(12, descriptor).descriptor.physicalWidth);
        }

        [Test]
        public void FailedResize_PreservesCommittedTextureAndDescriptor()
        {
            var small = MakeDescriptor(8, 8, "Committed");
            var large = MakeDescriptor(17, 13, "Pending");
            TextureHandle originalHandle = m_Cache.GetTexture(55, small);
            TextureHandle readDuringResizeHandle = m_Cache.GetTexture(55, large, out bool invalid);
            Assert.IsTrue(invalid);
            Assert.AreSame(originalHandle.texture, readDuringResizeHandle.texture);
            Assert.AreEqual(8, readDuringResizeHandle.descriptor.width);
            TextureHandle pendingHandle = m_Cache.GetWriteTexture(55, large);
            m_Cache.MarkProduced(55);
            m_Cache.RollbackPending();
            Assert.AreSame(originalHandle.texture, m_Cache.GetTexture(55, small, out bool recreated).texture);
            Assert.IsFalse(recreated);
            Assert.IsTrue(originalHandle.texture.rt.IsCreated());
            Assert.IsTrue(pendingHandle.texture.rt.IsCreated(), "Rollback must retain GPU allocations until retirement.");
            Assert.AreEqual(0, m_Cache.TextureGeneration(55));
        }

        [Test]
        public void FailedResize_PreservesCommittedBufferAndDescriptor()
        {
            var small = new BufferDescriptor(1, 16, GraphicsBuffer.Target.Structured);
            var large = new BufferDescriptor(8, 16, GraphicsBuffer.Target.Structured);
            BufferHandle originalHandle = m_Cache.GetBuffer(56, small);
            BufferHandle pendingHandle = m_Cache.GetWriteBuffer(56, large);
            m_Cache.MarkProduced(56);
            m_Cache.RollbackPending();
            Assert.AreSame(originalHandle.buffer, m_Cache.GetBuffer(56, small).buffer);
            Assert.IsTrue(originalHandle.buffer.IsValid());
            Assert.IsTrue(pendingHandle.buffer.IsValid());
            m_Cache.FlushRetired(default);
            Assert.IsTrue(m_Pool.BufferUsage(pendingHandle.buffer).returned, "Flush returns the discarded acquisition to the pool, which owns its lifetime.");
            Assert.IsFalse(m_Pool.BufferUsage(originalHandle.buffer).returned, "Committed history keeps its acquisition.");
            Assert.IsTrue(originalHandle.buffer.IsValid());
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

            TextureHandle committedHandle = m_Cache.GetTexture(id, descriptor, out _);
            TextureHandle pendingHandle = m_Cache.GetWriteTexture(id, descriptor);
            Assert.AreNotSame(committedHandle.texture, pendingHandle.texture);

            m_Cache.MarkProduced(id);
            m_Cache.CommitFrame();

            TextureHandle afterHandle = m_Cache.GetTexture(id, descriptor, out bool created);
            Assert.IsFalse(created);
            Assert.AreSame(pendingHandle.texture, afterHandle.texture);
            Assert.AreEqual(1, m_Cache.TextureGeneration(id));
        }
    }
}
