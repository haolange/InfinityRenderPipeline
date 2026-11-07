using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>Sort-key segment packing. Values saturate to their segment; nothing is silently truncated.</summary>
    internal static class SortKeyPacking
    {
        public static ulong SaturateUnsigned(int value, int bits)
        {
            ulong max = (1ul << bits) - 1ul;
            return value <= 0 ? 0ul : math.min((ulong)value, max);
        }

        public static ulong SaturateSigned(int value, int bits)
        {
            long bias = 1L << (bits - 1);
            return (ulong)(math.clamp((long)value, -bias, bias - 1) + bias);
        }

        /// <summary>Camera distance in decimeters, saturated to the segment.</summary>
        public static ulong QuantizeDepth(float depth, int bits)
        {
            float scaled = depth * 10f;
            ulong max = (1ul << bits) - 1ul;
            if (!(scaled > 0f)) return 0ul;
            return scaled >= max ? max : (ulong)scaled;
        }

        public static ulong Pack(ulong segment, int shift) => segment << shift;

        /// <summary>
        /// Front-to-back key: distance, then the state key's rank, material and section segments, each saturated to
        /// 16 bits (the state key stores rank 16 | material 24 | section 24).
        /// </summary>
        public static ulong FrontToBack(float depth, ulong stateKey)
        {
            ulong rank = stateKey >> 48;
            ulong material = math.min((stateKey >> 24) & 0xFFFFFFul, 0xFFFFul);
            ulong section = math.min(stateKey & 0xFFFFFFul, 0xFFFFul);
            return Pack(QuantizeDepth(depth, 16), 48) | Pack(rank, 32) | Pack(material, 16) | section;
        }
    }

    /// <summary>
    /// One visible (entity, command) pair of a draw list. <c>fade</c> is 0 for the stable LOD and the signed dither factor
    /// inside a cross-fade band: +f for the outgoing level, -f for the incoming one.
    /// </summary>
    internal struct MeshDrawItem
    {
        public ulong key;
        public int command;
        public int entity;
        public float depth;
        public float fade;

        public bool fading => fade != 0;
    }

    /// <summary>
    /// A run of instances that one draw call submits: <c>instanceToEntity[instanceOffset .. +instanceCount)</c>. A fading run
    /// draws with INFINITY_LOD_DITHER and reads its per-instance factor from the parallel fade table.
    /// </summary>
    internal struct MeshInstancedDraw
    {
        public int command;
        public int instanceOffset;
        public int instanceCount;
        public bool fading;
    }

    /// <summary>
    /// Total order of draw items. Back-to-front: key (priority), exact distance descending, command, entity, fade. Other
    /// modes: key, command, stable before fading, distance ascending, entity, fade.
    /// </summary>
    internal struct MeshDrawItemComparer : IComparer<MeshDrawItem>
    {
        public bool backToFront;

        public int Compare(MeshDrawItem a, MeshDrawItem b)
        {
            int c = a.key.CompareTo(b.key);
            if (c != 0) return c;
            if (backToFront)
            {
                c = b.depth.CompareTo(a.depth);
                if (c != 0) return c;
                c = a.command.CompareTo(b.command);
            }
            else
            {
                c = a.command.CompareTo(b.command);
                if (c != 0) return c;
                c = a.fading.CompareTo(b.fading);
                if (c != 0) return c;
                c = a.depth.CompareTo(b.depth);
            }
            if (c != 0) return c;
            c = a.entity.CompareTo(b.entity);
            // The same mesh may fill two LOD levels: one entity then has an outgoing and an incoming item of one command.
            return c != 0 ? c : a.fade.CompareTo(b.fade);
        }
    }

    [BurstCompile]
    internal struct MeshDrawGatherJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<uint2> candidates;
        [ReadOnly] public NativeArray<MeshDrawCommand> commands;
        [ReadOnly] public NativeArray<MeshElement> elements;
        [ReadOnly] public NativeArray<byte> visible;
        [ReadOnly] public NativeArray<float> depth;
        [ReadOnly] public NativeArray<byte> lod;
        [ReadOnly] public NativeArray<float> fade;
        public ESortMode sortMode;
        public bool ditherLod;
        public NativeList<MeshDrawItem>.ParallelWriter items;

        public void Execute(int element)
        {
            uint2 candidate = candidates[element];
            if (candidate.y == MeshPassCache.NoCommand) return;
            int entity = (int)candidate.x;
            if (visible[entity] == 0) return;
            int command = (int)(candidate.y & MeshPassCache.CommandMask);
            if (!LodFactor((int)(candidate.y >> MeshPassCache.LodShift), lod[entity], fade[entity], ditherLod, out float itemFade)) return;
            float distance = depth[entity];
            ulong key;
            switch (sortMode)
            {
                case ESortMode.FrontToBack:
                    key = SortKeyPacking.FrontToBack(distance, commands[command].stateKey);
                    break;
                case ESortMode.BackToFront:
                    key = SortKeyPacking.Pack(SortKeyPacking.SaturateSigned(elements[element].priority, 16), 48);
                    break;
                default:
                    key = commands[command].stateKey;
                    break;
            }
            items.AddNoResize(new MeshDrawItem { key = key, command = command, entity = entity, depth = distance, fade = itemFade });
        }

        /// <summary>
        /// Whether an element of LOD level <paramref name="elementLod"/> draws, and its fade (0 = stable). Inside the band of
        /// the selected level the outgoing level draws +f and the next level -f, so the two dither patterns are complementary;
        /// at f = 0 only the incoming level draws. Without dithering only the selected level draws.
        /// </summary>
        public static bool LodFactor(int elementLod, int selected, float fade, bool dither, out float itemFade)
        {
            itemFade = 0;
            bool fading = dither && fade < 1;
            if (elementLod == selected)
            {
                if (!fading) return true;
                itemFade = fade;
                return fade > 0;
            }
            if (fading && elementLod == selected + 1)
            {
                itemFade = -math.max(fade, 1e-6f);
                return true;
            }
            return false;
        }
    }

    [BurstCompile]
    internal struct MeshDrawSortJob : IJob
    {
        public NativeList<MeshDrawItem> items;
        public MeshDrawItemComparer comparer;

        public void Execute() => items.AsArray().Sort(comparer);
    }

    [BurstCompile]
    internal struct MeshDrawMergeJob : IJob
    {
        [ReadOnly] public NativeList<MeshDrawItem> items;
        public NativeList<MeshInstancedDraw> draws;
        [WriteOnly] public NativeArray<uint> instanceToEntity;
        [WriteOnly] public NativeArray<float> instanceFade;

        public void Execute()
        {
            draws.Clear();
            for (int instance = 0; instance < items.Length; ++instance)
            {
                MeshDrawItem item = items[instance];
                instanceToEntity[instance] = (uint)item.entity;
                instanceFade[instance] = item.fade;
                int last = draws.Length - 1;
                if (last >= 0 && draws[last].command == item.command && draws[last].fading == item.fading)
                {
                    MeshInstancedDraw run = draws[last];
                    run.instanceCount++;
                    draws[last] = run;
                }
                else
                {
                    draws.Add(new MeshInstancedDraw { command = item.command, instanceOffset = instance, instanceCount = 1, fading = item.fading });
                }
            }
        }
    }

    /// <summary>
    /// The CPU product of one draw list: sorted items, merged draws and the instance-to-entity table. Scheduled when the
    /// graph compiles, completed by the draw list's upload producer, disposed when the graph is dropped.
    /// </summary>
    internal struct MeshDrawListBuild : IDisposable
    {
        public NativeList<MeshDrawItem> items;
        public NativeList<MeshInstancedDraw> draws;
        public NativeArray<uint> instanceToEntity;
        public NativeArray<float> instanceFade;
        public JobHandle job;

        public bool isCreated => items.IsCreated;
        public int instanceCount => items.Length;

        public static MeshDrawListBuild Schedule(MeshPassCache cache, MeshWorld world, in MeshViewVisibility visibility)
        {
            int capacity = math.max(1, cache.memberCount);
            var build = new MeshDrawListBuild
            {
                items = new NativeList<MeshDrawItem>(capacity, Allocator.TempJob),
                draws = new NativeList<MeshInstancedDraw>(capacity, Allocator.TempJob),
                instanceToEntity = new NativeArray<uint>(capacity, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                instanceFade = new NativeArray<float>(capacity, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            };
            NativeArray<uint2> candidates = cache.candidates;
            JobHandle handle = new MeshDrawGatherJob
            {
                candidates = candidates,
                commands = cache.commands,
                elements = world.elements,
                visible = visibility.visible,
                depth = visibility.depth,
                lod = visibility.lod,
                fade = visibility.fade,
                sortMode = cache.desc.sortMode,
                ditherLod = cache.desc.ditherLodTransitions,
                items = build.items.AsParallelWriter()
            }.Schedule(candidates.Length, 256, visibility.job);
            handle = new MeshDrawSortJob
            {
                items = build.items,
                comparer = new MeshDrawItemComparer { backToFront = cache.desc.sortMode == ESortMode.BackToFront }
            }.Schedule(handle);
            build.job = new MeshDrawMergeJob
            {
                items = build.items,
                draws = build.draws,
                instanceToEntity = build.instanceToEntity,
                instanceFade = build.instanceFade
            }.Schedule(handle);
            return build;
        }

        public void Dispose()
        {
            job.Complete();
            if (items.IsCreated) items.Dispose();
            if (draws.IsCreated) draws.Dispose();
            if (instanceToEntity.IsCreated) instanceToEntity.Dispose();
            if (instanceFade.IsCreated) instanceFade.Dispose();
            this = default;
        }
    }
}
