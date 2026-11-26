using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Rendering.Spatial
{
    public readonly struct SpatialClusterGridDesc
    {
        public readonly int tileSize;
        public readonly int zSlices;
        public readonly int2 screenSize;
        public readonly int2 tileCount;
        public readonly float near;
        public readonly float far;
        // x = screenWidth, y = screenHeight, z = tileCount.x, w = tileCount.y
        public readonly Vector4 clusterParams;
        // x = zSlices, y = logScale, z = logBias, w = tileSize
        public readonly Vector4 zSliceParams;

        public int TotalClusters => tileCount.x * tileCount.y * zSlices;

        public SpatialClusterGridDesc(int2 screenSize, float near, float far, int tileSize = 64, int zSlices = 16)
        {
            this.tileSize = math.max(tileSize, 1);
            this.zSlices = math.max(zSlices, 1);
            this.screenSize = math.max(screenSize, new int2(1, 1));
            tileCount = SpatialProjection.TileCount(this.screenSize, this.tileSize);
            this.near = Mathf.Max(near, 1e-4f);
            this.far = Mathf.Max(far, this.near * 1.001f);
            Vector4 zBins = SpatialProjection.ComputeZBinParams(this.near, this.far, this.zSlices);
            clusterParams = new Vector4(this.screenSize.x, this.screenSize.y, tileCount.x, tileCount.y);
            zSliceParams = new Vector4(this.zSlices, zBins.y, zBins.z, this.tileSize);
        }
    }

    /// <summary>
    /// Priority-preserving 3D frustum cluster list. Items added in priority order remain ordered by priority inside every
    /// overlapping cluster while keeping zero steady-state managed heap allocations.
    /// </summary>
    internal sealed class SpatialClusterIndex : IDisposable
    {
        SpatialClusterGridDesc m_Desc;
        NativeList<int4> m_ItemTileRects;
        NativeList<int2> m_ItemZRanges;
        NativeList<uint> m_Counts;
        NativeList<uint> m_Cursors;
        NativeList<uint2> m_Headers;
        NativeList<uint> m_Indices;

        GraphicsBuffer m_HeaderBuffer;
        GraphicsBuffer m_IndexBuffer;
        int m_HeaderCapacity;
        int m_IndexCapacity;
        int m_TotalIndices;
        readonly List<GraphicsBuffer> m_RetiredBuffers = new List<GraphicsBuffer>();

        internal SpatialClusterIndex()
        {
            m_ItemTileRects = new NativeList<int4>(16, Allocator.Persistent);
            m_ItemZRanges = new NativeList<int2>(16, Allocator.Persistent);
            m_Counts = new NativeList<uint>(256, Allocator.Persistent);
            m_Cursors = new NativeList<uint>(256, Allocator.Persistent);
            m_Headers = new NativeList<uint2>(256, Allocator.Persistent);
            m_Indices = new NativeList<uint>(256, Allocator.Persistent);
            m_HeaderCapacity = 1;
            m_IndexCapacity = 1;
            m_HeaderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_HeaderCapacity, sizeof(uint) * 2);
            m_IndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_IndexCapacity, sizeof(uint));
        }

        internal SpatialClusterGridDesc Grid => m_Desc;
        internal int ItemCount => m_ItemTileRects.Length;
        internal int ClusterCount => m_Desc.TotalClusters;
        internal int IndexCount => m_TotalIndices;
        internal GraphicsBuffer HeaderBuffer => m_HeaderBuffer;
        internal GraphicsBuffer IndexBuffer => m_IndexBuffer;
        internal NativeArray<uint2> Headers => m_Headers.AsArray();
        internal NativeArray<uint> Indices => m_Indices.AsArray();

        internal void FlushRetired()
        {
            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
            {
                m_RetiredBuffers[i].Release();
            }
            m_RetiredBuffers.Clear();
        }

        internal void BeginBuild(int2 screenSize, float near, float far, int tileSize = 64, int zSlices = 16)
        {
            m_Desc = new SpatialClusterGridDesc(screenSize, near, far, tileSize, zSlices);
            m_ItemTileRects.Clear();
            m_ItemZRanges.Clear();
            m_TotalIndices = 0;

            int clusterCount = math.max(1, m_Desc.TotalClusters);
            m_Counts.ResizeUninitialized(clusterCount);
            m_Cursors.ResizeUninitialized(clusterCount);
            m_Headers.ResizeUninitialized(clusterCount);
            for (int c = 0; c < clusterCount; ++c)
            {
                m_Counts[c] = 0;
            }
        }

        internal void AddItem(int4 tileRect, float2 depthRange)
        {
            if (tileRect.x > tileRect.z || tileRect.y > tileRect.w)
            {
                m_ItemTileRects.Add(new int4(1, 1, 0, 0));
                m_ItemZRanges.Add(new int2(1, 0));
                return;
            }
            int tilesX = m_Desc.tileCount.x;
            int tilesY = m_Desc.tileCount.y;
            int4 clampedRect = new int4(
                math.clamp(tileRect.x, 0, tilesX - 1),
                math.clamp(tileRect.y, 0, tilesY - 1),
                math.clamp(tileRect.z, 0, tilesX - 1),
                math.clamp(tileRect.w, 0, tilesY - 1));
            int zFirst = SpatialProjection.DepthToZBin(depthRange.x, m_Desc.zSliceParams);
            int zLast = math.max(zFirst, SpatialProjection.DepthToZBin(depthRange.y, m_Desc.zSliceParams));

            m_ItemTileRects.Add(clampedRect);
            m_ItemZRanges.Add(new int2(zFirst, zLast));

            int layerStride = tilesX * tilesY;
            for (int z = zFirst; z <= zLast; ++z)
            {
                int zBase = z * layerStride;
                for (int y = clampedRect.y; y <= clampedRect.w; ++y)
                {
                    int rowBase = zBase + y * tilesX;
                    for (int x = clampedRect.x; x <= clampedRect.z; ++x)
                    {
                        m_Counts[rowBase + x]++;
                    }
                }
            }
        }

        internal void FinalizeBuild()
        {
            int clusterCount = math.max(1, m_Desc.TotalClusters);
            uint offset = 0;
            for (int c = 0; c < clusterCount; ++c)
            {
                uint count = m_Counts[c];
                m_Headers[c] = new uint2(offset, count);
                m_Cursors[c] = offset;
                offset += count;
            }
            m_TotalIndices = (int)offset;
            int neededIndices = math.max(1, m_TotalIndices);
            m_Indices.ResizeUninitialized(neededIndices);
            if (m_TotalIndices == 0)
            {
                m_Indices[0] = 0;
            }

            int tilesX = m_Desc.tileCount.x;
            int tilesY = m_Desc.tileCount.y;
            int layerStride = tilesX * tilesY;
            int itemCount = m_ItemTileRects.Length;
            for (int item = 0; item < itemCount; ++item)
            {
                int4 rect = m_ItemTileRects[item];
                int2 zRange = m_ItemZRanges[item];
                for (int z = zRange.x; z <= zRange.y; ++z)
                {
                    int zBase = z * layerStride;
                    for (int y = rect.y; y <= rect.w; ++y)
                    {
                        int rowBase = zBase + y * tilesX;
                        for (int x = rect.x; x <= rect.z; ++x)
                        {
                            int cluster = rowBase + x;
                            uint writeAt = m_Cursors[cluster];
                            m_Cursors[cluster] = writeAt + 1u;
                            m_Indices[(int)writeAt] = (uint)item;
                        }
                    }
                }
            }

            EnsureBufferCapacity(ref m_HeaderBuffer, ref m_HeaderCapacity, clusterCount, sizeof(uint) * 2);
            EnsureBufferCapacity(ref m_IndexBuffer, ref m_IndexCapacity, neededIndices, sizeof(uint));
        }

        void EnsureBufferCapacity(ref GraphicsBuffer buffer, ref int capacity, int required, int stride)
        {
            if (capacity >= required && buffer != null)
            {
                return;
            }
            int nextCapacity = math.max(required, math.max(1, capacity) * 2);
            var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nextCapacity, stride);
            if (buffer != null)
            {
                m_RetiredBuffers.Add(buffer);
            }
            buffer = replacement;
            capacity = nextCapacity;
        }

        public void Dispose()
        {
            FlushRetired();
            m_HeaderBuffer?.Release();
            m_HeaderBuffer = null;
            m_IndexBuffer?.Release();
            m_IndexBuffer = null;
            if (m_ItemTileRects.IsCreated) m_ItemTileRects.Dispose();
            if (m_ItemZRanges.IsCreated) m_ItemZRanges.Dispose();
            if (m_Counts.IsCreated) m_Counts.Dispose();
            if (m_Cursors.IsCreated) m_Cursors.Dispose();
            if (m_Headers.IsCreated) m_Headers.Dispose();
            if (m_Indices.IsCreated) m_Indices.Dispose();
        }
    }
}
