using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Identity for a shared per-view visibility result within a frame.
    /// </summary>
    public struct MeshVisibilitySignature : IEquatable<MeshVisibilitySignature>
    {
        public int sceneId;
        public ulong viewKey;
        public ulong frustumHash;
        public int sceneVisibilityRevision;
        public int policyId;

        public MeshVisibilitySignature(int sceneId, ulong viewKey, ulong frustumHash, int sceneVisibilityRevision, int policyId)
        {
            this.sceneId = sceneId;
            this.viewKey = viewKey;
            this.frustumHash = frustumHash;
            this.sceneVisibilityRevision = sceneVisibilityRevision;
            this.policyId = policyId;
        }

        public bool Equals(MeshVisibilitySignature other)
        {
            return sceneId == other.sceneId
                && viewKey == other.viewKey
                && frustumHash == other.frustumHash
                && sceneVisibilityRevision == other.sceneVisibilityRevision
                && policyId == other.policyId;
        }

        public override bool Equals(object obj)
        {
            return obj is MeshVisibilitySignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = sceneId;
                hash = (hash * 397) ^ (int)(viewKey ^ (viewKey >> 32));
                hash = (hash * 397) ^ (int)(frustumHash ^ (frustumHash >> 32));
                hash = (hash * 397) ^ sceneVisibilityRevision;
                hash = (hash * 397) ^ policyId;
                return hash;
            }
        }
    }

    /// <summary>
    /// Ref-counted handle to a shared <see cref="MeshViewCullingResult"/>.
    /// </summary>
    public struct MeshVisibilityHandle : IEquatable<MeshVisibilityHandle>
    {
        internal int slot;
        internal int generation;

        public static MeshVisibilityHandle Invalid => new MeshVisibilityHandle { slot = -1, generation = 0 };

        public bool IsValid => slot >= 0 && generation != 0;

        public bool Equals(MeshVisibilityHandle other)
        {
            return slot == other.slot && generation == other.generation;
        }

        public override bool Equals(object obj)
        {
            return obj is MeshVisibilityHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (slot * 397) ^ generation;
        }
    }

    /// <summary>
    /// Per-frame visibility interning: same sceneId + viewKey + frustumHash + revision + policy → one Cull.
    /// </summary>
    public sealed class MeshVisibilityShare : IDisposable
    {
        public const int PolicyMainFrustum = 0;
        public const int PolicyCascadeShadow = 1;
        public const int PolicyLocalShadow = 2;

        /// <summary>
        /// Quantize plane floats (~1e-3) then FNV-1a mix so tiny FP noise does not split shares.
        /// </summary>
        public static ulong HashFrustum(Plane[] planes)
        {
            unchecked
            {
                ulong hash = 14695981039346656037ul;
                if (planes == null)
                {
                    return hash;
                }

                for (int i = 0; i < planes.Length; ++i)
                {
                    Plane plane = planes[i];
                    hash = MixQuantized(hash, plane.normal.x);
                    hash = MixQuantized(hash, plane.normal.y);
                    hash = MixQuantized(hash, plane.normal.z);
                    hash = MixQuantized(hash, plane.distance);
                }

                return hash;
            }
        }

        public static ulong HashFrustum(ref ScriptableCullingParameters cullingParameters)
        {
            var planes = new Plane[cullingParameters.cullingPlaneCount];
            for (int i = 0; i < planes.Length; ++i)
            {
                planes[i] = cullingParameters.GetCullingPlane(i);
            }

            return HashFrustum(planes);
        }

        public static ulong MakeCameraViewKey(Camera camera)
        {
            return UnityEntityId.ToUInt64(camera);
        }

        /// <summary>
        /// Cascade shadow views must not share main-camera frustum cull results.
        /// </summary>
        public static ulong MakeCascadeViewKey(int lightInstanceId, int cascadeIndex)
        {
            return ((ulong)(uint)lightInstanceId) ^ (((ulong)(uint)(cascadeIndex + 1)) << 32);
        }

        /// <summary>
        /// Local (spot/point) shadow face views; high bit separates from cascade keys.
        /// faceIndex: Spot = 0; Point = CubemapFace 0..5.
        /// </summary>
        public static ulong MakeLocalShadowViewKey(int lightInstanceId, int faceIndex)
        {
            return ((ulong)(uint)lightInstanceId) ^ (((ulong)(uint)(faceIndex + 1)) << 32) ^ (1ul << 63);
        }

        private struct Entry
        {
            public MeshVisibilitySignature signature;
            public MeshViewCullingResult result;
            public int refCount;
            public int generation;
            public bool live;
        }

        private readonly List<Entry> m_Entries = new List<Entry>(8);
        private readonly Dictionary<MeshVisibilitySignature, int> m_Lookup = new Dictionary<MeshVisibilitySignature, int>(8);
        private readonly Stack<int> m_FreeSlots = new Stack<int>(8);

        private int m_FrameVisibilityRevision = int.MinValue;
        private bool m_Disposed;

        public void BeginFrame(int sceneVisibilityRevision)
        {
            if (m_FrameVisibilityRevision != sceneVisibilityRevision)
            {
                InvalidateAll();
                m_FrameVisibilityRevision = sceneVisibilityRevision;
            }
        }

        public MeshVisibilityHandle Acquire(
            MeshScene scene,
            ulong viewKey,
            ref ScriptableCullingParameters cullingParameters,
            int policyId,
            bool enable)
        {
            if (!enable || scene == null)
            {
                MeshPipelineDiagnostics.CulledPassSkippedBuilds++;
                return MeshVisibilityHandle.Invalid;
            }

            ulong frustumHash = HashFrustum(ref cullingParameters);
            var signature = new MeshVisibilitySignature(
                scene.SceneId, viewKey, frustumHash, scene.VisibilityRevision, policyId);
            if (m_Lookup.TryGetValue(signature, out int slot))
            {
                return AddRef(new MeshVisibilityHandle { slot = slot, generation = m_Entries[slot].generation });
            }

            MeshViewCullingResult result = MeshVisibilityUtility.CullInstances(scene, ref cullingParameters, enable);
            return Insert(signature, result);
        }

        public MeshVisibilityHandle Acquire(MeshScene scene, ulong viewKey, Plane[] planes, int policyId, bool enable)
        {
            if (!enable || scene == null)
            {
                MeshPipelineDiagnostics.CulledPassSkippedBuilds++;
                return MeshVisibilityHandle.Invalid;
            }

            ulong frustumHash = HashFrustum(planes);
            var signature = new MeshVisibilitySignature(
                scene.SceneId, viewKey, frustumHash, scene.VisibilityRevision, policyId);
            if (m_Lookup.TryGetValue(signature, out int slot))
            {
                return AddRef(new MeshVisibilityHandle { slot = slot, generation = m_Entries[slot].generation });
            }

            MeshViewCullingResult result = MeshVisibilityUtility.CullInstances(scene, planes, enable);
            return Insert(signature, result);
        }

        public MeshVisibilityHandle AddRef(MeshVisibilityHandle handle)
        {
            if (!TryGetEntry(handle, out int slot))
            {
                return MeshVisibilityHandle.Invalid;
            }

            Entry entry = m_Entries[slot];
            entry.refCount += 1;
            m_Entries[slot] = entry;
            return handle;
        }

        public void Release(MeshVisibilityHandle handle)
        {
            if (!TryGetEntry(handle, out int slot))
            {
                return;
            }

            Entry entry = m_Entries[slot];
            entry.refCount -= 1;
            if (entry.refCount > 0)
            {
                m_Entries[slot] = entry;
                return;
            }

            if (entry.result.isValid)
            {
                entry.result.Release();
            }

            m_Lookup.Remove(entry.signature);
            entry.live = false;
            entry.result = default;
            entry.refCount = 0;
            entry.generation += 1;
            m_Entries[slot] = entry;
            m_FreeSlots.Push(slot);
        }

        public MeshViewCullingResult GetResult(MeshVisibilityHandle handle)
        {
            if (!TryGetEntry(handle, out int slot))
            {
                return default;
            }

            return m_Entries[slot].result;
        }

        public void EndFrame()
        {
            // Records should have released; dispose any leftover shared entries.
            InvalidateAll();
            m_FrameVisibilityRevision = int.MinValue;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            InvalidateAll();
            m_Disposed = true;
        }

        private MeshVisibilityHandle Insert(in MeshVisibilitySignature signature, in MeshViewCullingResult result)
        {
            int slot;
            Entry entry;
            if (m_FreeSlots.Count > 0)
            {
                slot = m_FreeSlots.Pop();
                entry = m_Entries[slot];
                entry.signature = signature;
                entry.result = result;
                entry.refCount = 1;
                entry.generation = entry.generation == 0 ? 1 : entry.generation + 1;
                entry.live = true;
                m_Entries[slot] = entry;
            }
            else
            {
                slot = m_Entries.Count;
                entry = new Entry
                {
                    signature = signature,
                    result = result,
                    refCount = 1,
                    generation = 1,
                    live = true
                };
                m_Entries.Add(entry);
            }

            m_Lookup[signature] = slot;
            return new MeshVisibilityHandle { slot = slot, generation = entry.generation };
        }

        private bool TryGetEntry(MeshVisibilityHandle handle, out int slot)
        {
            slot = handle.slot;
            if (slot < 0 || slot >= m_Entries.Count)
            {
                return false;
            }

            Entry entry = m_Entries[slot];
            return entry.live && entry.generation == handle.generation;
        }

        private void InvalidateAll()
        {
            for (int i = 0; i < m_Entries.Count; ++i)
            {
                Entry entry = m_Entries[i];
                if (!entry.live)
                {
                    continue;
                }

                if (entry.result.isValid)
                {
                    entry.result.Release();
                }

                entry.live = false;
                entry.result = default;
                entry.refCount = 0;
                entry.generation += 1;
                m_Entries[i] = entry;
                m_FreeSlots.Push(i);
            }

            m_Lookup.Clear();
        }

        private static ulong MixQuantized(ulong hash, float value)
        {
            unchecked
            {
                int quantized = (int)Mathf.Round(value * 1000f);
                hash ^= (uint)quantized;
                hash *= 1099511628211ul;
                return hash;
            }
        }
    }
}
