using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// One thread per entity slot: the CPU visibility of one view, the packed word a <c>CullMeshView</c> kernel writes on the
    /// GPU. Every CPU list of the view reads it; no occluder is tested.
    /// </summary>
    [BurstCompile]
    internal struct MeshViewVisibilityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<MeshEntity> entities;
        [ReadOnly] public NativeArray<BoundingBox> bounds;
        public MeshCullingContext context;
        public MeshLODProjection projection;
        [WriteOnly] public NativeArray<uint> visibility;

        public void Execute(int entity)
        {
            visibility[entity] = MeshCulling.Evaluate(entities[entity], bounds[entity], context, projection);
        }

        /// <summary>Evaluates every slot of <paramref name="visibility"/>, which covers the entity slots its lists read.</summary>
        public static JobHandle Schedule(MeshWorld world, in MeshCullingContext context, NativeArray<uint> visibility)
        {
            var job = new MeshViewVisibilityJob
            {
                entities = world.entities,
                bounds = world.entityBounds,
                context = context,
                projection = context.ReadLODProjection(),
                visibility = visibility
            };
            return IJobParallelForExtensions.Schedule(job, visibility.Length, 64);
        }
    }
}
