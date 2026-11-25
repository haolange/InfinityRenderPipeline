using System;
using UnityEngine;

namespace InfinityTech.Rendering.LightPipeline
{
    /// <summary>
    /// Fixed-footprint power-of-two quadtree/buddy allocator for shared square or rectangular shadow atlases.
    /// Pre-allocates flat node arrays on Configure; Reset, TryAllocate and Free perform zero managed heap allocations.
    /// </summary>
    internal sealed class ShadowAtlasLayout
    {
        const byte StateFree = 0;
        const byte StateSplit = 1;
        const byte StateUsed = 2;

        byte[] m_State = Array.Empty<byte>();
        int[] m_NodeSize = Array.Empty<int>();
        int[] m_MaxFreeSize = Array.Empty<int>();
        int[] m_ParentNode = Array.Empty<int>();
        int[] m_FirstChildNode = Array.Empty<int>();
        int[] m_NodeLevel = Array.Empty<int>();
        Vector4[] m_NodeRect = Array.Empty<Vector4>();

        int m_RootCount;
        int m_NodesPerTree;
        int m_TotalNodes;
        int m_LevelCount;

        public int AtlasWidth { get; private set; }
        public int AtlasHeight { get; private set; }
        public int MinTileSize { get; private set; }
        public int MaxTileSize { get; private set; }
        public int RootBlockSize { get; private set; }
        public int RootColumns { get; private set; }
        public int RootRows { get; private set; }
        public int MaxMinTileCapacity { get; private set; }
        public long TotalTexels { get; private set; }
        public long AllocatedTexels { get; private set; }
        public int AllocatedTileCount { get; private set; }
        public float Occupancy => TotalTexels > 0 ? (float)((double)AllocatedTexels / TotalTexels) : 0f;

        public static int ClampPowerOfTwo(int value, int min, int max)
        {
            if (max < min)
                return min;
            int clamped = Mathf.Clamp(value, min, max);
            int floorPow = PreviousPowerOfTwo(clamped);
            return Mathf.Clamp(floorPow, min, max);
        }

        public static int PreviousPowerOfTwo(int value)
        {
            if (value <= 1)
                return 1;
            int power = 1;
            while ((power << 1) <= value && power < (1 << 29))
                power <<= 1;
            return power;
        }

        public bool Configure(int atlasWidth, int atlasHeight, int minTileSize, int maxTileSize)
        {
            atlasWidth = Mathf.Max(0, atlasWidth);
            atlasHeight = Mathf.Max(0, atlasHeight);
            minTileSize = PreviousPowerOfTwo(Mathf.Max(1, minTileSize));
            maxTileSize = PreviousPowerOfTwo(Mathf.Max(minTileSize, maxTileSize));

            // Root block size must be a power of two that divides both atlas dimensions cleanly (or at most maxTileSize).
            int rootBlock = maxTileSize;
            while (rootBlock > minTileSize && ((atlasWidth % rootBlock) != 0 || (atlasHeight % rootBlock) != 0))
                rootBlock >>= 1;

            bool valid = atlasWidth >= minTileSize && atlasHeight >= minTileSize && rootBlock >= minTileSize
                && (atlasWidth >= rootBlock) && (atlasHeight >= rootBlock);

            if (!valid)
            {
                bool changedEmpty = AtlasWidth != atlasWidth || AtlasHeight != atlasHeight || MinTileSize != minTileSize || MaxTileSize != maxTileSize || m_RootCount != 0;
                AtlasWidth = atlasWidth;
                AtlasHeight = atlasHeight;
                MinTileSize = minTileSize;
                MaxTileSize = maxTileSize;
                RootBlockSize = 0;
                RootColumns = 0;
                RootRows = 0;
                m_RootCount = 0;
                m_NodesPerTree = 0;
                m_TotalNodes = 0;
                m_LevelCount = 0;
                MaxMinTileCapacity = 0;
                TotalTexels = 0;
                AllocatedTexels = 0;
                AllocatedTileCount = 0;
                return changedEmpty;
            }

            int rootCols = atlasWidth / rootBlock;
            int rootRows = atlasHeight / rootBlock;
            if (AtlasWidth == atlasWidth && AtlasHeight == atlasHeight && MinTileSize == minTileSize
                && MaxTileSize == maxTileSize && RootBlockSize == rootBlock && RootColumns == rootCols && RootRows == rootRows)
            {
                return false;
            }

            AtlasWidth = atlasWidth;
            AtlasHeight = atlasHeight;
            MinTileSize = minTileSize;
            MaxTileSize = maxTileSize;
            RootBlockSize = rootBlock;
            RootColumns = rootCols;
            RootRows = rootRows;
            m_RootCount = rootCols * rootRows;

            int levels = 1;
            int size = rootBlock;
            while (size > minTileSize)
            {
                size >>= 1;
                levels++;
            }
            m_LevelCount = levels;

            int nodesPerTree = 0;
            int levelNodes = 1;
            for (int l = 0; l < levels; ++l)
            {
                nodesPerTree += levelNodes;
                levelNodes *= 4;
            }
            m_NodesPerTree = nodesPerTree;
            m_TotalNodes = m_RootCount * m_NodesPerTree;
            MaxMinTileCapacity = (atlasWidth / minTileSize) * (atlasHeight / minTileSize);
            TotalTexels = (long)rootCols * rootRows * rootBlock * rootBlock;

            EnsureArrayCapacity(m_TotalNodes);

            for (int r = 0; r < m_RootCount; ++r)
            {
                int col = r % rootCols;
                int row = r / rootCols;
                int treeBase = r * m_NodesPerTree;
                BuildTreeMetadata(treeBase, 0, 0, col * rootBlock, row * rootBlock, rootBlock, -1);
            }

            Reset();
            return true;
        }

