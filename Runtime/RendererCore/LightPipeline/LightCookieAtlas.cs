using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.LightPipeline
{
    // How a cookie / IES texture is stored in the atlas.
    public enum ECookieLayout
    {
        // 2D texture copied as is (spot, directional, rect and disk cookies; 2D IES profiles).
        Projected = 0,
        // Cubemap re-projected to an octahedral square (point cookies; cube IES profiles).
        Octahedral = 1,
    }

    // Which channels of the source become the cookie color.
    public enum ECookieChannels
    {
        RGB = 0,
        // Alpha-only formats (Alpha8): alpha to RGB.
        Alpha = 1,
        // Single-channel formats (R8, R16, RFloat): red to RGB.
        Red = 2,
    }

    internal readonly struct FCookieKey : IEquatable<FCookieKey>
    {
        public readonly ulong textureId;
        public readonly ECookieLayout layout;

        public FCookieKey(ulong textureId, ECookieLayout layout)
        {
            this.textureId = textureId;
            this.layout = layout;
        }

        public bool Equals(FCookieKey other) => textureId == other.textureId && layout == other.layout;
        public override bool Equals(object obj) => obj is FCookieKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(textureId, (int)layout);
    }

    // A texture copy the cookie pass records this frame.
    internal struct FCookieUpload
    {
        public FCookieKey key;
        public Texture source;
        public ECookieLayout layout;
        public ECookieChannels channels;
        public Vector2Int position;
        public int size;
    }

    /// <summary>
    /// Light cookie and IES atlas: one persistent RGBA8 texture with a full mip chain; textures live in power-of-two
    /// blocks (AtlasQuadAllocator) keyed by texture identity and layout, re-uploaded when the source contents change.
    /// Same transaction as the cached shadow atlas: an upload is pending until its pass is queued (MarkProduced) and
    /// committed with a submitted frame; a failed frame invalidates the blocks it wrote. Blocks of textures not used
    /// this frame are evicted least-recently-used when space runs out.
    /// </summary>
    internal sealed class LightCookieAtlas : IDisposable
    {
        public static readonly int AtlasID = Shader.PropertyToID("_LightCookieAtlasTexture");
        public const int MinBlockSize = 16;

        readonly struct SourceState : IEquatable<SourceState>
        {
            readonly Hash128 imageHash;
            readonly GraphicsFormat format;
            readonly int width, height, mipCount;
            readonly ECookieChannels channels;

            public SourceState(Texture texture, int width, int height, ECookieChannels channels)
            {
                imageHash = default;
#if UNITY_EDITOR
                // Reimport can preserve both the native texture identity and updateCount.
                if (texture != null) imageHash = texture.imageContentsHash;
#endif
                format = texture != null ? texture.graphicsFormat : GraphicsFormat.None;
                this.width = width;
                this.height = height;
                mipCount = texture != null ? texture.mipmapCount : 0;
                this.channels = channels;
            }

            public bool Equals(SourceState other) => imageHash == other.imageHash && format == other.format
                && width == other.width && height == other.height && mipCount == other.mipCount && channels == other.channels;
        }

        struct Entry
        {
            public Vector2Int position;
            public int size;
            public bool valid;
            public uint updateCount;
            public int lastUsedFrame;
            public bool pending;
            public bool produced;
            public uint pendingUpdateCount;
            public SourceState source;
            public SourceState pendingSource;
        }

        readonly HistoryCache m_History = new HistoryCache();
        readonly Dictionary<FCookieKey, Entry> m_Entries = new Dictionary<FCookieKey, Entry>();
        readonly List<FCookieKey> m_Scratch = new List<FCookieKey>();
        readonly List<FCookieUpload> m_Uploads = new List<FCookieUpload>();
        AtlasQuadAllocator m_Allocator;

        public int AtlasSize { get; private set; }
        public int MaxBlockSize { get; private set; }
        public int MipCount => AtlasSize > 0 ? (int)Math.Log(AtlasSize, 2) + 1 : 1;
        public int EntryCount => m_Entries.Count;
        public bool Enabled => AtlasSize > 0;
        // Uploads recorded by Acquire this frame, consumed by the cookie pass.
        public List<FCookieUpload> Uploads => m_Uploads;
        // Textures that did not fit this frame (Rendering Debugger).
        public int RejectedThisFrame { get; private set; }

        public void BeginFrame()
        {
            m_History.BeginFrame();
            m_Uploads.Clear();
            RejectedThisFrame = 0;
        }

        // A size of 0 disables cookies. Changing a size empties the atlas.
        public void Configure(int atlasSize, int maxBlockSize)
        {
            atlasSize = atlasSize > 0 ? Mathf.ClosestPowerOfTwo(Mathf.Max(atlasSize, MinBlockSize)) : 0;
            maxBlockSize = atlasSize > 0 ? Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(maxBlockSize, MinBlockSize)), MinBlockSize, atlasSize) : 0;
            if (atlasSize == AtlasSize && maxBlockSize == MaxBlockSize)
            {
                return;
            }
            AtlasSize = atlasSize;
            MaxBlockSize = maxBlockSize;
            m_Entries.Clear();
            m_Allocator = atlasSize > 0 ? new AtlasQuadAllocator(atlasSize, MinBlockSize) : null;
        }

        public TextureDescriptor AtlasDescriptor()
        {
            var descriptor = new TextureDescriptor(Mathf.Max(1, AtlasSize), Mathf.Max(1, AtlasSize));
            descriptor.name = "LightCookieAtlasTexture";
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
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
            if (created)
            {
                InvalidateAll();
            }
            return atlas;
        }

        // Block size for a texture: its largest side (cube: twice the face size for the octahedral square), rounded
        // up to a power of two and clamped to [MinBlockSize, MaxBlockSize].
        public static int RequestedSize(int width, int height, ECookieLayout layout, int maxBlockSize)
        {
            int side = layout == ECookieLayout.Octahedral ? 2 * Mathf.Max(width, height) : Mathf.Max(width, height);
            return AtlasQuadAllocator.BlockSize(side, MinBlockSize, Mathf.Max(MinBlockSize, maxBlockSize));
        }

        // Uv rect (offset xy, scale zw) of the texture's block, queuing an upload when the block is new or stale.
        // Returns false when the atlas is disabled or full.
        public bool Acquire(Texture texture, ECookieLayout layout, ECookieChannels channels, ulong textureId, uint updateCount,
            int width, int height, int frame, out Vector4 uvRect)
        {
            uvRect = Vector4.zero;
            if (!Enabled)
            {
                return false;
            }
            var key = new FCookieKey(textureId, layout);
            int size = RequestedSize(width, height, layout, MaxBlockSize);
            if (m_Entries.TryGetValue(key, out Entry entry) && entry.size != size)
            {
                m_Allocator.Free(entry.position, entry.size);
                m_Entries.Remove(key);
                entry = default;
            }
            if (!m_Entries.ContainsKey(key))
            {
                if (!TryAllocate(size, frame, out Vector2Int position))
                {
                    RejectedThisFrame++;
                    return false;
                }
                entry = new Entry { position = position, size = size };
            }

            entry.lastUsedFrame = frame;
            var source = new SourceState(texture, width, height, channels);
            bool current = entry.pending
                ? entry.pendingUpdateCount == updateCount && entry.pendingSource.Equals(source)
                : entry.valid && entry.updateCount == updateCount && entry.source.Equals(source);
            if (!current)
            {
                entry.pending = true;
                entry.produced = false;
                entry.pendingUpdateCount = updateCount;
                entry.pendingSource = source;
                if (texture != null)
                {
                    m_Uploads.Add(new FCookieUpload { key = key, source = texture, layout = layout, channels = channels, position = entry.position, size = size });
                }
            }
            m_Entries[key] = entry;
            float inv = 1.0f / AtlasSize;
            uvRect = new Vector4(entry.position.x * inv, entry.position.y * inv, size * inv, size * inv);
            return true;
        }

        bool TryAllocate(int size, int frame, out Vector2Int position)
        {
            while (!m_Allocator.TryAllocate(size, out position))
            {
                // Evict the least recently used block not used this frame.
                bool found = false;
                FCookieKey victim = default;
                int oldest = int.MaxValue;
                foreach (KeyValuePair<FCookieKey, Entry> pair in m_Entries)
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
                    return false;
                }
                Entry evicted = m_Entries[victim];
                m_Allocator.Free(evicted.position, evicted.size);
                m_Entries.Remove(victim);
            }
            return true;
        }

        public void MarkProduced(FCookieKey[] keys, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                if (m_Entries.TryGetValue(keys[i], out Entry entry) && entry.pending)
                {
                    // Queue acceptance overwrites this physical block. The old committed version must
                    // not become reusable if a later update is discarded before it reaches the queue.
                    entry.valid = false;
                    entry.produced = true;
                    m_Entries[keys[i]] = entry;
                }
            }
        }

        // Recording can consume the upload list before any pass reaches its queue. A failed camera must
        // return those reservations so another camera can upload the same texture version this frame.
        // Keep committed contents and accepted production: neither was changed by an unqueued upload.
        public void DiscardUnproducedPending()
        {
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FCookieKey key in m_Scratch)
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
            foreach (FCookieKey key in m_Scratch)
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
            foreach (FCookieKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                if (!entry.pending) continue;
                entry.valid = false;
                entry.pending = false;
                entry.produced = false;
                m_Entries[key] = entry;
            }
        }

        public bool IsValid(in FCookieKey key) => m_Entries.TryGetValue(key, out Entry entry) && entry.valid;

        public void InvalidateAll()
        {
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Entries.Keys);
            foreach (FCookieKey key in m_Scratch)
            {
                Entry entry = m_Entries[key];
                entry.valid = false;
                m_Entries[key] = entry;
            }
        }

        public void FlushRetired() => m_History.FlushRetired();

        public void Dispose()
        {
            m_History.Release();
            m_History.ForceFlushForTeardown();
            m_Entries.Clear();
        }
    }
}
