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

    /// <summary>
    /// Entity tests of a culling context shared by the CPU gather job; <c>Compute_MeshDrawPipeline.compute</c> evaluates the
    /// same expressions (MeshEligible, MeshFrustumCulled).
    /// </summary>
    internal static class MeshCulling
    {
        public static bool Eligible(in MeshEntityDesc desc, in MeshCullingContext context)
        {
            return (desc.flags & EMeshEntityFlags.Visible) != 0
                && (unchecked((uint)desc.layerMask) & context.layerMask) != 0
                && (desc.sceneCullingMask & context.sceneCullingMask) != 0
                && (context.filterRenderingLayers == 0 || (desc.renderingLayerMask & context.renderingLayerMask) != 0);
        }

        public static bool OutsidePlanes(in MeshCullingContext context, in BoundingBox bound)
        {
            for (int i = 0; i < (int)context.planeCount; ++i)
            {
                float4 plane = context.GetPlane(i);
                float radius = math.dot(math.abs(plane.xyz), bound.extents);
                if (math.dot(plane.xyz, bound.center) + plane.w + radius < 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// One thread per element slot of a pass cache: an element whose entity passes the culling context and whose level the
    /// LOD selection draws becomes one instance with its pass key. The key formula is the type argument.
    /// </summary>
    [BurstCompile]
    internal struct MeshDrawGatherJob<TKey> : IJobParallelFor where TKey : struct, IMeshSortKey
    {
        [ReadOnly] public NativeArray<uint2> candidates;
        [ReadOnly] public NativeArray<MeshDrawCommand> commands;
        [ReadOnly] public NativeArray<MeshElement> elements;
        [ReadOnly] public NativeArray<MeshEntity> entities;
        [ReadOnly] public NativeArray<uint> generations;
        [ReadOnly] public NativeArray<BoundingBox> bounds;
        public MeshCullingContext context;
        public MeshLODProjection lod;
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
            if ((generations[entity] & 1u) == 0 || !MeshCulling.Eligible(entities[entity].desc, context))
            {
                return;
            }
            BoundingBox bound = bounds[entity];
            MeshLODSelection.Select(entities[entity].lod, bound, lod, out byte level, out float levelFade);
            if (level == MeshLODSelection.None
                || !MeshDrawLod.Factor((int)(candidate.y >> MeshPassCache.LodShift), level, levelFade, ditherLod, out float itemFade)
                || MeshCulling.OutsidePlanes(context, bound))
            {
                return;
            }
            int command = (int)(candidate.y & MeshPassCache.CommandMask);
            var input = new MeshSortKeyInput
            {
                stateKey = commands[command].stateKey,
                priority = elements[element].priority,
                distance = math.length(bound.center - lod.origin),
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

        public static MeshDrawListBuild Schedule(MeshPassCache cache, MeshWorld world, in MeshCullingContext context)
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
                handle = Gather<TranslucentColorSortKey>(cache, world, context, build.items);
            }
            else if (cache.desc.sortKey == EMeshSortKey.State)
            {
                handle = Gather<StateSortKey>(cache, world, context, build.items);
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

        static JobHandle Gather<TKey>(MeshPassCache cache, MeshWorld world, in MeshCullingContext context, NativeList<MeshDrawInstance> items)
            where TKey : struct, IMeshSortKey
        {
            return new MeshDrawGatherJob<TKey>
            {
                candidates = cache.candidates,
                commands = cache.commands,
                elements = world.elements,
                entities = world.entities,
                generations = world.entityGenerations,
                bounds = world.entityBounds,
                context = context,
                lod = context.ReadLODProjection(),
                ditherLod = cache.desc.ditherLODTransitions,
                items = items.AsParallelWriter()
            }.Schedule(cache.candidates.Length, 256);
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