        public void Reset()
        {
            for (int i = 0; i < m_TotalNodes; ++i)
            {
                m_State[i] = StateFree;
                m_MaxFreeSize[i] = m_NodeSize[i];
            }
            AllocatedTexels = 0;
            AllocatedTileCount = 0;
        }

        public bool CanAllocate(int tileSize)
        {
            if (m_RootCount <= 0)
                return false;
            int snapped = SnapRequestedTileSize(tileSize);
            if (snapped <= 0)
                return false;
            for (int r = 0; r < m_RootCount; ++r)
            {
                if (m_MaxFreeSize[r * m_NodesPerTree] >= snapped)
                    return true;
            }
            return false;
        }

        public bool TryAllocate(int tileSize, out int handle, out Vector4 pixelRect)
        {
            handle = -1;
            pixelRect = Vector4.zero;
            if (m_RootCount <= 0)
                return false;

            int snapped = SnapRequestedTileSize(tileSize);
            if (snapped <= 0)
                return false;

            // Best-fit root: pick the root tree whose largest free block is >= snapped and minimal,
            // keeping whole large roots intact whenever a subdivided root already has space.
            int chosenRoot = -1;
            int bestFree = int.MaxValue;
            for (int r = 0; r < m_RootCount; ++r)
            {
                int rootNode = r * m_NodesPerTree;
                int freeSize = m_MaxFreeSize[rootNode];
                if (freeSize >= snapped && freeSize < bestFree)
                {
                    bestFree = freeSize;
                    chosenRoot = rootNode;
                    if (freeSize == snapped)
                        break;
                }
            }
            if (chosenRoot < 0)
                return false;

            int current = chosenRoot;
            while (m_NodeSize[current] > snapped)
            {
                if (m_State[current] == StateFree)
                    m_State[current] = StateSplit;

                int firstChild = m_FirstChildNode[current];
                if (firstChild < 0)
                    return false;

                int chosenChild = -1;
                int bestChildFree = int.MaxValue;
                for (int c = 0; c < 4; ++c)
                {
                    int child = firstChild + c;
                    int childFree = m_MaxFreeSize[child];
                    if (childFree >= snapped && childFree < bestChildFree)
                    {
                        bestChildFree = childFree;
                        chosenChild = child;
                        if (childFree == snapped)
                            break;
                    }
                }
                if (chosenChild < 0)
                    return false;

                current = chosenChild;
            }

            if (m_State[current] != StateFree || m_NodeSize[current] != snapped)
                return false;

            m_State[current] = StateUsed;
            m_MaxFreeSize[current] = 0;
            PropagateUpward(m_ParentNode[current]);

            AllocatedTexels += (long)snapped * snapped;
            AllocatedTileCount++;
            handle = current;
            pixelRect = m_NodeRect[current];
            return true;
        }

