using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Rendering.Spatial
{
    /// <summary>
    /// Logarithmic depth-bin range builder shared by Z-bin + tile-bitmask spatial consumers.
    /// </summary>
    public static class SpatialZBinTileIndex
    {
        public static void BuildZBins(NativeArray<float2> depthRanges, int count, Vector4 zBinParams, NativeArray<uint> bins)
        {
            int binCount = (int)zBinParams.x;
            if (bins.Length < binCount)
            {
                throw new ArgumentException("Bin array is smaller than the bin count.", nameof(bins));
            }
            if (count > SpatialProjection.MaxIndexedItems)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"InfinityRP: at most {SpatialProjection.MaxIndexedItems} Z-binned items per camera.");
            }
            var minIndex = new NativeArray<int>(binCount, Allocator.Temp);
            var maxIndex = new NativeArray<int>(binCount, Allocator.Temp);
            for (int b = 0; b < binCount; ++b)
            {
                minIndex[b] = int.MaxValue;
                maxIndex[b] = -1;
            }
            for (int i = 0; i < count; ++i)
            {
                float2 range = depthRanges[i];
                int first = SpatialProjection.DepthToZBin(range.x, zBinParams);
                int last = SpatialProjection.DepthToZBin(range.y, zBinParams);
                for (int b = first; b <= last; ++b)
                {
                    minIndex[b] = math.min(minIndex[b], i);
                    maxIndex[b] = math.max(maxIndex[b], i);
                }
            }
            for (int b = 0; b < binCount; ++b)
            {
                bins[b] = maxIndex[b] < 0 ? SpatialProjection.EmptyZBin : SpatialProjection.PackZBin(minIndex[b], maxIndex[b]);
            }
            minIndex.Dispose();
            maxIndex.Dispose();
        }
    }
}
