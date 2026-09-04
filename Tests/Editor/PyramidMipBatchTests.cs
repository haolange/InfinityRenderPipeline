using NUnit.Framework;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class PyramidMipBatchTests
    {
        [Test]
        public void MipCount_MatchesLog2PlusOne()
        {
            Assert.AreEqual(1, PyramidMipBatch.MipCount(1, 1));
            Assert.AreEqual(8, PyramidMipBatch.MipCount(128, 64));
            Assert.AreEqual(11, PyramidMipBatch.MipCount(1920, 1080));
        }

        [Test]
        public void HiZBatchCount_IsCeilMipCountDiv4()
        {
            Assert.AreEqual(1, PyramidMipBatch.HiZBatchCount(1));
            Assert.AreEqual(1, PyramidMipBatch.HiZBatchCount(4));
            Assert.AreEqual(2, PyramidMipBatch.HiZBatchCount(5));
            Assert.AreEqual(3, PyramidMipBatch.HiZBatchCount(11));
        }

        [Test]
        public void ColorPyramidBatchCount_IsCeilMipCountDiv2()
        {
            Assert.AreEqual(1, PyramidMipBatch.ColorPyramidBatchCount(1));
            Assert.AreEqual(1, PyramidMipBatch.ColorPyramidBatchCount(2));
            Assert.AreEqual(6, PyramidMipBatch.ColorPyramidBatchCount(11));
        }

        [Test]
        public void MipSize_ClampsToOne()
        {
            Assert.AreEqual(1920, PyramidMipBatch.MipSize(1920, 0));
            Assert.AreEqual(960, PyramidMipBatch.MipSize(1920, 1));
            Assert.AreEqual(1, PyramidMipBatch.MipSize(1920, 20));
        }
    }
}
