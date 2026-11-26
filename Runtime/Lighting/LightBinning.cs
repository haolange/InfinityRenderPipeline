using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Rendering.Spatial;

namespace InfinityTech.Rendering.LightPipeline
{
    // Clustered light list (Forward+ style) built on the shared Spatial Z-bin + tile-bitmask mechanism.
    public static class LightBinning
    {
        public const int TileSize = 16;
        public const int ZBinCount = 128;
        public const int MaxLocalLights = SpatialProjection.MaxIndexedItems;
        public const uint EmptyBin = SpatialProjection.EmptyZBin;

        public static Vector4 ZBinParams(float near, float far, int binCount)
            => SpatialProjection.ComputeZBinParams(near, far, binCount);

        public static int DepthToBin(float depth, Vector4 zBinParams)
            => SpatialProjection.DepthToZBin(depth, zBinParams);

        public static uint PackBin(int minIndex, int maxIndex)
            => SpatialProjection.PackZBin(minIndex, maxIndex);

        public static void UnpackBin(uint packed, out int minIndex, out int maxIndex)
            => SpatialProjection.UnpackZBin(packed, out minIndex, out maxIndex);

        public static void BuildZBins(NativeArray<float2> depthRanges, int count, Vector4 zBinParams, NativeArray<uint> bins)
            => SpatialZBinTileIndex.BuildZBins(depthRanges, count, zBinParams, bins);

        public static int2 TileCount(int2 screenSize)
            => SpatialProjection.TileCount(screenSize, TileSize);

        public static int WordCount(int localCount)
            => SpatialProjection.WordCount(localCount);

        public static bool TryComputeTileRect(Vector3 centerVS, float radius, Matrix4x4 gpuProjection, bool orthographic, float near,
            int2 screenSize, out int4 rect)
            => SpatialProjection.TryProjectSphereToTileRect(centerVS, radius, gpuProjection, orthographic, near, screenSize, TileSize, out rect);
    }
}
