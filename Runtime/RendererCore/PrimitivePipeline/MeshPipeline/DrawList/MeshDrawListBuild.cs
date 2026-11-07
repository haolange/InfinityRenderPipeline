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

    /// <summary>One visible (entity, command) pair of a draw list.</summary>
    internal struct MeshDrawItem
    {
        public ulong key;
        public int command;
        public int entity;
        public float depth;
    }

    /// <summary>A run of instances that one draw call submits: <c>instanceToEntity[instanceOffset .. +instanceCount)</c>.</summary>
    internal struct MeshInstancedDraw
    {
        public int command;
        public int instanceOffset;
        public int instanceCount;
    }

    /// <summary>
    /// Total order of draw items. Back-to-front: key (priority), exact distance descending, command, entity. Other modes:
    /// key, command, distance ascending, entity.
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
                c = a.depth.CompareTo(b.depth);
            }
            return c != 0 ? c : a.entity.CompareTo(b.entity);
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
        public ESortMode sortMode;
        public NativeList<MeshDrawItem>.ParallelWriter items;

        public void Execute(int element)
        {
            uint2 candidate = candidates[element];
            if (candidate.y == MeshPassCache.NoCommand) return;
            int entity = (int)candidate.x;
            if (visible[entity] == 0) return;
            int command = (int)candidate.y;
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
            items.AddNoResize(new MeshDrawItem { key = key, command = command, entity = entity, depth = distance });
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

        public void Execute()
        {
            draws.Clear();
            for (int instance = 0; instance < items.Length; ++instance)
            {
                MeshDrawItem item = items[instance];
                instanceToEntity[instance] = (uint)item.entity;
                int last = draws.Length - 1;
                if (last >= 0 && draws[last].command == item.command)
                {
                    MeshInstancedDraw run = draws[last];
                    run.instanceCount++;
                    draws[last] = run;
                }
                else
                {
                    draws.Add(new MeshInstancedDraw { command = item.command, instanceOffset = instance, instanceCount = 1 });
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
                instanceToEntity = new NativeArray<uint>(capacity, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            };
            NativeArray<uint2> candidates = cache.candidates;
            JobHandle handle = new MeshDrawGatherJob
            {
                candidates = candidates,
                commands = cache.commands,
                elements = world.elements,
                visible = visibility.visible,
                depth = visibility.depth,
                sortMode = cache.desc.sortMode,
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
                instanceToEntity = build.instanceToEntity
            }.Schedule(handle);
            return build;
        }

        public void Dispose()
        {
            job.Complete();
            if (items.IsCreated) items.Dispose();
            if (draws.IsCreated) draws.Dispose();
            if (instanceToEntity.IsCreated) instanceToEntity.Dispose();
            this = default;
        }
    }
}
