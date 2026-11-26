using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Rendering.Spatial
{
    /// <summary>
    /// Shared view-depth binning and conservative screen-tile projection primitives.
    /// </summary>
    public static class SpatialProjection
    {
        public const int MaxIndexedItems = 0xFFFF;
        public const uint EmptyZBin = 0x0000FFFFu;

        // x = bin count, y = scale, z = bias: bin = floor(log2(depth) * scale + bias).
        public static Vector4 ComputeZBinParams(float near, float far, int binCount)
        {
            float safeNear = Mathf.Max(near, 1e-4f);
            float safeFar = Mathf.Max(far, safeNear * 1.001f);
            float scale = binCount / Mathf.Log(safeFar / safeNear, 2.0f);
            return new Vector4(binCount, scale, -Mathf.Log(safeNear, 2.0f) * scale, 0.0f);
        }

        public static int DepthToZBin(float depth, Vector4 zBinParams)
        {
            int count = (int)zBinParams.x;
            if (depth <= 0.0f || count <= 1)
            {
                return 0;
            }
            int bin = (int)math.floor(Mathf.Log(depth, 2.0f) * zBinParams.y + zBinParams.z);
            return math.clamp(bin, 0, count - 1);
        }

        public static uint PackZBin(int minIndex, int maxIndex) => (uint)minIndex | ((uint)maxIndex << 16);

        public static void UnpackZBin(uint packed, out int minIndex, out int maxIndex)
        {
            minIndex = (int)(packed & 0xFFFFu);
            maxIndex = (int)(packed >> 16);
        }

        public static int2 TileCount(int2 screenSize, int tileSize)
        {
            int safeTile = math.max(tileSize, 1);
            return (math.max(screenSize, new int2(1, 1)) + (safeTile - 1)) / safeTile;
        }

        public static int WordCount(int itemCount) => math.max(1, (itemCount + 31) / 32);

        public static bool TryProjectSphereToTileRect(Vector3 centerVS, float radius, in Matrix4x4 gpuProjection, bool orthographic,
            float near, int2 screenSize, int tileSize, out int4 tileRect)
        {
            int2 tiles = TileCount(screenSize, tileSize);
            tileRect = new int4(0, 0, tiles.x - 1, tiles.y - 1);
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
                float2 x = AxisTangents(centerVS.x, depth, radius);
                float2 y = AxisTangents(centerVS.y, depth, radius);
                float x0 = gpuProjection.m00 * x.x - gpuProjection.m02, x1 = gpuProjection.m00 * x.y - gpuProjection.m02;
                float y0 = gpuProjection.m11 * y.x - gpuProjection.m12, y1 = gpuProjection.m11 * y.y - gpuProjection.m12;
                ndcMin = new float2(math.min(x0, x1), math.min(y0, y1));
                ndcMax = new float2(math.max(x0, x1), math.max(y0, y1));
            }

            if (ndcMax.x < -1.0f || ndcMin.x > 1.0f || ndcMax.y < -1.0f || ndcMin.y > 1.0f)
            {
                return false;
            }

            tileRect = NdcBoundsToTileRect(ndcMin, ndcMax, screenSize, tileSize, tiles);
            return true;
        }

        public static bool TryProjectBoundsToTileRect(in SpatialBounds worldBounds, in Matrix4x4 worldToView, in Matrix4x4 gpuProjection,
            bool orthographic, float near, int2 screenSize, int tileSize, out int4 tileRect, out float2 depthRange)
        {
            int2 tiles = TileCount(screenSize, tileSize);
            tileRect = new int4(0, 0, tiles.x - 1, tiles.y - 1);
            float3 c = worldBounds.center;
            float3 e = worldBounds.halfExtents;

            float minDepth = float.PositiveInfinity;
            float maxDepth = float.NegativeInfinity;
            float2 ndcMin = new float2(float.PositiveInfinity, float.PositiveInfinity);
            float2 ndcMax = new float2(float.NegativeInfinity, float.NegativeInfinity);

            for (int corner = 0; corner < 8; ++corner)
            {
                float3 offset = new float3(
                    (corner & 1) != 0 ? e.x : -e.x,
                    (corner & 2) != 0 ? e.y : -e.y,
                    (corner & 4) != 0 ? e.z : -e.z);
                Vector3 cornerVS = worldToView.MultiplyPoint3x4(c + offset);
                float depth = -cornerVS.z;
                minDepth = math.min(minDepth, depth);
                maxDepth = math.max(maxDepth, depth);

                if (orthographic)
                {
                    float2 ndc = ProjectOrtho(gpuProjection, cornerVS);
                    ndcMin = math.min(ndcMin, ndc);
                    ndcMax = math.max(ndcMax, ndc);
                }
                else if (depth > near)
                {
                    float invDepth = 1.0f / depth;
                    float2 ndc = new float2(
                        gpuProjection.m00 * (cornerVS.x * invDepth) - gpuProjection.m02,
                        gpuProjection.m11 * (cornerVS.y * invDepth) - gpuProjection.m12);
                    ndcMin = math.min(ndcMin, ndc);
                    ndcMax = math.max(ndcMax, ndc);
                }
            }

            if (maxDepth < near)
            {
                depthRange = float2.zero;
                return false;
            }

            depthRange = new float2(math.max(minDepth, near), math.max(maxDepth, near));
            if (!orthographic && minDepth <= near)
            {
                return true;
            }

            if (ndcMax.x < -1.0f || ndcMin.x > 1.0f || ndcMax.y < -1.0f || ndcMin.y > 1.0f)
            {
                return false;
            }

            tileRect = NdcBoundsToTileRect(ndcMin, ndcMax, screenSize, tileSize, tiles);
            return true;
        }

        static int4 NdcBoundsToTileRect(float2 ndcMin, float2 ndcMax, int2 screenSize, int tileSize, int2 tiles)
        {
            float2 size = math.max(screenSize, new int2(1, 1));
            // One pixel of margin covers TAA jitter, which the unjittered projection does not include.
            float2 pixelMin = (math.clamp(ndcMin, -1.0f, 1.0f) * 0.5f + 0.5f) * size - 1.0f;
            float2 pixelMax = (math.clamp(ndcMax, -1.0f, 1.0f) * 0.5f + 0.5f) * size + 1.0f;
            int2 tileMin = math.clamp((int2)math.floor(pixelMin / tileSize), 0, tiles - 1);
            int2 tileMax = math.clamp((int2)math.floor(pixelMax / tileSize), 0, tiles - 1);
            return new int4(tileMin, tileMax);
        }

        static float2 AxisTangents(float lateral, float depth, float radius)
        {
            float distanceSq = lateral * lateral + depth * depth;
            float centerAngle = math.atan2(lateral, depth);
            float halfAngle = math.asin(math.min(1.0f, radius / math.sqrt(distanceSq)));
            return new float2(math.tan(centerAngle - halfAngle), math.tan(centerAngle + halfAngle));
        }

        static float2 ProjectOrtho(in Matrix4x4 projection, Vector3 positionVS)
        {
            Vector4 clip = projection * new Vector4(positionVS.x, positionVS.y, positionVS.z, 1.0f);
            return new float2(clip.x, clip.y) / clip.w;
        }
    }
}
