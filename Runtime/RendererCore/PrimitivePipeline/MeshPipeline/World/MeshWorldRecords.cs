using System;
using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>Entity attributes. Bit values are shared with the GPU culling kernels (Visible is 4).</summary>
    [Flags]
    public enum EMeshEntityFlags : uint
    {
        None = 0,
        CastShadow = 1 << 0,
        ReceiveShadow = 1 << 1,
        Visible = 1 << 2,
        AffectIndirect = 1 << 3,
        ObjectMotion = 1 << 4
    }

    /// <summary>Per-entity attributes written by <see cref="MeshWorldUpdate"/>.</summary>
    public struct MeshEntityDesc : IEquatable<MeshEntityDesc>
    {
        public int layerMask;
        public ulong sceneCullingMask;
        public uint renderingLayerMask;
        public EMeshEntityFlags flags;

        public bool Equals(MeshEntityDesc other) => layerMask == other.layerMask && sceneCullingMask == other.sceneCullingMask
            && renderingLayerMask == other.renderingLayerMask && flags == other.flags;
        public override bool Equals(object obj) => obj is MeshEntityDesc other && Equals(other);
        public override int GetHashCode() => unchecked((layerMask * 397) ^ sceneCullingMask.GetHashCode() ^ ((int)renderingLayerMask << 8) ^ (int)flags);
    }

    /// <summary>The routing state of a material that pass membership reads: queue, surface route and translucent stage.</summary>
    public struct MeshMaterialState : IEquatable<MeshMaterialState>
    {
        public int renderQueue;
        public byte route;
        public byte stage;

        public MeshMaterialState(int renderQueue, int route, int stage)
        {
            if (route < 0 || route > 1) throw new ArgumentOutOfRangeException(nameof(route));
            if (stage < 0 || stage > 3) throw new ArgumentOutOfRangeException(nameof(stage));
            this.renderQueue = renderQueue;
            this.route = (byte)route;
            this.stage = (byte)stage;
        }

        public bool Equals(MeshMaterialState other) => renderQueue == other.renderQueue && route == other.route && stage == other.stage;
        public override bool Equals(object obj) => obj is MeshMaterialState other && Equals(other);
        public override int GetHashCode() => (renderQueue * 397) ^ (route << 4) ^ stage;
    }

    public struct MeshEntity
    {
        public MeshEntityDesc desc;
        /// <summary>Head of this entity's element chain (element index), -1 when empty.</summary>
        public int firstElement;
        public int elementCount;
    }

    public struct MeshElement
    {
        public MeshEntityId entity;
        public MeshSectionId section;
        /// <summary>Invalid when the element has no material; such an element belongs to no pass.</summary>
        public MeshMaterialId material;
        public int priority;
        /// <summary>Next element of the same entity, -1 at the end.</summary>
        public int nextElement;
    }

    public struct MeshSection
    {
        public ulong meshId;
        public int submesh;
        public int refCount;
        public uint revision;
    }

    public struct MeshMaterial
    {
        public ulong materialId;
        public MeshMaterialState state;
        public int refCount;
        public uint revision;
    }

    internal struct FSectionKey : IEquatable<FSectionKey>
    {
        public ulong meshId;
        public int submesh;

        public FSectionKey(ulong meshId, int submesh) { this.meshId = meshId; this.submesh = submesh; }
        public bool Equals(FSectionKey other) => meshId == other.meshId && submesh == other.submesh;
        public override bool Equals(object obj) => obj is FSectionKey other && Equals(other);
        public override int GetHashCode() => unchecked(meshId.GetHashCode() * 397 ^ submesh);
    }

    /// <summary>
    /// Slot dirty bitmap. One bit marks a page of <see cref="PageSize"/> slots. Words are sized before a job marks pages,
    /// so jobs mark through <see cref="Mark(NativeArray{ulong}, int)"/> without growth.
    /// </summary>
    internal struct DirtyPageBitmap : IDisposable
    {
        public const int PageSize = 64;

        NativeList<ulong> m_Words;

        public void Init() => m_Words = new NativeList<ulong>(4, Allocator.Persistent);

        public bool IsCreated => m_Words.IsCreated;

        public bool Any
        {
            get
            {
                for (int i = 0; i < m_Words.Length; ++i)
                    if (m_Words[i] != 0ul) return true;
                return false;
            }
        }

        /// <summary>Grows the words to cover <paramref name="slotCount"/> slots; valid as a job target until the next growth.</summary>
        public NativeArray<ulong> Words(int slotCount)
        {
            int pages = (slotCount + PageSize - 1) / PageSize;
            int words = (pages + 63) / 64;
            while (m_Words.Length < words) m_Words.Add(0ul);
            return m_Words.AsArray();
        }

        public void Mark(int slot)
        {
            Mark(Words(slot + 1), slot);
        }

        public static void Mark(NativeArray<ulong> words, int slot)
        {
            int page = slot / PageSize;
            words[page >> 6] |= 1ul << (page & 63);
        }

        public void Clear()
        {
            for (int i = 0; i < m_Words.Length; ++i) m_Words[i] = 0ul;
        }

        public void CopyTo(NativeList<ulong> destination)
        {
            destination.Clear();
            for (int i = 0; i < m_Words.Length; ++i) destination.Add(m_Words[i]);
        }

        public void Restore(NativeList<ulong> source)
        {
            for (int i = 0; i < m_Words.Length; ++i) m_Words[i] = i < source.Length ? source[i] : 0ul;
        }

        /// <summary>Merged [begin, end) slot runs of dirty pages, clamped to <paramref name="slotLimit"/>.</summary>
        public void CollectRuns(NativeList<int2> runs, int slotLimit)
        {
            runs.Clear();
            int runBegin = -1;
            int maxPage = (slotLimit + PageSize - 1) / PageSize;
            for (int page = 0; page < maxPage; ++page)
            {
                int word = page >> 6;
                bool dirty = word < m_Words.Length && (m_Words[word] & (1ul << (page & 63))) != 0ul;
                if (dirty && runBegin < 0) runBegin = page * PageSize;
                else if (!dirty && runBegin >= 0)
                {
                    runs.Add(new int2(runBegin, math.min(page * PageSize, slotLimit)));
                    runBegin = -1;
                }
            }
            if (runBegin >= 0) runs.Add(new int2(runBegin, math.min(maxPage * PageSize, slotLimit)));
        }

        public void Dispose()
        {
            if (m_Words.IsCreated) m_Words.Dispose();
        }
    }
}
