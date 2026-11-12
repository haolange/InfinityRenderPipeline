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

        public static ulong Pack(ulong segment, int shift) => segment << shift;
    }

    /// <summary>
    /// One visible (entity, command) pair of a draw list. <c>fade</c> is 0 for the stable LOD and the signed dither factor
    /// inside a cross-fade band: +f for the outgoing level, -f for the incoming one.
    /// </summary>
    internal struct MeshDrawInstance
    {
        public ulong key;
        public int command;
        public int entity;
        public float fade;

        public bool fading => fade != 0;
    }

    /// <summary>
    /// A run of instances that one draw call submits: <c>instanceToEntity[instanceOffset .. +instanceCount)</c>. A fading run
    /// draws with INFINITY_LOD_DITHER and reads its per-instance factor from the parallel fade table.
    /// </summary>
    internal struct MeshDrawCall
    {
        public int command;
        public int instanceOffset;
        public int instanceCount;
        public bool fading;
    }

    /// <summary>Total order of draw items: key, command, entity, then fade.</summary>
    internal struct MeshDrawInstanceComparer : IComparer<MeshDrawInstance>
    {
        public int Compare(MeshDrawInstance a, MeshDrawInstance b)
        {
            int c = a.key.CompareTo(b.key);
            if (c != 0)
            {
                return c;
            }
            c = a.command.CompareTo(b.command);
            if (c != 0)
            {
                return c;
            }
            c = a.entity.CompareTo(b.entity);
            // The same mesh may fill two LOD levels: one entity then has an outgoing and an incoming item of one command.
            return c != 0 ? c : a.fade.CompareTo(b.fade);
        }
    }

    /// <summary>LOD membership of one element. The gather job calls this; it is not a sort key.</summary>
    internal static class MeshDrawLod
    {
        /// <summary>
        /// Whether an element of LOD level <paramref name="elementLod"/> draws, and its fade (0 = stable). Inside the band of
        /// the selected level the outgoing level draws +f and the next level -f, so the two dither patterns are complementary;
        /// at f = 0 only the incoming level draws. Without dithering only the selected level draws.
        /// </summary>
        public static bool Factor(int elementLod, int selected, float fade, bool dither, out float itemFade)
        {
            itemFade = 0;
            bool fading = dither && fade < 1;
            if (elementLod == selected)
            {
                if (!fading)
                {
                    return true;
                }
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

    internal struct MeshDrawGatherSource
    {
        [ReadOnly] public NativeArray<uint2> candidates;
        [ReadOnly] public NativeArray<MeshDrawCommand> commands;
        [ReadOnly] public NativeArray<MeshElement> elements;
        [ReadOnly] public NativeArray<byte> visible;
        [ReadOnly] public NativeArray<float> depth;
        [ReadOnly] public NativeArray<byte> lod;
        [ReadOnly] public NativeArray<float> fade;
        public bool ditherLod;
        public NativeList<MeshDrawInstance>.ParallelWriter items;
    }

    internal static class MeshDrawGather
    {
        public static bool TryRead(int element, in MeshDrawGatherSource source, out MeshSortKeyInput input, out int command, out int entity, out float itemFade)
        {
            input = default;
            command = 0;
            entity = 0;
            itemFade = 0;
            uint2 candidate = source.candidates[element];
            if (candidate.y == MeshPassCache.NoCommand || source.visible[(int)candidate.x] == 0)
            {
                return false;
            }
            entity = (int)candidate.x;
            command = (int)(candidate.y & MeshPassCache.CommandMask);
            if (!MeshDrawLod.Factor((int)(candidate.y >> MeshPassCache.LodShift), source.lod[entity], source.fade[entity], source.ditherLod, out itemFade))
            {
                return false;
            }
            input = new MeshSortKeyInput
            {
                stateKey = source.commands[command].stateKey,
                priority = source.elements[element].priority,
                distance = source.depth[entity],
                fade = itemFade
            };
            return true;
        }
    }

    [BurstCompile]
    internal struct MeshDrawGatherStateJob : IJobParallelFor
    {
        public MeshDrawGatherSource source;

        public void Execute(int element)
        {
            if (!MeshDrawGather.TryRead(element, source, out MeshSortKeyInput input, out int command, out int entity, out float itemFade))
            {
                return;
            }
            ulong key = default(StateSortKey).Eval(input);
            source.items.AddNoResize(new MeshDrawInstance { key = key, command = command, entity = entity, fade = itemFade });
        }
    }

    [BurstCompile]
    internal struct MeshDrawGatherTranslucentColorJob : IJobParallelFor
    {
        public MeshDrawGatherSource source;

        public void Execute(int element)
        {
            if (!MeshDrawGather.TryRead(element, source, out MeshSortKeyInput input, out int command, out int entity, out float itemFade))
            {
                return;
            }
            ulong key = default(TranslucentColorSortKey).Eval(input);
            source.items.AddNoResize(new MeshDrawInstance { key = key, command = command, entity = entity, fade = itemFade });
        }
    }

    [BurstCompile]
    internal struct MeshDrawSortJob : IJob
    {
        public NativeList<MeshDrawInstance> items;
        public MeshDrawInstanceComparer comparer;

        public void Execute() => items.AsArray().Sort(comparer);
    }

    [BurstCompile]
    internal struct MeshDrawMergeJob : IJob
    {
        [ReadOnly] public NativeList<MeshDrawInstance> items;
        public NativeList<MeshDrawCall> draws;
        [WriteOnly] public NativeArray<uint> instanceToEntity;
        [WriteOnly] public NativeArray<float> instanceFade;

        public void Execute()
        {
            draws.Clear();
            for (int instance = 0; instance < items.Length; ++instance)
            {
                MeshDrawInstance item = items[instance];
                instanceToEntity[instance] = (uint)item.entity;
                instanceFade[instance] = item.fade;
                int last = draws.Length - 1;
                if (last >= 0 && draws[last].command == item.command && draws[last].fading == item.fading)
                {
                    MeshDrawCall run = draws[last];
                    run.instanceCount++;
                    draws[last] = run;
                }
                else
                {
                    draws.Add(new MeshDrawCall { command = item.command, instanceOffset = instance, instanceCount = 1, fading = item.fading });
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
        public NativeList<MeshDrawInstance> items;
        public NativeList<MeshDrawCall> draws;
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
                items = new NativeList<MeshDrawInstance>(capacity, Allocator.TempJob),
                draws = new NativeList<MeshDrawCall>(capacity, Allocator.TempJob),
                instanceToEntity = new NativeArray<uint>(capacity, Allocator.TempJob, NativeArrayOptions.UninitializedMemory),
                instanceFade = new NativeArray<float>(capacity, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            };
            JobHandle handle;
            if (cache.desc.sortKey == EMeshSortKey.TranslucentColor)
            {
                handle = ScheduleTranslucentColor(cache, world, visibility, build.items);
            }
            else if (cache.desc.sortKey == EMeshSortKey.State)
            {
                handle = ScheduleState(cache, world, visibility, build.items);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(cache.desc.sortKey));
            }
            handle = new MeshDrawSortJob
            {
                items = build.items,
                comparer = default
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

        static MeshDrawGatherSource Source(MeshPassCache cache, MeshWorld world, in MeshViewVisibility visibility, NativeList<MeshDrawInstance> items)
        {
            return new MeshDrawGatherSource
            {
                candidates = cache.candidates,
                commands = cache.commands,
                elements = world.elements,
                visible = visibility.visible,
                depth = visibility.depth,
                lod = visibility.lod,
                fade = visibility.fade,
                ditherLod = cache.desc.ditherLODTransitions,
                items = items.AsParallelWriter()
            };
        }

        static JobHandle ScheduleState(MeshPassCache cache, MeshWorld world, in MeshViewVisibility visibility, NativeList<MeshDrawInstance> items)
        {
            return new MeshDrawGatherStateJob { source = Source(cache, world, visibility, items) }
                .Schedule(cache.candidates.Length, 256, visibility.job);
        }

        static JobHandle ScheduleTranslucentColor(MeshPassCache cache, MeshWorld world, in MeshViewVisibility visibility, NativeList<MeshDrawInstance> items)
        {
            return new MeshDrawGatherTranslucentColorJob { source = Source(cache, world, visibility, items) }
                .Schedule(cache.candidates.Length, 256, visibility.job);
        }

        public void Dispose()
        {
            if (isCreated)
            {
                using (MeshDrawProfiling.cpuCleanup.Auto())
                {
                    job.Complete();
                }
            }
            else
            {
                job.Complete();
            }
            if (items.IsCreated)
            {
                items.Dispose();
            }
            if (draws.IsCreated)
            {
                draws.Dispose();
            }
            if (instanceToEntity.IsCreated)
            {
                instanceToEntity.Dispose();
            }
            if (instanceFade.IsCreated)
            {
                instanceFade.Dispose();
            }
            this = default;
        }
    }
}