        public void Free(int handle)
        {
            if (handle < 0 || handle >= m_TotalNodes || m_State[handle] != StateUsed)
                return;

            int freedSize = m_NodeSize[handle];
            AllocatedTexels -= (long)freedSize * freedSize;
            AllocatedTileCount = Mathf.Max(0, AllocatedTileCount - 1);

            m_State[handle] = StateFree;
            m_MaxFreeSize[handle] = freedSize;
            PropagateUpward(m_ParentNode[handle]);
        }

        public Vector4 SlotPixelRect(int handle)
        {
            if (handle < 0 || handle >= m_TotalNodes)
                return Vector4.zero;
            return m_NodeRect[handle];
        }

        public int SlotTileSize(int handle)
        {
            if (handle < 0 || handle >= m_TotalNodes)
                return 0;
            return m_NodeSize[handle];
        }

        int SnapRequestedTileSize(int tileSize)
        {
            if (tileSize <= 0 || tileSize > RootBlockSize)
                return 0;
            int pow2 = PreviousPowerOfTwo(Mathf.Max(MinTileSize, tileSize));
            if (pow2 < tileSize && (pow2 << 1) <= RootBlockSize)
                pow2 <<= 1;
            return pow2 <= RootBlockSize ? pow2 : 0;
        }

        void PropagateUpward(int node)
        {
            while (node >= 0)
            {
                int firstChild = m_FirstChildNode[node];
                if (firstChild < 0)
                    break;

                bool allFree = true;
                int childExpectedSize = m_NodeSize[firstChild];
                int maxChildFree = 0;
                for (int c = 0; c < 4; ++c)
                {
                    int child = firstChild + c;
                    if (m_State[child] != StateFree || m_MaxFreeSize[child] != childExpectedSize)
                        allFree = false;
                    if (m_MaxFreeSize[child] > maxChildFree)
                        maxChildFree = m_MaxFreeSize[child];
                }

                if (allFree)
                {
                    m_State[node] = StateFree;
                    m_MaxFreeSize[node] = m_NodeSize[node];
                }
                else
                {
                    m_State[node] = StateSplit;
                    m_MaxFreeSize[node] = maxChildFree;
                }
                node = m_ParentNode[node];
            }
        }

        void EnsureArrayCapacity(int requiredNodes)
        {
            if (m_State.Length >= requiredNodes)
                return;
            m_State = new byte[requiredNodes];
            m_NodeSize = new int[requiredNodes];
            m_MaxFreeSize = new int[requiredNodes];
            m_ParentNode = new int[requiredNodes];
            m_FirstChildNode = new int[requiredNodes];
            m_NodeLevel = new int[requiredNodes];
            m_NodeRect = new Vector4[requiredNodes];
        }

        void BuildTreeMetadata(int treeBase, int level, int levelOffset, float x, float y, int size, int parentGlobalIndex)
        {
            int levelStart = LevelStartOffset(level);
            int globalIndex = treeBase + levelStart + levelOffset;
            m_NodeLevel[globalIndex] = level;
            m_NodeSize[globalIndex] = size;
            m_ParentNode[globalIndex] = parentGlobalIndex;
            m_NodeRect[globalIndex] = new Vector4(x, y, size, size);

            if (level + 1 >= m_LevelCount)
            {
                m_FirstChildNode[globalIndex] = -1;
                return;
            }

            int childLevelStart = LevelStartOffset(level + 1);
            int firstChildOffset = levelOffset * 4;
            m_FirstChildNode[globalIndex] = treeBase + childLevelStart + firstChildOffset;

            int half = size >> 1;
            BuildTreeMetadata(treeBase, level + 1, firstChildOffset + 0, x, y, half, globalIndex);
            BuildTreeMetadata(treeBase, level + 1, firstChildOffset + 1, x + half, y, half, globalIndex);
            BuildTreeMetadata(treeBase, level + 1, firstChildOffset + 2, x, y + half, half, globalIndex);
            BuildTreeMetadata(treeBase, level + 1, firstChildOffset + 3, x + half, y + half, half, globalIndex);
        }

        static int LevelStartOffset(int level)
        {
            int offset = 0;
            int nodes = 1;
            for (int i = 0; i < level; ++i)
            {
                offset += nodes;
                nodes *= 4;
            }
            return offset;
        }
    }
}
