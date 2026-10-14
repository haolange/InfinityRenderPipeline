using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityTech.Rendering.LightPipeline
{
    /// <summary>
    /// Buddy allocator of power-of-two square blocks inside a square power-of-two atlas. A block of size S sits at a
    /// multiple of S, so its whole mip chain stays inside the block (mip m of the block is the atlas region at
    /// position / 2^m) and trilinear filtering never reads a neighbour. Freeing merges buddies back.
    /// </summary>
    internal sealed class AtlasQuadAllocator
    {
        readonly int m_AtlasSize;
        readonly int m_MinSize;
        // Free blocks per level; level 0 is the whole atlas, level l has size atlasSize >> l.
        readonly List<HashSet<Vector2Int>> m_Free = new List<HashSet<Vector2Int>>();

        public int AtlasSize => m_AtlasSize;
        public int MinSize => m_MinSize;

        public AtlasQuadAllocator(int atlasSize, int minSize)
        {
            if (atlasSize <= 0 || !Mathf.IsPowerOfTwo(atlasSize)) throw new ArgumentOutOfRangeException(nameof(atlasSize), "Atlas size must be a power of two.");
            if (minSize <= 0 || !Mathf.IsPowerOfTwo(minSize) || minSize > atlasSize) throw new ArgumentOutOfRangeException(nameof(minSize));
            m_AtlasSize = atlasSize;
            m_MinSize = minSize;
            for (int size = atlasSize; size >= minSize; size >>= 1)
            {
                m_Free.Add(new HashSet<Vector2Int>());
            }
            m_Free[0].Add(Vector2Int.zero);
        }

        public static int BlockSize(int requested, int minSize, int maxSize)
        {
            int size = Mathf.NextPowerOfTwo(Mathf.Max(1, requested));
            return Mathf.Clamp(size, minSize, maxSize);
        }

        int Level(int size) => (int)Math.Round(Math.Log(m_AtlasSize / (double)size, 2));

        // Pixel position of a free block of the given power-of-two size, or false when none fits.
        public bool TryAllocate(int size, out Vector2Int position)
        {
            position = default;
            if (size < m_MinSize || size > m_AtlasSize || !Mathf.IsPowerOfTwo(size))
            {
                return false;
            }
            int level = Level(size);
            int source = level;
            while (source >= 0 && m_Free[source].Count == 0)
            {
                source--;
            }
            if (source < 0)
            {
                return false;
            }

            Vector2Int block = First(m_Free[source]);
            m_Free[source].Remove(block);
            // Split down to the requested level, keeping the first child and freeing the other three.
            for (int l = source; l < level; ++l)
            {
                int half = (m_AtlasSize >> l) >> 1;
                m_Free[l + 1].Add(block + new Vector2Int(half, 0));
                m_Free[l + 1].Add(block + new Vector2Int(0, half));
                m_Free[l + 1].Add(block + new Vector2Int(half, half));
            }
            position = block;
            return true;
        }

        public void Free(Vector2Int position, int size)
        {
            int level = Level(size);
            while (level > 0)
            {
                int parentSize = m_AtlasSize >> (level - 1);
                Vector2Int parent = new Vector2Int(position.x / parentSize * parentSize, position.y / parentSize * parentSize);
                int half = parentSize >> 1;
                bool allFree = true;
                for (int i = 0; i < 4 && allFree; ++i)
                {
                    Vector2Int sibling = parent + new Vector2Int((i & 1) * half, (i >> 1) * half);
                    if (sibling != position && !m_Free[level].Contains(sibling)) allFree = false;
                }
                if (!allFree)
                {
                    break;
                }
                for (int i = 0; i < 4; ++i)
                {
                    m_Free[level].Remove(parent + new Vector2Int((i & 1) * half, (i >> 1) * half));
                }
                position = parent;
                level--;
            }
            m_Free[level].Add(position);
        }

        // Deterministic choice: lowest y, then lowest x.
        static Vector2Int First(HashSet<Vector2Int> set)
        {
            Vector2Int best = default;
            bool found = false;
            foreach (Vector2Int v in set)
            {
                if (!found || v.y < best.y || (v.y == best.y && v.x < best.x))
                {
                    best = v;
                    found = true;
                }
            }
            return best;
        }

        public int FreeArea()
        {
            int area = 0;
            for (int l = 0; l < m_Free.Count; ++l)
            {
                int size = m_AtlasSize >> l;
                area += m_Free[l].Count * size * size;
            }
            return area;
        }
    }
}
