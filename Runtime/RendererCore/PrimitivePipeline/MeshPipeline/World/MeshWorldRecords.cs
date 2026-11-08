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

    /// <summary>
    /// The material state pass membership and command bindings read: queue, surface route, translucent stage and the
    /// shader identity. A shader swap is a state change, so commands resolve their pass index again.
    /// </summary>
    public struct MeshMaterialState : IEquatable<MeshMaterialState>
    {
        public int renderQueue;
        public byte route;
        public byte stage;
        public ulong shader;

        public MeshMaterialState(int renderQueue, int route, int stage, ulong shader)
        {
            if (route < 0 || route > 1) throw new ArgumentOutOfRangeException(nameof(route));
            if (stage < 0 || stage > 3) throw new ArgumentOutOfRangeException(nameof(stage));
            this.renderQueue = renderQueue;
            this.route = (byte)route;
            this.stage = (byte)stage;
            this.shader = shader;
        }

        public bool Equals(MeshMaterialState other) => renderQueue == other.renderQueue && route == other.route && stage == other.stage && shader == other.shader;
        public override bool Equals(object obj) => obj is MeshMaterialState other && Equals(other);
        public override int GetHashCode() => (renderQueue * 397) ^ (route << 4) ^ stage ^ shader.GetHashCode();
    }

    /// <summary>
    /// An entity's LOD chain in LODGroup semantics: LOD i draws while the screen size is at least <c>screenSize[i]</c>
    /// (a lower bound, strictly decreasing); below the last level nothing is drawn. The cross-fade band sits above each
    /// threshold and is <c>fadeWidth</c> of the gap to the previous threshold (1 for LOD 0).
    /// </summary>
    // GPU-RECORD: MeshLODChain Shaders/ShaderLibrary/MeshLOD.hlsl
    public struct MeshLODChain : IEquatable<MeshLODChain>
    {
        public const int MaxLevels = 8;
        public float4 screenSize0;
        public float4 screenSize1;
        public float fadeWidth;
        public int count;

        /// <summary>One level drawn at every screen size.</summary>
        public static MeshLODChain Single => new MeshLODChain { count = 1, fadeWidth = 0.2f };

        public float ScreenSize(int level) => level < 4 ? screenSize0[level] : screenSize1[level - 4];

        /// <summary>Bounds strictly decrease, so some level has a cross-fade band exactly when LOD 0's bound is positive.</summary>
        public bool hasFadeBand => count > 0 && screenSize0.x > 0;

        public static MeshLODChain Create(ReadOnlySpan<float> screenSizes, float fadeWidth)
        {
            if (screenSizes.Length < 1 || screenSizes.Length > MaxLevels)
                throw new ArgumentOutOfRangeException(nameof(screenSizes), screenSizes.Length, $"A LOD chain has 1 to {MaxLevels} levels.");
            var chain = new MeshLODChain { count = screenSizes.Length, fadeWidth = fadeWidth };
            for (int i = 0; i < screenSizes.Length; ++i)
                if (i < 4) chain.screenSize0[i] = screenSizes[i]; else chain.screenSize1[i - 4] = screenSizes[i];
            chain.ThrowIfInvalid();
            return chain;
        }

        /// <summary>Throws unless the chain has 1 to 8 levels, a fade width in [0.01, 0.99] and bounds in [0, 1] that strictly decrease.</summary>
        public void ThrowIfInvalid()
        {
            if (count < 1 || count > MaxLevels) throw new ArgumentOutOfRangeException(nameof(count), count, $"A LOD chain has 1 to {MaxLevels} levels.");
            if (!(fadeWidth >= 0.01f && fadeWidth <= 0.99f)) throw new ArgumentOutOfRangeException(nameof(fadeWidth), fadeWidth, "fadeWidth is in [0.01, 0.99].");
            float previous = 1;
            for (int i = 0; i < count; ++i)
            {
                float size = ScreenSize(i);
                bool valid = size >= 0 && (i == 0 ? size <= 1 : size < previous);
                if (!valid) throw new ArgumentException($"LOD {i} screen size {size} must be in [0, 1] and below the previous level.", "screenSizes");
                previous = size;
            }
        }

        public bool Equals(MeshLODChain other) => screenSize0.Equals(other.screenSize0) && screenSize1.Equals(other.screenSize1)
            && fadeWidth == other.fadeWidth && count == other.count;
        public override bool Equals(object obj) => obj is MeshLODChain other && Equals(other);
        public override int GetHashCode() => screenSize0.GetHashCode() ^ (count * 397);
    }

    public struct MeshEntity
    {
        public MeshEntityDesc desc;
        public MeshLODChain lod;
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
        /// <summary>The LOD level of the entity's chain this element draws in.</summary>
        public byte lodIndex;
        public int priority;
        /// <summary>Next element of the same entity, -1 at the end.</summary>
        public int nextElement;
    }

    public struct MeshSection
    {
        public ulong meshId;
        public int submesh;
        public int refCount;
    }

    public struct MeshMaterial
    {
        public ulong materialId;
        public MeshMaterialState state;
        public int refCount;
    }

    internal struct SectionKey : IEquatable<SectionKey>
    {
        public ulong meshId;
        public int submesh;

        public SectionKey(ulong meshId, int submesh) { this.meshId = meshId; this.submesh = submesh; }
        public bool Equals(SectionKey other) => meshId == other.meshId && submesh == other.submesh;
        public override bool Equals(object obj) => obj is SectionKey other && Equals(other);
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
