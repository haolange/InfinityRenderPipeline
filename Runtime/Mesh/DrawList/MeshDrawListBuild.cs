using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

// The gather job is scheduled from a generic method, so Burst learns its closed types here.
[assembly: RegisterGenericJobType(typeof(InfinityTech.Rendering.MeshPipeline.MeshDrawGatherJob<InfinityTech.Rendering.MeshPipeline.StateSortKey>))]
[assembly: RegisterGenericJobType(typeof(InfinityTech.Rendering.MeshPipeline.MeshDrawGatherJob<InfinityTech.Rendering.MeshPipeline.TranslucentColorSortKey>))]

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

    /// <summary>Which range of its command one element fills; the gather job calls this, it is not a sort key.</summary>
    internal static class MeshDrawLod
    {
        /// <summary>
        /// Whether an element of LOD level <paramref name="elementLod"/> draws under its entity's packed view visibility, and
        /// its signed dither factor (0 = stable). Inside the band of the selected level a dithering pass draws the outgoing
        /// level +f and the next level -f, so the two patterns are complementary; at fade bits 0 only the incoming level draws.
        /// Without dithering only the selected level draws. Mirrors MeshElementRange in <c>Compute_MeshDrawPipeline.compute</c>.
        /// </summary>
        public static bool Range(uint visibility, uint elementLod, bool dither, out float factor)
        {
            factor = 0;
            if (visibility == 0u)
            {
                return false;
            }
            uint level = (visibility >> MeshLODSelection.LevelShift) & 0xFFu;
            uint fadeBits = visibility >> MeshLODSelection.FadeShift;
            bool fading = dither && fadeBits != MeshLODSelection.StableFade;
            float fade = fadeBits / MeshLODSelection.FadeScale;
            if (elementLod == level)
            {
                if (!fading)
                {
                    return true;
                }
                if (fadeBits == 0u)
                {
                    return false;
                }
                factor = fade;
                return true;
            }
            if (fading && elementLod == level + 1u)
            {
                factor = -math.max(fade, 1e-6f);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// One thread per element slot of a pass cache: an element whose entity its view sees and whose level the view's LOD
    /// selection draws in this pass becomes one instance with its pass key. The list filters; its view culled. The key
    /// formula is the type argument. The candidate table holds elements of live entities only, as the GPU build kernel also
    /// assumes.
    /// </summary>
    [BurstCompile]
    internal struct MeshDrawGatherJob<TKey> : IJobParallelFor where TKey : struct, IMeshSortKey
    {
        [ReadOnly] public NativeArray<uint2> candidates;
        [ReadOnly] public NativeArray<MeshDrawCommand> commands;
        [ReadOnly] public NativeArray<MeshElement> elements;
        [ReadOnly] public NativeArray<BoundingBox> bounds;
        [ReadOnly] public NativeArray<uint> visibility;
        // Translucent distances are measured from the view's LOD origin.
        public float3 origin;
        public bool ditherLod;
        public NativeList<MeshDrawInstance>.ParallelWriter items;

        public void Execute(int element)
        {
            uint2 candidate = candidates[element];
            if (candidate.y == MeshPassCache.NoCommand)
            {
                return;
            }
            int entity = (int)candidate.x;
            if (!MeshDrawLod.Range(visibility[entity], candidate.y >> MeshPassCache.LodShift, ditherLod, out float itemFade))
            {
                return;
            }
            int command = (int)(candidate.y & MeshPassCache.CommandMask);
            var input = new MeshSortKeyInput
            {
                stateKey = commands[command].stateKey,
                priority = elements[element].priority,
                distance = math.length(bounds[entity].center - origin),
                fade = itemFade
            };
            items.AddNoResize(new MeshDrawInstance { key = default(TKey).Eval(input), command = command, entity = entity, fade = itemFade });
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
    /// graph compiles, after its view's culling job; completed by the draw list's upload producer, disposed when the graph
    /// is dropped.
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

        /// <param name="visibility">The view's packed visibility, written by <paramref name="visibilityJob"/>.</param>
        /// <param name="origin">The view's LOD origin, from which translucent distances are measured.</param>
        public static MeshDrawListBuild Schedule(MeshPassCache cache, MeshWorld world, NativeArray<uint> visibility, float3 origin, JobHandle visibilityJob)
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
                handle = Gather<TranslucentColorSortKey>(cache, world, visibility, origin, visibilityJob, build.items);
            }
            else if (cache.desc.sortKey == EMeshSortKey.State)
            {
                handle = Gather<StateSortKey>(cache, world, visibility, origin, visibilityJob, build.items);
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

        static JobHandle Gather<TKey>(MeshPassCache cache, MeshWorld world, NativeArray<uint> visibility, float3 origin, JobHandle visibilityJob,
            NativeList<MeshDrawInstance> items) where TKey : struct, IMeshSortKey
        {
            return new MeshDrawGatherJob<TKey>
            {
                candidates = cache.candidates,
                commands = cache.commands,
                elements = world.elements,
                bounds = world.entityBounds,
                visibility = visibility,
                origin = origin,
                ditherLod = cache.desc.ditherLODTransitions,
                items = items.AsParallelWriter()
            }.Schedule(cache.candidates.Length, 256, visibilityJob);
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
