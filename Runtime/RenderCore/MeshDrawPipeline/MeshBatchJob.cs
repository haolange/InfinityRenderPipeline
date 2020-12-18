using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Runtime.Core.Geometry;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    [BurstCompile]
    internal unsafe struct ConvertHashMapToArray : IJob
    {
        [WriteOnly]
        public NativeArray<FMeshBatch> StaticMeshBatchList;

        [ReadOnly]
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public void Execute()
        {
            CacheMeshBatchStateBuckets.GetValueArray(StaticMeshBatchList);
        }
    }

    [BurstCompile]
    internal struct CopyStaticMeshBatch : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<FMeshBatch> StaticMeshBatchList;

        [WriteOnly]
        public NativeArray<FMeshBatch> DynamicMeshBatchList;

        public void Execute(int index)
        {
            DynamicMeshBatchList[index] = StaticMeshBatchList[index];
        }
    }

    [BurstCompile]
    internal struct CullMeshBatch : IJobParallelFor
    {
        [ReadOnly]
        public float3 ViewOrigin;

        [ReadOnly]
        public NativeArray<FPlane> ViewFrustum;

        [ReadOnly]
        public NativeList<FMeshBatch> MeshBatchList;

        [WriteOnly]
        public NativeArray<FVisibleMeshBatch> VisibleMeshBatchList;

        public void Execute(int index)
        {
            FMeshBatch MeshBatch = MeshBatchList[index];
            
            bool Visibility = true;
            float Distance = math.distance(ViewOrigin, MeshBatch.BoundBox.center);

            for (int i = 0; i < 6; i++)
            {
                float3 normal = ViewFrustum[i].normal;
                float distance = ViewFrustum[i].distance;

                float dist = math.dot(normal, MeshBatch.BoundBox.center) + distance;
                float radius = math.dot(math.abs(normal), MeshBatch.BoundBox.extents);

                if (dist + radius < 0) {
                    Visibility = false;
                }
            }

            VisibleMeshBatchList[index] = new FVisibleMeshBatch(index, MeshBatch.Priority, MeshBatch.Visible && Visibility, Distance);
        }
    }

    [BurstCompile]
    internal struct SortMeshBatch : IJob
    {
        [WriteOnly]
        public NativeArray<FVisibleMeshBatch> VisibleMeshBatchList;

        public void Execute()
        {
            VisibleMeshBatchList.Sort();
        }
    }
}
