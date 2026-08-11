using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Stable draw template for a (shaderPass, mesh, section, material) combination.
    /// Invalidation boundary:
    /// - Transform / camera motion must NOT cause a cache miss.
    /// - materialUnityId change or material revision change → miss / invalidate.
    /// - meshUnityId / sectionIndex / shaderPass / platformFeatureKey / sectionRevision / staticFlags change → new key.
    /// </summary>
    public struct MeshPassDraw
    {
        public int shaderPassIndex;
        public int meshUnityId;
        public int sectionIndex;
        public int materialUnityId;
        public MeshPassDrawId id;
        public uint materialRevision;
        public uint sectionRevision;
        public uint platformFeatureKey;
        public uint staticFlags;
    }

    /// <summary>
    /// Full structured cache key. Equals is authoritative; GetHashCode is acceleration only.
    /// </summary>
    public struct MeshPassDrawCacheKey : IEquatable<MeshPassDrawCacheKey>
    {
        public int shaderPassIndex;
        public int meshUnityId;
        public int sectionIndex;
        public int materialUnityId;
        public uint materialRevision;
        public uint sectionRevision;
        public uint platformFeatureKey;
        public uint staticFlags;

        public MeshPassDrawCacheKey(
            int shaderPassIndex,
            int meshUnityId,
            int sectionIndex,
            int materialUnityId,
            uint materialRevision,
            uint sectionRevision,
            uint platformFeatureKey,
            uint staticFlags = 0)
        {
            this.shaderPassIndex = shaderPassIndex;
            this.meshUnityId = meshUnityId;
            this.sectionIndex = sectionIndex;
            this.materialUnityId = materialUnityId;
            this.materialRevision = materialRevision;
            this.sectionRevision = sectionRevision;
            this.platformFeatureKey = platformFeatureKey;
            this.staticFlags = staticFlags;
        }

        public bool Equals(MeshPassDrawCacheKey other)
        {
            return shaderPassIndex == other.shaderPassIndex
                && meshUnityId == other.meshUnityId
                && sectionIndex == other.sectionIndex
                && materialUnityId == other.materialUnityId
                && materialRevision == other.materialRevision
                && sectionRevision == other.sectionRevision
                && platformFeatureKey == other.platformFeatureKey
                && staticFlags == other.staticFlags;
        }

        public override bool Equals(object obj) => obj is MeshPassDrawCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = shaderPassIndex;
                hash = (hash * 397) ^ meshUnityId;
                hash = (hash * 397) ^ sectionIndex;
                hash = (hash * 397) ^ materialUnityId;
                hash = (hash * 397) ^ (int)materialRevision;
                hash = (hash * 397) ^ (int)sectionRevision;
                hash = (hash * 397) ^ (int)platformFeatureKey;
                hash = (hash * 397) ^ (int)staticFlags;
                return hash;
            }
        }
    }

    /// <summary>
    /// CPU template cache. Schedule warms entries before Burst filter jobs.
    /// Set <see cref="Enabled"/> false to force rebuild every lookup (image-equivalent via grouping fallback).
    /// </summary>
    public sealed class MeshPassDrawCache : IDisposable
    {
        public static bool Enabled = true;

        /// <summary>
        /// Raised when a material's revision changes. Pipelines invalidate matching templates.
        /// </summary>
        public static event Action<int, uint> MaterialRevisionInvalidated;

        public static void NotifyMaterialRevision(int materialUnityId, uint oldRevision)
        {
            MaterialRevisionInvalidated?.Invoke(materialUnityId, oldRevision);
        }

        private readonly Dictionary<MeshPassDrawCacheKey, int> m_Lookup = new Dictionary<MeshPassDrawCacheKey, int>(256);
        private readonly List<int> m_FreeSlots = new List<int>(32);
        private NativeList<MeshPassDraw> m_Entries;
        private uint m_NextGeneration = 1;
        private bool m_IsCreated;

        public int Count => m_IsCreated ? m_Entries.Length - m_FreeSlots.Count : 0;

        public MeshPassDrawCache(int capacity = 256)
        {
            m_Entries = new NativeList<MeshPassDraw>(math.max(16, capacity), Allocator.Persistent);
            m_IsCreated = true;
            MaterialRevisionInvalidated += OnMaterialRevisionInvalidated;
        }

        public MeshPassDrawId GetOrCreate(
            int shaderPassIndex,
            int meshUnityId,
            int sectionIndex,
            int materialUnityId,
            uint materialRevision,
            uint sectionRevision = 0,
            uint platformFeatureKey = 0,
            uint staticFlags = 0)
        {
            if (!m_IsCreated)
            {
                return MeshPassDrawId.Invalid;
            }

            // Disabled: BuildJob falls back to MeshGroupingKey when passDrawId is Invalid.
            if (!Enabled)
            {
                return MeshPassDrawId.Invalid;
            }

            var key = new MeshPassDrawCacheKey(
                shaderPassIndex,
                meshUnityId,
                sectionIndex,
                materialUnityId,
                materialRevision,
                sectionRevision,
                platformFeatureKey,
                staticFlags);

            if (m_Lookup.TryGetValue(key, out int existingIndex))
            {
                MeshPassDraw existing = m_Entries[existingIndex];
                if (existing.id.IsValid)
                {
                    MeshPipelineDiagnostics.TemplateCacheHits++;
                    return existing.id;
                }
            }

            MeshPipelineDiagnostics.TemplateCacheMisses++;

            int slot;
            if (m_FreeSlots.Count > 0)
            {
                slot = m_FreeSlots[m_FreeSlots.Count - 1];
                m_FreeSlots.RemoveAt(m_FreeSlots.Count - 1);
            }
            else
            {
                slot = m_Entries.Length;
                m_Entries.Add(default);
            }

            var id = new MeshPassDrawId((uint)slot, m_NextGeneration++);
            var entry = new MeshPassDraw
            {
                shaderPassIndex = shaderPassIndex,
                meshUnityId = meshUnityId,
                sectionIndex = sectionIndex,
                materialUnityId = materialUnityId,
                id = id,
                materialRevision = materialRevision,
                sectionRevision = sectionRevision,
                platformFeatureKey = platformFeatureKey,
                staticFlags = staticFlags
            };
            m_Entries[slot] = entry;
            m_Lookup[key] = slot;
            return id;
        }

        public bool TryGet(MeshPassDrawId id, out MeshPassDraw draw)
        {
            draw = default;
            if (!m_IsCreated || !id.IsValid || id.Index >= (uint)m_Entries.Length)
            {
                return false;
            }

            MeshPassDraw entry = m_Entries[(int)id.Index];
            if (entry.id.Generation != id.Generation)
            {
                return false;
            }

            draw = entry;
            return true;
        }

        public void InvalidateByMaterialRevision(int materialUnityId, uint materialRevision)
        {
            if (!m_IsCreated)
            {
                return;
            }

            for (int i = 0; i < m_Entries.Length; ++i)
            {
                MeshPassDraw entry = m_Entries[i];
                if (!entry.id.IsValid)
                {
                    continue;
                }

                if (entry.materialUnityId == materialUnityId && entry.materialRevision == materialRevision)
                {
                    var key = new MeshPassDrawCacheKey(
                        entry.shaderPassIndex,
                        entry.meshUnityId,
                        entry.sectionIndex,
                        entry.materialUnityId,
                        entry.materialRevision,
                        entry.sectionRevision,
                        entry.platformFeatureKey,
                        entry.staticFlags);
                    m_Lookup.Remove(key);
                    entry.id = MeshPassDrawId.Invalid;
                    m_Entries[i] = entry;
                    m_FreeSlots.Add(i);
                }
            }
        }

        public void InvalidateAll()
        {
            m_Lookup.Clear();
            m_FreeSlots.Clear();
            if (m_IsCreated)
            {
                m_Entries.Clear();
            }
        }

        public void Dispose()
        {
            if (!m_IsCreated)
            {
                return;
            }

            MaterialRevisionInvalidated -= OnMaterialRevisionInvalidated;
            m_Lookup.Clear();
            m_FreeSlots.Clear();
            if (m_Entries.IsCreated)
            {
                m_Entries.Dispose();
            }

            m_IsCreated = false;
        }

        private void OnMaterialRevisionInvalidated(int materialUnityId, uint materialRevision)
        {
            InvalidateByMaterialRevision(materialUnityId, materialRevision);
        }
    }
}
