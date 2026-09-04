using Unity.Mathematics;

namespace InfinityTech.Rendering.Pipeline
{
    public static class PyramidMipBatch
    {
        public const int HiZMipsPerDispatch = 4;
        public const int ColorPyramidMipsPerDispatch = 2;

        public static int MipCount(int width, int height)
        {
            int maxDim = math.max(math.max(width, height), 1);
            return 1 + (int)math.floor(math.log2(maxDim));
        }

        public static int HiZBatchCount(int mipCount)
        {
            return BatchCount(mipCount, HiZMipsPerDispatch);
        }

        public static int ColorPyramidBatchCount(int mipCount)
        {
            return BatchCount(mipCount, ColorPyramidMipsPerDispatch);
        }

        public static int BatchCount(int mipCount, int mipsPerDispatch)
        {
            int count = math.max(mipCount, 1);
            int perDispatch = math.max(mipsPerDispatch, 1);
            return (count + perDispatch - 1) / perDispatch;
        }

        public static int MipSize(int dim, int mip)
        {
            return math.max(1, dim >> math.max(mip, 0));
        }
    }
}
