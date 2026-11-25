using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.LightPipeline
{
    // One cached shadow tile: a light, cube face and exact camera scene eligibility.
    internal readonly struct ShadowCacheKey : IEquatable<ShadowCacheKey>
    {
        public readonly ulong lightId;
        public readonly int face;
        public readonly ulong sceneCullingMask;

        public ShadowCacheKey(ulong lightId, int face, ulong sceneCullingMask = ulong.MaxValue)
        {
            this.lightId = lightId;
            this.face = face;
            this.sceneCullingMask = sceneCullingMask;
        }

        public bool Equals(ShadowCacheKey other) => lightId == other.lightId && face == other.face
            && sceneCullingMask == other.sceneCullingMask;
        public override bool Equals(object obj) => obj is ShadowCacheKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(lightId, face, sceneCullingMask);
    }

    /// <summary>
    /// Cached shadow atlas for OnEnable / OnDemand local lights with variable power-of-two tile slots backed by
    /// <see cref="ShadowAtlasLayout"/>. Keyed by light instance id, face and scene culling mask, never by record index.
    /// Transactional state tracks pending vs produced vs committed tiles; resolution tier changes relocate cleanly without
    /// reading stale texels.
    /// </summary>
    internal sealed class LocalShadowCache : IFrameTransaction, IDisposable
    {
        public static readonly int AtlasID = Shader.PropertyToID("_CachedLocalShadowMapTexture");

        internal struct SamplingState
        {
            public LocalShadowSlice slice;
            public LocalShadowSliceGPU gpu;
            public int faceCount;
            public int tileResolution;
            public long production;
        }

        struct Entry
        {
            public int slot;
            public int tileResolution;
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

        readonly HistoryCache m_History;
        readonly ShadowAtlasLayout m_Layout = new ShadowAtlasLayout();
        readonly Dictionary<ShadowCacheKey, Entry> m_Entries = new Dictionary<ShadowCacheKey, Entry>();
        readonly List<ShadowCacheKey> m_Scratch = new List<ShadowCacheKey>();

        public int AtlasWidth => m_Layout.AtlasWidth;
        public int AtlasHeight => m_Layout.AtlasHeight;
        public int MinTileResolution => m_Layout.MinTileSize;
        public int MaxTileResolution => m_Layout.MaxTileSize;
        public int SlotCount => m_Layout.MaxMinTileCapacity;
        public int EntryCount => m_Entries.Count;
        public float Occupancy => m_Layout.Occupancy;
        public int RetiredQueuedCount => m_History.RetiredQueuedCount;

        // Rendered / reused / evicted / relocated tiles of the last Acquire calls (Rendering Debugger).
        public int RenderedThisFrame
        {
            get;
            private set;
        }
        public int ReusedThisFrame
        {
            get;
            private set;
        }
        public int EvictedThisFrame
        {
            get;
            private set;
        }
        public int RelocatedThisFrame
        {
            get;
            private set;
        }

        public void BeginFrame()
        {
            m_History.BeginFrame();
            RenderedThisFrame = 0;
            ReusedThisFrame = 0;
            EvictedThisFrame = 0;
            RelocatedThisFrame = 0;
        }

        // The atlas keeps the frame atlas height, so 1:1 tile copies preserve rows regardless of graphics API origin.
        // Changing dimensions or tile bounds empties the cache cleanly.
        public void Configure(int atlasWidth, int atlasHeight, int minTileResolution, int maxTileResolution)
        {
            if (m_Layout.Configure(atlasWidth, atlasHeight, minTileResolution, maxTileResolution))
                m_Entries.Clear();
        }

        public Vector4 SlotPixelRect(int slot) => m_Layout.SlotPixelRect(slot);

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
            TextureHandle atlasHandle = m_History.GetTexture(AtlasID, AtlasDescriptor(), out bool created);
            if (created)
            {
                InvalidateAll();
            }
            return atlasHandle;
        }

        // Finds, relocates or allocates the variable-resolution tile of key. Returns -1 when no slot is free
        // (the caller renders it every frame into the frame atlas); render is true when missing, relocated, or stale.
        public int Acquire(in ShadowCacheKey key, EShadowUpdateMode mode, int tileResolution, int contentHash, int version, int frame, out bool render)
        {
            render = true;
            if (mode == EShadowUpdateMode.EveryFrame || SlotCount <= 0 || tileResolution <= 0 || tileResolution > MaxTileResolution)
            {
                return -1;
            }

            if (m_Entries.TryGetValue(key, out Entry entry))
            {
                if (entry.tileResolution != tileResolution)
                {
                    m_Layout.Free(entry.slot);
                    m_Entries.Remove(key);
                    RelocatedThisFrame++;
                    entry = default;
                    entry.slot = -1;
                }
            }
            else
            {
                entry.slot = -1;
            }

            if (entry.slot < 0)
            {
                int slot = AllocateSlot(tileResolution, frame);
                if (slot < 0)
                {
                    return -1;
                }
                entry = new Entry { slot = slot, tileResolution = tileResolution };
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
                if (mode == EShadowUpdateMode.OnEnable)
                {
                    stale |= entry.contentHash != contentHash;
                }
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
        public void SetPendingSampling(in ShadowCacheKey key, in SamplingState sampling)
        {
            if (!m_Entries.TryGetValue(key, out Entry entry) || !entry.pending)
                throw new InvalidOperationException("InfinityRP: shadow sampling state requires a pending cache tile.");
            entry.pendingSampling = sampling;
            entry.pendingHasSampling = true;
            m_Entries[key] = entry;
        }

        // Resolve a complete cube/single-face production before allocation. Missing faces invalidate the whole group:
        // mixing an old face with a new projection is never a valid OnDemand snapshot. Writes the faces into `result`
        // (six entries) and returns their count, or 0 when no complete production exists.
        public int ResolveSampling(ulong lightId, ulong sceneMask, int version, int frame, Span<SamplingState> result)
        {
            var firstKey = new ShadowCacheKey(lightId, 0, sceneMask);
            m_Entries.TryGetValue(firstKey, out Entry first);
            SamplingState firstState = first.pending && first.produced ? first.pendingSampling : first.sampling;
            int count = firstState.faceCount;
            bool complete = count == 1 || count == 6;
            for (int face = 0; complete && face < count; ++face)
            {
                var key = new ShadowCacheKey(lightId, face, sceneMask);
                complete = m_Entries.TryGetValue(key, out Entry entry);
                if (!complete)
                {
                    break;
                }
                bool pending = entry.pending && entry.produced;
                complete = pending ? entry.pendingHasSampling && entry.pendingVersion == version
                    : !entry.pending && entry.valid && entry.hasSampling && entry.version == version;
                SamplingState state = pending ? entry.pendingSampling : entry.sampling;
                complete &= state.faceCount == count && state.production == firstState.production
                    && (state.tileResolution <= 0 || state.tileResolution == entry.tileResolution);
                if (complete)
                {
                    result[face] = state;
                }
            }
            m_Scratch.Clear();
            foreach (var pair in m_Entries)
                if (pair.Key.lightId == lightId && pair.Key.sceneCullingMask == sceneMask)
                {
                    m_Scratch.Add(pair.Key);
                }
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
            return complete ? count : 0;
        }

        // Check total available texel capacity before Acquire mutates any slot. A cube never uses a partial cache allocation.
        public bool CanAcquireGroup(ulong lightId, ulong sceneMask, int faceCount, int tileResolution, int frame)
        {
            if (SlotCount <= 0 || tileResolution <= 0 || tileResolution > MaxTileResolution)
                return false;

            long requiredTexels = (long)faceCount * tileResolution * tileResolution;
            if (m_Layout.TotalTexels < requiredTexels)
                return false;

            long freeTexels = m_Layout.TotalTexels - m_Layout.AllocatedTexels;
            foreach (var pair in m_Entries)
            {
                bool ownFace = pair.Key.lightId == lightId && pair.Key.sceneCullingMask == sceneMask && pair.Key.face < faceCount;
                if (ownFace)
                {
                    freeTexels += (long)pair.Value.tileResolution * pair.Value.tileResolution;
                }
                else if (pair.Value.lastUsedFrame < frame && !pair.Value.pending)
                {
                    freeTexels += (long)pair.Value.tileResolution * pair.Value.tileResolution;
                }
            }
            return freeTexels >= requiredTexels;
        }

        public void PinSampling(ulong lightId, ulong sceneMask, int faceCount, int frame)
        {
            for (int face = 0; face < faceCount; ++face)
            {
                var key = new ShadowCacheKey(lightId, face, sceneMask);
                if (!m_Entries.TryGetValue(key, out Entry entry))
                    throw new InvalidOperationException("InfinityRP: resolved shadow group lost a tile before pinning.");
                entry.lastUsedFrame = frame;
                m_Entries[key] = entry;
            }
        }

        // Drops pending state across a light group if all faces could not be atomically acquired together,
        // freeing the abandoned slots so the rest of the frame can reuse that atlas area.
        public void ReleasePendingGroup(ulong lightId, ulong sceneMask, int faceCount)
        {
            for (int face = 0; face < faceCount; ++face)
            {
                var key = new ShadowCacheKey(lightId, face, sceneMask);
                if (!m_Entries.TryGetValue(key, out Entry entry) || !entry.pending)
                    continue;

                m_Layout.Free(entry.slot);
                m_Entries.Remove(key);
                RenderedThisFrame = Mathf.Max(0, RenderedThisFrame - 1);
            }
        }

        // The pass that renders these tiles was queued.
        public void MarkProduced(ShadowCacheKey[] keys, int count)
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
            foreach (ShadowCacheKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending)
                {
                    continue;
                }
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

        // Production is marked when a pass is queued, and CommitFrame invalidates every recorded but unqueued tile, so a
        // failed camera needs no action here.
        public void CameraFailed(bool queueUncertain) { }

        // The frame failed: every tile written this frame is undefined.
        public void RollbackPending()
        {
            m_History.RollbackPending();
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (ShadowCacheKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending)
                {
                    continue;
                }
                entry.valid = false;
                entry.pending = false;
                entry.produced = false;
                m_Entries[key] = entry;
            }
        }

        public bool IsValid(in ShadowCacheKey key) => m_Entries.TryGetValue(key, out Entry entry) && entry.valid;

        public void InvalidateAll()
        {
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (ShadowCacheKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                entry.valid = false;
                m_Entries[key] = entry;
            }
        }

        int AllocateSlot(int tileResolution, int frame)
        {
            if (m_Layout.TryAllocate(tileResolution, out int handle, out _))
            {
                return handle;
            }

            // Evict least-recently-used tiles from prior frames until buddy coalescing yields a slot of tileResolution.
            while (true)
            {
                bool found = false;
                ShadowCacheKey victim = default;
                int oldest = int.MaxValue;
                foreach (KeyValuePair<ShadowCacheKey, Entry> pair in m_Entries)
                {
                    if (pair.Value.lastUsedFrame >= frame || pair.Value.pending)
                    {
                        continue;
                    }
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
                int freedSlot = m_Entries[victim].slot;
                m_Entries.Remove(victim);
                m_Layout.Free(freedSlot);
                EvictedThisFrame++;
                if (m_Layout.TryAllocate(tileResolution, out handle, out _))
                {
                    return handle;
                }
            }
        }

        public void FlushRetired(ResourceReuseFence fence) => m_History.FlushRetired(fence);

        public LocalShadowCache(ResourcePool pool)
        {
            m_History = new HistoryCache(pool);
        }

        public void Dispose()
        {
            m_History.Dispose();
            m_Entries.Clear();
        }
    }
}
