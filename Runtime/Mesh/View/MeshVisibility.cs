using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Culling identity of a view within one camera render, shared by CPU visibility interning and GPU visibility buffers.
    /// Field-level equality is the identity; the hash only accelerates lookup.
    /// </summary>
    public readonly struct MeshVisibilityKey : IEquatable<MeshVisibilityKey>
    {
        public readonly ulong viewKey;
        readonly EMeshVisibilityPhase m_Phase;
        internal EMeshVisibilityPhase phase => m_Phase;
        readonly ulong m_History;
        public readonly EMeshViewKind kind;
        public readonly int subviewIndex;
        public readonly int layerMask;
        public readonly ulong sceneCullingMask;
        public readonly uint renderingLayerMask;
        public readonly bool filterRenderingLayers;
        public readonly MeshLODProjection lodProjection;

        /// <param name="requestIdentity">Includes the occlusion history, so a recovered request never shares a stale buffer.</param>
        /// <param name="residentIdentity">
        /// Drops the LOD projection. A resident GPU buffer lives across frames while the projection moves with the camera;
        /// within one camera render a view's projection is fixed, like its planes.
        /// </param>
        internal MeshVisibilityKey(in MeshView view, bool requestIdentity, bool residentIdentity = false)
        {
            viewKey = view.viewKey;
            m_Phase = view.visibilityPhase;
            m_History = requestIdentity ? view.visibilityHistory : 0;
            kind = view.isShadow ? view.kind : EMeshViewKind.Main;
            subviewIndex = view.subviewIndex;
            layerMask = view.layerMask;
            sceneCullingMask = view.sceneCullingMask;
            renderingLayerMask = view.renderingLayerMask;
            filterRenderingLayers = view.FilterRenderingLayers;
            lodProjection = residentIdentity ? default : view.lodProjection;
        }

        public bool Equals(MeshVisibilityKey other)
        {
            return viewKey == other.viewKey && m_Phase == other.m_Phase && m_History == other.m_History && kind == other.kind
                && subviewIndex == other.subviewIndex && layerMask == other.layerMask && sceneCullingMask == other.sceneCullingMask
                && renderingLayerMask == other.renderingLayerMask && filterRenderingLayers == other.filterRenderingLayers
                && lodProjection.Equals(other.lodProjection);
        }

        public override bool Equals(object obj) => obj is MeshVisibilityKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = viewKey.GetHashCode();
                hash = hash * 31 + (int)m_Phase;
                hash = hash * 31 + m_History.GetHashCode();
                hash = hash * 31 + (int)kind;
                hash = hash * 31 + subviewIndex;
                hash = hash * 31 + layerMask;
                hash = hash * 31 + sceneCullingMask.GetHashCode();
                hash = hash * 31 + (int)renderingLayerMask;
                hash = hash * 31 + lodProjection.GetHashCode();
                return hash * 31 + (filterRenderingLayers ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// CPU visibility of one view per entity slot: visible, camera distance, selected LOD level and its fade (1 when stable,
    /// in [0, 1) inside the cross-fade band). Valid after <see cref="job"/> completes.
    /// </summary>
    internal struct MeshViewVisibility
    {
        public NativeArray<byte> visible;
        public NativeArray<float> depth;
        public NativeArray<byte> lod;
        public NativeArray<float> fade;
        public JobHandle job;
        public bool isValid => visible.IsCreated;
    }

    [BurstCompile]
    internal struct MeshEntityCullingJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<MeshEntity> entities;
        [ReadOnly] public NativeArray<uint> generations;
        [ReadOnly] public NativeArray<BoundingBox> bounds;
        public float4 plane0, plane1, plane2, plane3, plane4, plane5;
        public int planeCount;
        public float3 viewPosition;
        public int layerMask;
        public ulong sceneCullingMask;
        public uint renderingLayerMask;
        public bool filterRenderingLayers;
        public MeshLODProjection lodProjection;
        [WriteOnly] public NativeArray<byte> visible;
        [WriteOnly] public NativeArray<float> depth;
        [WriteOnly] public NativeArray<byte> lod;
        [WriteOnly] public NativeArray<float> fade;

        public void Execute(int index)
        {
            byte result = 0;
            float distance = 0;
            byte level = MeshLODSelection.None;
            float levelFade = 1;
            if ((generations[index] & 1u) != 0)
            {
                MeshEntityDesc desc = entities[index].desc;
                bool eligible = (desc.flags & EMeshEntityFlags.Visible) != 0
                    && (desc.layerMask & layerMask) != 0
                    && (desc.sceneCullingMask & sceneCullingMask) != 0
                    && (!filterRenderingLayers || (desc.renderingLayerMask & renderingLayerMask) != 0);
                if (eligible)
                {
                    BoundingBox bound = bounds[index];
                    bool inside = Inside(plane0, 0, bound) && Inside(plane1, 1, bound) && Inside(plane2, 2, bound)
                        && Inside(plane3, 3, bound) && Inside(plane4, 4, bound) && Inside(plane5, 5, bound);
                    distance = math.length(bound.center - viewPosition);
                    if (inside)
                    {
                        MeshLODSelection.Select(entities[index].lod, bound, lodProjection, out level, out levelFade);
                    }
                    result = (byte)(inside && level != MeshLODSelection.None ? 1 : 0);
                }
            }
            visible[index] = result;
            depth[index] = distance;
            lod[index] = level;
            fade[index] = levelFade;
        }

        bool Inside(float4 plane, int slot, in BoundingBox bound)
        {
            if (slot >= planeCount)
            {
                return true;
            }
            float radius = math.dot(math.abs(plane.xyz), bound.extents);
            return math.dot(plane.xyz, bound.center) + plane.w + radius >= 0;
        }
    }

    /// <summary>
    /// LOD selection shared by the CPU culling job and mirrored by <c>MeshLOD.hlsl</c>. The selected level is the first
    /// whose lower bound the screen size reaches; the fade is 1 outside the band above that bound and in [0, 1) inside it.
    /// A level whose bound is 0 never fades.
    /// </summary>
    internal static class MeshLODSelection
    {
        public const byte None = 255;

        /// <summary>
        /// The largest half-extent of the world bounds times scale and bias over distance: for a camera, the share of the
        /// view height the bounds' largest side covers, as <c>LODGroup.size</c> drives <c>screenRelativeTransitionHeight</c>.
        /// </summary>
        public static float ScreenSize(in BoundingBox bound, in MeshLODProjection projection)
        {
            float distance = projection.orthographic ? 1.0f : math.max(math.length(bound.center - projection.origin), 1e-6f);
            return math.cmax(bound.extents) * projection.scale * projection.bias / distance;
        }

        public static void Select(in MeshLODChain chain, in BoundingBox bound, in MeshLODProjection projection, out byte level, out float fade)
        {
            float screen = ScreenSize(bound, projection);
            level = None;
            fade = 1;
            for (int i = 0; i < chain.count; ++i)
            {
                float threshold = chain.ScreenSize(i);
                if (screen < threshold)
                {
                    continue;
                }
                level = (byte)i;
                if (threshold > 0)
                {
                    float previous = i == 0 ? 1.0f : chain.ScreenSize(i - 1);
                    float width = chain.fadeWidth * (previous - threshold);
                    float position = (screen - threshold) / width;
                    if (position < 1)
                    {
                        fade = position;
                    }
                }
                return;
            }
        }
    }

    /// <summary>
    /// Per-camera interning of CPU visibility: one culling job per <see cref="MeshVisibilityKey"/>. Results are TempJob
    /// arrays owned by reference counts; the draw-list registry releases its references when the graph is dropped.
    /// </summary>
    internal sealed class MeshVisibilityShare : IDisposable
    {
        struct Slot
        {
            public MeshViewVisibility result;
            public int refCount;
            public int generation;
        }

        readonly List<Slot> m_Slots = new List<Slot>(16);
        readonly Stack<int> m_FreeSlots = new Stack<int>(16);
        readonly Dictionary<MeshVisibilityKey, int> m_Interned = new Dictionary<MeshVisibilityKey, int>(16);
        bool m_Disposed;

        public int liveCount
        {
            get;
            private set;
        }

        /// <summary>Results from an earlier camera stay alive for their holders but are no longer shared.</summary>
        public void BeginCamera() => m_Interned.Clear();

        public MeshVisibilityHandle Acquire(MeshWorld world, in MeshView view)
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(nameof(MeshVisibilityShare));
            }
            var key = new MeshVisibilityKey(view, false);
            if (m_Interned.TryGetValue(key, out int existing))
            {
                Slot shared = m_Slots[existing];
                shared.refCount++;
                m_Slots[existing] = shared;
                return new MeshVisibilityHandle(existing, shared.generation);
            }

            int count = math.max(1, world.entityHighWater);
            var result = new MeshViewVisibility
            {
                visible = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                depth = new NativeArray<float>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                lod = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                fade = new NativeArray<float>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            };
            result.job = new MeshEntityCullingJob
            {
                entities = world.entities,
                generations = world.entityGenerations,
                bounds = world.entityBounds,
                plane0 = Plane(view, 0), plane1 = Plane(view, 1), plane2 = Plane(view, 2),
                plane3 = Plane(view, 3), plane4 = Plane(view, 4), plane5 = Plane(view, 5),
                planeCount = math.clamp(view.planeCount, 0, MeshView.MaxPlanes),
                viewPosition = view.viewPosition,
                layerMask = view.layerMask,
                sceneCullingMask = view.sceneCullingMask,
                renderingLayerMask = view.renderingLayerMask,
                filterRenderingLayers = view.FilterRenderingLayers,
                lodProjection = view.lodProjection,
                visible = result.visible,
                depth = result.depth,
                lod = result.lod,
                fade = result.fade
            }.Schedule(world.entityHighWater, 256);
            world.stats.visibilityProductions++;

            int slot = m_FreeSlots.Count > 0 ? m_FreeSlots.Pop() : AddSlot();
            Slot entry = m_Slots[slot];
            entry.result = result;
            entry.refCount = 1;
            entry.generation = entry.generation + 1;
            m_Slots[slot] = entry;
            m_Interned[key] = slot;
            liveCount++;
            return new MeshVisibilityHandle(slot, entry.generation);
        }

        static float4 Plane(in MeshView view, int index)
        {
            UnityEngine.Plane plane = view.GetPlane(index);
            return new float4(plane.normal, plane.distance);
        }

        int AddSlot()
        {
            m_Slots.Add(default);
            return m_Slots.Count - 1;
        }

        bool IsLive(MeshVisibilityHandle handle)
            => handle.IsValid && handle.slot < m_Slots.Count && m_Slots[handle.slot].generation == handle.generation && m_Slots[handle.slot].refCount > 0;

        public MeshViewVisibility Get(MeshVisibilityHandle handle)
        {
            if (!IsLive(handle))
            {
                throw new InvalidOperationException("The visibility handle is not live.");
            }
            return m_Slots[handle.slot].result;
        }

        public void Release(MeshVisibilityHandle handle)
        {
            if (!IsLive(handle))
            {
                throw new InvalidOperationException("The visibility handle is not live.");
            }
            Slot slot = m_Slots[handle.slot];
            if (--slot.refCount == 0)
            {
                slot.result.job.Complete();
                slot.result.visible.Dispose();
                slot.result.depth.Dispose();
                slot.result.lod.Dispose();
                slot.result.fade.Dispose();
                slot.result = default;
                m_FreeSlots.Push(handle.slot);
                liveCount--;
                foreach (var pair in m_Interned)
                    if (pair.Value == handle.slot)
                    {
                        m_Interned.Remove(pair.Key);
                        break;
                    }
            }
            m_Slots[handle.slot] = slot;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            m_Disposed = true;
            for (int i = 0; i < m_Slots.Count; ++i)
            {
                Slot slot = m_Slots[i];
                if (slot.refCount <= 0)
                {
                    continue;
                }
                slot.result.job.Complete();
                slot.result.visible.Dispose();
                slot.result.depth.Dispose();
                slot.result.lod.Dispose();
                slot.result.fade.Dispose();
            }
            m_Slots.Clear();
            m_Interned.Clear();
            liveCount = 0;
        }
    }

    /// <summary>Reference to an interned CPU visibility result.</summary>
    internal readonly struct MeshVisibilityHandle
    {
        public readonly int slot;
        public readonly int generation;
        public MeshVisibilityHandle(int slot, int generation)
        {
            this.slot = slot;
            this.generation = generation;
        }
        public bool IsValid => generation != 0;
        public static MeshVisibilityHandle Invalid => default;
    }
}
