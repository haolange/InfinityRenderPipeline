using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.LightPipeline
{
    // A probe's cube in the atlas: the probe and the texture it currently shows.
    internal readonly struct FProbeCubeKey : IEquatable<FProbeCubeKey>
    {
        public readonly ulong probeId;
        public readonly ulong textureId;

        public FProbeCubeKey(ulong probeId, ulong textureId)
        {
            this.probeId = probeId;
            this.textureId = textureId;
        }

        public bool Equals(FProbeCubeKey other) => probeId == other.probeId && textureId == other.textureId;
        public override bool Equals(object obj) => obj is FProbeCubeKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(probeId, textureId);
    }

    // Blending priority of reflection probes (list order of the probe index space): higher importance first, then the
    // smaller influence volume, so a local probe inside a larger one wins at equal importance.
    internal static class ReflectionProbePriority
    {
        public static float Volume(Bounds bounds) => bounds.size.x * bounds.size.y * bounds.size.z;

        public static int Compare(int importanceA, float volumeA, int importanceB, float volumeB)
        {
            if (importanceA != importanceB) return importanceB.CompareTo(importanceA);
            return volumeA.CompareTo(volumeB);
        }
    }

    internal struct FProbeCubeUpload
    {
        public FProbeCubeKey key;
        public Texture source;
        public int slot;
    }

    /// <summary>
    /// GGX-prefiltered reflection probe cubes: one persistent Texture2DArray (6 slices per probe, CubemapFaceDirection
    /// layout, mip m = roughness m / (mipCount - 1)) with fixed slots. A baked or custom cube is filtered when it enters
    /// a slot or its contents, sampling format or Texture.updateCount changes. A realtime probe is filtered every frame
    /// it is visible (once per frame across cameras). Same transaction as the cookie atlas: filtering is pending until its
    /// pass is queued and the frame is submitted; a failed frame invalidates what it wrote. Slots of probes not used
    /// this frame are reused least-recently-used.
    /// </summary>
    internal sealed class ReflectionProbeAtlas : IDisposable
    {
        public static readonly int AtlasID = Shader.PropertyToID("_ReflectionProbeAtlasTexture");

        readonly struct SourceState : IEquatable<SourceState>
        {
            readonly Hash128 imageHash;
            readonly GraphicsFormat format;
            readonly int width, height, mipCount;

            public SourceState(Texture texture)
            {
                imageHash = default;
#if UNITY_EDITOR
                // Reimport can preserve both texture identity and updateCount.
                if (texture != null) imageHash = texture.imageContentsHash;
#endif
                format = texture != null ? texture.graphicsFormat : GraphicsFormat.None;
                width = texture != null ? texture.width : 0;
                height = texture != null ? texture.height : 0;
                mipCount = texture != null ? texture.mipmapCount : 0;
            }

            public bool Equals(SourceState other) => imageHash == other.imageHash && format == other.format
                && width == other.width && height == other.height && mipCount == other.mipCount;
        }

        struct Entry
        {
            public int slot;
            public bool valid;
            public uint updateCount;
            public int lastUsedFrame;
            public bool pending;
            public bool produced;
            public uint pendingUpdateCount;
            public SourceState source;
            public SourceState pendingSource;
            public int pendingFrame;
        }

        readonly HistoryCache m_History = new HistoryCache();
        readonly Dictionary<FProbeCubeKey, Entry> m_Entries = new Dictionary<FProbeCubeKey, Entry>();
        readonly List<FProbeCubeKey> m_Scratch = new List<FProbeCubeKey>();
        readonly Stack<int> m_FreeSlots = new Stack<int>();
        readonly List<FProbeCubeUpload> m_Uploads = new List<FProbeCubeUpload>();

        public int Resolution { get; private set; }
        public int SlotCount { get; private set; }
        public int MipCount => Resolution > 0 ? (int)Math.Log(Resolution, 2) + 1 : 1;
        public bool Enabled => Resolution > 0 && SlotCount > 0;
        public int EntryCount => m_Entries.Count;
        public List<FProbeCubeUpload> Uploads => m_Uploads;
        public int RejectedThisFrame { get; private set; }

        public void BeginFrame()
        {
            m_History.BeginFrame();
            m_Uploads.Clear();
            RejectedThisFrame = 0;
        }

        // Resolution 0 or no slots disables probes. Changing either empties the atlas.
        public void Configure(int resolution, int slotCount)
        {
            resolution = resolution > 0 ? Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 16, 1024)) : 0;
            slotCount = Mathf.Clamp(slotCount, 0, 340);
            if (resolution == Resolution && slotCount == SlotCount) return;
            Resolution = resolution;
            SlotCount = slotCount;
            m_Entries.Clear();
            m_FreeSlots.Clear();
            for (int slot = slotCount - 1; slot >= 0; --slot) m_FreeSlots.Push(slot);
        }

        public TextureDescriptor AtlasDescriptor()
        {
            var descriptor = new TextureDescriptor(Mathf.Max(1, Resolution), Mathf.Max(1, Resolution), Mathf.Max(6, 6 * SlotCount));
            descriptor.name = "ReflectionProbeAtlasTexture";
            descriptor.dimension = TextureDimension.Tex2DArray;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = true;
            descriptor.autoGenerateMips = false;
            descriptor.filterMode = FilterMode.Trilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
        }

        public FTextureRef GetAtlas()
        {
            FTextureRef atlas = m_History.GetTexture(AtlasID, AtlasDescriptor(), out bool created);
            if (created) InvalidateAll();
            return atlas;
        }

        // Slot of the probe's cube, queuing a filter pass when it is new, changed or realtime. False when disabled or
        // every slot is in use this frame.
        public bool Acquire(in FProbeCubeKey key, Texture source, uint updateCount, bool realtime, int frame, out int slot)
        {
            slot = -1;
            if (!Enabled) return false;
            if (!m_Entries.TryGetValue(key, out Entry entry))
            {
                if (!TryAllocate(frame, out int freeSlot))
                {
                    RejectedThisFrame++;
                    return false;
                }
                entry = new Entry { slot = freeSlot };
            }

            entry.lastUsedFrame = frame;
            var sourceState = new SourceState(source);
            bool current = entry.pending
                ? entry.pendingFrame == frame && entry.pendingUpdateCount == updateCount && entry.pendingSource.Equals(sourceState)
                : !realtime && entry.valid && entry.updateCount == updateCount && entry.source.Equals(sourceState);
            if (!current)
            {
                entry.pending = true;
                entry.produced = false;
                entry.pendingUpdateCount = updateCount;
                entry.pendingSource = sourceState;
                entry.pendingFrame = frame;
                if (source != null) m_Uploads.Add(new FProbeCubeUpload { key = key, source = source, slot = entry.slot });
            }
            m_Entries[key] = entry;
            slot = entry.slot;
            return true;
        }

        bool TryAllocate(int frame, out int slot)
        {
            if (m_FreeSlots.Count > 0)
            {
                slot = m_FreeSlots.Pop();
                return true;
            }
            slot = -1;
            bool found = false;
            FProbeCubeKey victim = default;
            int oldest = int.MaxValue;
            foreach (KeyValuePair<FProbeCubeKey, Entry> pair in m_Entries)
            {
                if (pair.Value.lastUsedFrame >= frame || pair.Value.pending) continue;
                if (pair.Value.lastUsedFrame < oldest)
                {
                    oldest = pair.Value.lastUsedFrame;
                    victim = pair.Key;
                    found = true;
                }
            }
            if (!found) return false;
            slot = m_Entries[victim].slot;
            m_Entries.Remove(victim);
            return true;
        }

        public void MarkProduced(FProbeCubeKey[] keys, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                if (m_Entries.TryGetValue(keys[i], out Entry entry) && entry.pending)
                {
                    // Queue acceptance overwrites the physical slot; a later discarded update
                    // cannot make its previously committed contents reusable again.
                    entry.valid = false;
                    entry.produced = true;
                    m_Entries[keys[i]] = entry;
                }
            }
        }

        // Recording consumes uploads before queue acceptance. Return unproduced reservations
        // after a camera fails so the next camera can filter the same version this frame.
        public void DiscardUnproducedPending()
        {
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FProbeCubeKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending || entry.produced) continue;
                entry.pending = false;
                m_Entries[key] = entry;
            }
            for (int i = m_Uploads.Count - 1; i >= 0; --i)
            {
                if (!m_Entries.TryGetValue(m_Uploads[i].key, out Entry entry) || !entry.pending)
                    m_Uploads.RemoveAt(i);
            }
        }

        public void CommitFrame()
        {
            m_History.CommitFrame();
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FProbeCubeKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending) continue;
                entry.valid = entry.produced;
                if (entry.produced)
                {
                    entry.updateCount = entry.pendingUpdateCount;
                    entry.source = entry.pendingSource;
                }
                entry.pending = false;
                entry.produced = false;
                m_Entries[key] = entry;
            }
        }

        public void RollbackPending()
        {
            m_History.RollbackPending();
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FProbeCubeKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending) continue;
                entry.valid = false;
                entry.pending = false;
                entry.produced = false;
                m_Entries[key] = entry;
            }
        }

        public bool IsValid(in FProbeCubeKey key) => m_Entries.TryGetValue(key, out Entry entry) && entry.valid;

        public void InvalidateAll()
        {
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FProbeCubeKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                entry.valid = false;
                m_Entries[key] = entry;
            }
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
