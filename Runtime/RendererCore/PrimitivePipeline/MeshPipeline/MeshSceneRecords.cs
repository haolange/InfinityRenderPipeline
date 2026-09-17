using System;
using System.Runtime.InteropServices;
using InfinityTech.Core.Geometry;
using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshInstanceRecord
    {
        public TransformId transform;
        public FMeshBakedLighting bakedLighting;
        public FBound worldBounds;
        public int layerMask;
        public uint renderingLayerMask;
        public EMeshInstanceFlags flags;
        public EMotionType motionType;
        public ECastShadowMethod castShadow;
        public int drawStart;
        public int drawCount;
        public EGeometrySourceKind geometrySource;
        public uint deformationDataId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TransformRecord
    {
        public float4x4 current;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MeshDraw
    {
        public MeshInstanceId instance;
        public MeshSectionId section;
        public MaterialDataId material;
        public EPassEligibility eligibility;
        public int renderQueue;
        public int priority;
        public ulong meshUnityId;
        public ulong materialUnityId;
        public int sectionIndex;
        /// <summary>
        /// Draw-level static / batching flags (e.g. 1 = Static mobility). Feeds MeshDrawCommandKey.
        /// </summary>
        public uint staticFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MeshSectionRecord
    {
        public ulong meshUnityId;
        public int sectionIndex;
        public EGeometrySourceKind geometrySource;
        public int refCount;
        public uint revision;
        /// <summary>
        /// Geometry fingerprint (e.g. hash of subMeshCount/vertexCount). Stored separately;
        /// changes bump <see cref="revision"/> so template cache keys observe geometry edits.
        /// </summary>
        public uint geometryRevision;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialDataRecord
    {
        public ulong materialUnityId;
        public int renderQueue;
        public uint revision;
        public int refCount;
    }

    public struct MeshSceneRevisionSnapshot
    {
        public int StructuralRevision;
        public int ContentRevision;
        public int VisibilityRevision;
    }

    /// <summary>
    /// Transaction snapshot for revisions + dirty page words only.
    /// highWater / free-list membership are owned by Free*/Restore*/deferred reclaim and may grow monotonically.
    /// </summary>
    public struct MeshSceneStateSnapshot
    {
        public int StructuralRevision;
        public int ContentRevision;
        public int VisibilityRevision;

        public ulong[] TransformDirtyPages;
        public ulong[] BoundsDirtyPages;
    }

    /// <summary>
    /// Slot dirty bitmap. One bit is a page of <see cref="PageSize"/> slots.
    /// </summary>
    internal struct DirtyPageBitmap : IDisposable
    {
        public const int PageSize = 64;

        private NativeList<ulong> m_Words;

        public bool IsCreated => m_Words.IsCreated;
        public bool Any
        {
            get
            {
                if (!m_Words.IsCreated)
                {
                    return false;
                }

                for (int i = 0; i < m_Words.Length; ++i)
                {
                    if (m_Words[i] != 0ul)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int FirstDirtySlot
        {
            get
            {
                if (!TryGetFirstLastPage(out int firstPage, out _))
                {
                    return int.MaxValue;
                }

                return firstPage * PageSize;
            }
        }

        public int LastDirtySlot
        {
            get
            {
                if (!TryGetFirstLastPage(out _, out int lastPage))
                {
                    return -1;
                }

                return lastPage * PageSize + (PageSize - 1);
            }
        }

        public void Init()
        {
            m_Words = new NativeList<ulong>(1, Allocator.Persistent);
            m_Words.Add(0ul);
        }

        public void Dispose()
        {
            if (m_Words.IsCreated)
            {
                m_Words.Dispose();
            }
        }

        public void Clear()
        {
            if (!m_Words.IsCreated)
            {
                return;
            }

            for (int i = 0; i < m_Words.Length; ++i)
            {
                m_Words[i] = 0ul;
            }
        }

        public void Mark(int slotIndex)
        {
            if (slotIndex < 0)
            {
                return;
            }

            int page = slotIndex / PageSize;
            int word = page / 64;
            int bit = page & 63;
            EnsureWords(word + 1);
            m_Words[word] |= 1ul << bit;
        }

        public ulong[] CopyWords()
        {
            if (!m_Words.IsCreated || m_Words.Length == 0)
            {
                return Array.Empty<ulong>();
            }

            var copy = new ulong[m_Words.Length];
            for (int i = 0; i < m_Words.Length; ++i)
            {
                copy[i] = m_Words[i];
            }

            return copy;
        }

        public void Restore(ulong[] words)
        {
            if (!m_Words.IsCreated)
            {
                Init();
            }

            if (words == null || words.Length == 0)
            {
                Clear();
                return;
            }

            EnsureWords(words.Length);
            for (int i = 0; i < m_Words.Length; ++i)
            {
                m_Words[i] = i < words.Length ? words[i] : 0ul;
            }
        }

        public void CollectMergedRuns(NativeList<int2> runs, int slotLimit)
        {
            runs.Clear();
            if (!m_Words.IsCreated || slotLimit <= 0)
            {
                return;
            }

            int runBegin = -1;
            int maxPage = (slotLimit + PageSize - 1) / PageSize;
            int page = 0;
            for (int word = 0; word < m_Words.Length && page < maxPage; ++word)
            {
                ulong bits = m_Words[word];
                for (int bit = 0; bit < 64 && page < maxPage; ++bit, ++page)
                {
                    bool dirty = (bits & (1ul << bit)) != 0ul;
                    if (dirty && runBegin < 0)
                    {
                        runBegin = page * PageSize;
                    }
                    else if (!dirty && runBegin >= 0)
                    {
                        int exclusive = math.min(page * PageSize, slotLimit);
                        if (exclusive > runBegin)
                        {
                            runs.Add(new int2(runBegin, exclusive));
                        }

                        runBegin = -1;
                    }
                }
            }

            if (runBegin >= 0)
            {
                int exclusive = math.min(maxPage * PageSize, slotLimit);
                if (exclusive > runBegin)
                {
                    runs.Add(new int2(runBegin, exclusive));
                }
            }
        }

        void EnsureWords(int count)
        {
            if (!m_Words.IsCreated)
            {
                Init();
            }

            while (m_Words.Length < count)
            {
                m_Words.Add(0ul);
            }
        }

        bool TryGetFirstLastPage(out int firstPage, out int lastPage)
        {
            firstPage = 0;
            lastPage = -1;
            if (!m_Words.IsCreated)
            {
                return false;
            }

            bool found = false;
            int page = 0;
            for (int word = 0; word < m_Words.Length; ++word)
            {
                ulong bits = m_Words[word];
                for (int bit = 0; bit < 64; ++bit, ++page)
                {
                    if ((bits & (1ul << bit)) == 0ul)
                    {
                        continue;
                    }

                    if (!found)
                    {
                        firstPage = page;
                        found = true;
                    }

                    lastPage = page;
                }
            }

            return found;
        }
    }
}
