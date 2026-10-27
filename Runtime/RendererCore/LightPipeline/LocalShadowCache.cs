using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.LightPipeline
{
    // One cached shadow tile: a light, cube face and exact camera scene eligibility.
    internal readonly struct FShadowCacheKey : IEquatable<FShadowCacheKey>
    {
        public readonly ulong lightId;
        public readonly int face;
        public readonly ulong sceneCullingMask;

        public FShadowCacheKey(ulong lightId, int face, ulong sceneCullingMask = ulong.MaxValue)
        {
            this.lightId = lightId;
            this.face = face;
            this.sceneCullingMask = sceneCullingMask;
        }

        public bool Equals(FShadowCacheKey other) => lightId == other.lightId && face == other.face
            && sceneCullingMask == other.sceneCullingMask;
        public override bool Equals(object obj) => obj is FShadowCacheKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(lightId, face, sceneCullingMask);
    }

    /// <summary>
    /// Cached shadow atlas for OnEnable / OnDemand local lights. Tiles have the frame atlas tile size and are keyed by
    /// light instance id, face and scene culling mask, never by record index. The atlas is one persistent texture; what is transactional
    /// is the slot state: a tile rendered this frame is pending until its pass is queued (MarkProduced) and the frame
    /// is submitted (CommitFrame). A frame that fails rolls the produced tiles back to invalid, so they re-render.
    /// Slots are reused least-recently-used when the atlas is full; a light that gets no slot renders every frame.
    /// </summary>
    internal sealed class LocalShadowCache : IDisposable
    {
        public static readonly int AtlasID = Shader.PropertyToID("_CachedLocalShadowMapTexture");

        internal struct SamplingState
        {
            public FLocalShadowSlice slice;
            public FLocalShadowSliceGPU gpu;
            public int faceCount;
            public long production;
        }

        struct Entry
        {
            public int slot;
            public bool valid;
            public int contentHash;
            public int version;
            public int lastUsedFrame;
            // Rendered by a queued pass this frame; committed at CommitFrame.
            public bool produced;
            public bool pending;
            public int pendingHash;
            public int pendingVersion;
            public SamplingState sampling;
            public SamplingState pendingSampling;
            public bool hasSampling;
            public bool pendingHasSampling;
        }

        long m_NextProduction;
        public long NextProduction() => ++m_NextProduction;

        readonly HistoryCache m_History = new HistoryCache();
        readonly Dictionary<FShadowCacheKey, Entry> m_Entries = new Dictionary<FShadowCacheKey, Entry>();
        readonly List<FShadowCacheKey> m_Scratch = new List<FShadowCacheKey>();
        readonly Stack<int> m_FreeSlots = new Stack<int>();

        public int AtlasWidth { get; private set; }
        public int AtlasHeight { get; private set; }
        public int TileResolution { get; private set; }
        public int SlotsPerRow { get; private set; }
        public int RowCount { get; private set; }
        public int SlotCount => SlotsPerRow * RowCount;
        public int EntryCount => m_Entries.Count;
        public int RetiredQueuedCount => m_History.RetiredQueuedCount;

        // Rendered / reused tiles of the last Acquire calls (Rendering Debugger).
        public int RenderedThisFrame { get; private set; }
        public int ReusedThisFrame { get; private set; }

        public void BeginFrame()
        {
            m_History.BeginFrame();
            RenderedThisFrame = 0;
            ReusedThisFrame = 0;
        }

        // The atlas has the frame atlas height, so a tile copy keeps the same row in both textures whatever origin
        // the graphics API uses for copy regions. Changing any size empties the cache (every tile re-renders).
        public void Configure(int atlasWidth, int atlasHeight, int tileResolution)
        {
            atlasWidth = Mathf.Max(0, atlasWidth);
            atlasHeight = Mathf.Max(0, atlasHeight);
            tileResolution = Mathf.Max(1, tileResolution);
            if (atlasWidth == AtlasWidth && atlasHeight == AtlasHeight && tileResolution == TileResolution)
            {
                return;
            }
            AtlasWidth = atlasWidth;
            AtlasHeight = atlasHeight;
            TileResolution = tileResolution;
            SlotsPerRow = atlasWidth / tileResolution;
            RowCount = atlasHeight / tileResolution;
            m_Entries.Clear();
            m_FreeSlots.Clear();
            for (int slot = SlotCount - 1; slot >= 0; --slot)
            {
                m_FreeSlots.Push(slot);
            }
        }

        public Vector4 SlotPixelRect(int slot)
        {
            int row = SlotsPerRow > 0 ? slot / SlotsPerRow : 0;
            int col = SlotsPerRow > 0 ? slot % SlotsPerRow : 0;
            return new Vector4(col * TileResolution, row * TileResolution, TileResolution, TileResolution);
        }

        public TextureDescriptor AtlasDescriptor()
        {
            var descriptor = new TextureDescriptor(Mathf.Max(1, AtlasWidth), Mathf.Max(1, AtlasHeight));
            descriptor.name = "CachedLocalShadowMapTexture";
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = GraphicsFormat.None;
            descriptor.depthBufferBits = EDepthBits.Depth32;
            descriptor.isShadowMap = true;
            descriptor.filterMode = FilterMode.Bilinear;
            return descriptor;
        }

        // The persistent atlas. A newly allocated texture holds no valid tile.
        public TextureHandle GetAtlas()
        {
            TextureHandle atlas = m_History.GetTexture(AtlasID, AtlasDescriptor(), out bool created);
            if (created)
            {
                InvalidateAll();
            }
            return atlas;
        }

        // Finds or allocates the tile of key. Returns -1 when no slot is free (the caller renders it every frame);
        // render is true when the tile content is missing or stale for its update mode.
        public int Acquire(in FShadowCacheKey key, EShadowUpdateMode mode, int contentHash, int version, int frame, out bool render)
        {
            render = true;
            if (mode == EShadowUpdateMode.EveryFrame || SlotCount <= 0)
            {
                return -1;
            }

            if (!m_Entries.TryGetValue(key, out Entry entry))
            {
                int slot = AllocateSlot(frame);
                if (slot < 0)
                {
                    return -1;
                }
                entry = new Entry { slot = slot };
            }

            entry.lastUsedFrame = frame;
            if (entry.pending && entry.produced && entry.pendingVersion == version
                && (mode == EShadowUpdateMode.OnDemand || entry.pendingHash == contentHash))
            {
                // An earlier camera of this frame already rendered this tile.
                render = false;
            }
            else
            {
                bool stale = !entry.valid || entry.version != version || entry.pending;
                if (mode == EShadowUpdateMode.OnEnable) stale |= entry.contentHash != contentHash;
                render = stale;
            }

            if (render)
            {
                entry.pending = true;
                entry.produced = false;
                entry.pendingHash = contentHash;
                entry.pendingVersion = version;
                entry.pendingHasSampling = false;
                RenderedThisFrame++;
            }
            else
            {
                ReusedThisFrame++;
            }
            m_Entries[key] = entry;
            return entry.slot;
        }

        // Sampling metadata belongs to the queued depth production, not the current light pose.
        public void SetPendingSampling(in FShadowCacheKey key, in SamplingState sampling)
        {
            if (!m_Entries.TryGetValue(key, out Entry entry) || !entry.pending)
                throw new InvalidOperationException("InfinityRP: shadow sampling state requires a pending cache tile.");
            entry.pendingSampling = sampling;
            entry.pendingHasSampling = true;
            m_Entries[key] = entry;
        }

        // Resolve a complete cube/single-face production before allocation. Missing faces invalidate the whole group:
        // mixing an old face with a new projection is never a valid OnDemand snapshot.
        public SamplingState[] ResolveSampling(ulong lightId, ulong sceneMask, int version, int frame)
        {
            var firstKey = new FShadowCacheKey(lightId, 0, sceneMask);
            m_Entries.TryGetValue(firstKey, out Entry first);
            SamplingState firstState = first.pending && first.produced ? first.pendingSampling : first.sampling;
            int count = firstState.faceCount;
            bool complete = count == 1 || count == 6;
            var result = complete ? new SamplingState[count] : null;
            for (int face = 0; complete && face < count; ++face)
            {
                var key = new FShadowCacheKey(lightId, face, sceneMask);
                complete = m_Entries.TryGetValue(key, out Entry entry);
                if (!complete) break;
                bool pending = entry.pending && entry.produced;
                complete = pending ? entry.pendingHasSampling && entry.pendingVersion == version
                    : !entry.pending && entry.valid && entry.hasSampling && entry.version == version;
                SamplingState state = pending ? entry.pendingSampling : entry.sampling;
                complete &= state.faceCount == count && state.production == firstState.production;
                if (complete) result[face] = state;
            }
            m_Scratch.Clear();
            foreach (var pair in m_Entries)
                if (pair.Key.lightId == lightId && pair.Key.sceneCullingMask == sceneMask) m_Scratch.Add(pair.Key);
            foreach (var key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!complete)
                {
                    entry.valid = false;
                    entry.hasSampling = false;
                    entry.pending = false;
                    entry.produced = false;
                    entry.pendingHasSampling = false;
                }
                m_Entries[key] = entry;
            }
            return complete ? result : null;
        }

        // Check the complete group before Acquire mutates any slot. A cube never uses a partial cache allocation.
        public bool CanAcquireGroup(ulong lightId, ulong sceneMask, int faceCount, int frame)
        {
            if (SlotCount < faceCount) return false;
            int available = m_FreeSlots.Count;
            foreach (var pair in m_Entries)
            {
                bool ownFace = pair.Key.lightId == lightId && pair.Key.sceneCullingMask == sceneMask && pair.Key.face < faceCount;
                if (ownFace || (pair.Value.lastUsedFrame < frame && !pair.Value.pending)) available++;
            }
            return available >= faceCount;
        }

        public void PinSampling(ulong lightId, ulong sceneMask, int faceCount, int frame)
        {
            for (int face = 0; face < faceCount; ++face)
            {
                var key = new FShadowCacheKey(lightId, face, sceneMask);
                if (!m_Entries.TryGetValue(key, out Entry entry))
                    throw new InvalidOperationException("InfinityRP: resolved shadow group lost a tile before pinning.");
                entry.lastUsedFrame = frame;
                m_Entries[key] = entry;
            }
        }

        // The pass that renders these tiles was queued.
        public void MarkProduced(FShadowCacheKey[] keys, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                if (m_Entries.TryGetValue(keys[i], out Entry entry) && entry.pending)
                {
                    entry.produced = true;
                    m_Entries[keys[i]] = entry;
                }
            }
        }

        public void CommitFrame()
        {
            m_History.CommitFrame();
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FShadowCacheKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending) continue;
                if (entry.produced)
                {
                    entry.valid = true;
                    entry.contentHash = entry.pendingHash;
                    entry.version = entry.pendingVersion;
                    entry.sampling = entry.pendingSampling;
                    entry.hasSampling = entry.pendingHasSampling;
                }
                else
                {
                    // Recorded but never queued: the tile may be partly written.
                    entry.valid = false;
                }
                entry.pending = false;
                entry.produced = false;
                m_Entries[key] = entry;
            }
        }

        // The frame failed: every tile written this frame is undefined.
        public void RollbackPending()
        {
            m_History.RollbackPending();
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FShadowCacheKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending) continue;
                entry.valid = false;
                entry.pending = false;
                entry.produced = false;
                m_Entries[key] = entry;
            }
        }

        public bool IsValid(in FShadowCacheKey key) => m_Entries.TryGetValue(key, out Entry entry) && entry.valid;

        public void InvalidateAll()
        {
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FShadowCacheKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                entry.valid = false;
                m_Entries[key] = entry;
            }
        }

        int AllocateSlot(int frame)
        {
            if (m_FreeSlots.Count > 0)
            {
                return m_FreeSlots.Pop();
            }
            // Evict the least recently used tile that this frame has not touched.
            bool found = false;
            FShadowCacheKey victim = default;
            int oldest = int.MaxValue;
            foreach (KeyValuePair<FShadowCacheKey, Entry> pair in m_Entries)
            {
                if (pair.Value.lastUsedFrame >= frame || pair.Value.pending) continue;
                if (pair.Value.lastUsedFrame < oldest)
                {
                    oldest = pair.Value.lastUsedFrame;
                    victim = pair.Key;
                    found = true;
                }
            }
            if (!found)
            {
                return -1;
            }
            int slot = m_Entries[victim].slot;
            m_Entries.Remove(victim);
            return slot;
        }

        public void FlushRetired(ResourceReuseFence fence = default) => m_History.FlushRetired(fence);

        public void Dispose()
        {
            m_History.Release();
            m_History.ForceFlushForTeardown();
            m_Entries.Clear();
        }
    }
}
