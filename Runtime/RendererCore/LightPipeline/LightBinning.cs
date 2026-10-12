using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Rendering.LightPipeline
{
    // Clustered light list v2 (Forward+ style). Local lights are sorted by view depth on the CPU; each logarithmic depth
    // bin stores the [min, max] range of sorted local indices whose depth range overlaps it; each 16x16 screen tile
    // stores a bitmask of the local lights whose screen rectangle covers it (built on the GPU from the CPU rectangles).
    // A pixel visits the bits of its tile mask that fall inside its bin's index range.
    public static class LightBinning
    {
        public const int TileSize = 16;
        public const int ZBinCount = 128;
        // Local indices are 16-bit in the bin encoding.
        public const int MaxLocalLights = 0xFFFF;
        public const uint EmptyBin = 0x0000FFFFu;

        // x = bin count, y = scale, z = bias: bin = floor(log2(depth) * scale + bias).
        public static Vector4 ZBinParams(float near, float far, int binCount)
        {
            near = Mathf.Max(near, 1e-4f);
            far = Mathf.Max(far, near * 1.001f);
            float scale = binCount / Mathf.Log(far / near, 2);
            return new Vector4(binCount, scale, -Mathf.Log(near, 2) * scale, 0);
        }

        public static int DepthToBin(float depth, Vector4 zBinParams)
        {
            int count = (int)zBinParams.x;
            if (depth <= 0) return 0;
            int bin = (int)math.floor(Mathf.Log(depth, 2) * zBinParams.y + zBinParams.z);
            return math.clamp(bin, 0, count - 1);
        }

        public static uint PackBin(int minIndex, int maxIndex) => (uint)minIndex | ((uint)maxIndex << 16);

        public static void UnpackBin(uint packed, out int minIndex, out int maxIndex)
        {
            minIndex = (int)(packed & 0xFFFFu);
            maxIndex = (int)(packed >> 16);
        }

        // depthRanges[i] = (nearest, farthest) view depth of sorted local light i. Bins no light overlaps stay empty
        // (min > max).
        public static void BuildZBins(NativeArray<float2> depthRanges, int count, Vector4 zBinParams, NativeArray<uint> bins)
        {
            int binCount = (int)zBinParams.x;
            if (bins.Length < binCount) throw new ArgumentException("Bin array is smaller than the bin count.", nameof(bins));
            if (count > MaxLocalLights) throw new ArgumentOutOfRangeException(nameof(count), $"InfinityRP: at most {MaxLocalLights} local lights per camera.");
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
                int first = DepthToBin(range.x, zBinParams);
                int last = DepthToBin(range.y, zBinParams);
                for (int b = first; b <= last; ++b)
                {
                    minIndex[b] = math.min(minIndex[b], i);
                    maxIndex[b] = math.max(maxIndex[b], i);
                }
            }
            for (int b = 0; b < binCount; ++b)
                bins[b] = maxIndex[b] < 0 ? EmptyBin : PackBin(minIndex[b], maxIndex[b]);
            minIndex.Dispose();
            maxIndex.Dispose();
        }

        public static int2 TileCount(int2 screenSize) => (screenSize + (TileSize - 1)) / TileSize;

        public static int WordCount(int localCount) => math.max(1, (localCount + 31) / 32);

        // Conservative tile rectangle (inclusive min / max tile) of a sphere given in Unity view space (camera looks
        // down -Z). gpuProjection is GL.GetGPUProjectionMatrix(projection, true), the matrix compute passes use to
        // map view space onto texture rows. Returns false when the sphere is entirely behind the near plane or off
        // screen. A sphere that reaches the near plane covers every tile: its projection is unbounded.
        public static bool TryComputeTileRect(Vector3 centerVS, float radius, Matrix4x4 gpuProjection, bool orthographic, float near,
            int2 screenSize, out int4 rect)
        {
            int2 tiles = TileCount(screenSize);
            rect = new int4(0, 0, tiles.x - 1, tiles.y - 1);
            float depth = -centerVS.z;
            if (depth + radius < near)
            {
                return false;
            }

            float2 ndcMin, ndcMax;
            if (orthographic)
            {
                float2 a = ProjectOrtho(gpuProjection, new Vector3(centerVS.x - radius, centerVS.y - radius, centerVS.z));
                float2 b = ProjectOrtho(gpuProjection, new Vector3(centerVS.x + radius, centerVS.y + radius, centerVS.z));
                ndcMin = math.min(a, b);
                ndcMax = math.max(a, b);
            }
            else
            {
                if (depth - radius <= near)
                {
                    return true;
                }
                // Tangent lines from the eye to the sphere, per axis (Mara and McGuire 2013): the sphere lies in front
                // of the eye, so both tangent angles are inside (-90, 90) degrees.
                float2 x = AxisTangents(centerVS.x, depth, radius);
                float2 y = AxisTangents(centerVS.y, depth, radius);
                // ndc = P00 * (x / depth) + P01 * (y / depth) - P02 (view z = -depth); likewise for y.
                float x0 = gpuProjection.m00 * x.x - gpuProjection.m02, x1 = gpuProjection.m00 * x.y - gpuProjection.m02;
                float y0 = gpuProjection.m11 * y.x - gpuProjection.m12, y1 = gpuProjection.m11 * y.y - gpuProjection.m12;
                ndcMin = new float2(math.min(x0, x1), math.min(y0, y1));
                ndcMax = new float2(math.max(x0, x1), math.max(y0, y1));
            }

            if (ndcMax.x < -1 || ndcMin.x > 1 || ndcMax.y < -1 || ndcMin.y > 1)
            {
                return false;
            }

            float2 size = screenSize;
            // One pixel of margin covers TAA jitter, which the unjittered projection does not include.
            float2 pixelMin = (math.clamp(ndcMin, -1, 1) * 0.5f + 0.5f) * size - 1;
            float2 pixelMax = (math.clamp(ndcMax, -1, 1) * 0.5f + 0.5f) * size + 1;
            int2 tileMin = math.clamp((int2)math.floor(pixelMin / TileSize), 0, tiles - 1);
            int2 tileMax = math.clamp((int2)math.floor(pixelMax / TileSize), 0, tiles - 1);
            rect = new int4(tileMin, tileMax);
            return true;
        }

        // Tangents (x / depth) of the two lines from the eye that touch a circle of the given radius.
        static float2 AxisTangents(float lateral, float depth, float radius)
        {
            float distanceSq = lateral * lateral + depth * depth;
            float centerAngle = math.atan2(lateral, depth);
            float halfAngle = math.asin(math.min(1, radius / math.sqrt(distanceSq)));
            return new float2(math.tan(centerAngle - halfAngle), math.tan(centerAngle + halfAngle));
        }

        static float2 ProjectOrtho(Matrix4x4 projection, Vector3 positionVS)
        {
            Vector4 clip = projection * new Vector4(positionVS.x, positionVS.y, positionVS.z, 1);
            return new float2(clip.x, clip.y) / clip.w;
        }
    }
}
