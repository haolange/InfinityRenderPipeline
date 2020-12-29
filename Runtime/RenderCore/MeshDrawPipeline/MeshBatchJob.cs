using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using InfinityTech.Runtime.Core.Geometry;
using System;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    [BurstCompile]
    internal unsafe struct HashmapValueToArray<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TValue> Array;

        [ReadOnly]
        public NativeHashMap<TKey, TValue> Hashmap;

        public void Execute()
        {
            Hashmap.GetValueArray(Array);
        }
    }

    [BurstCompile]
    public unsafe struct HashmapValueToArrayParallel<TKey, TValue> : IJobParallelFor where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TValue> Array;

        [ReadOnly]
        public NativeHashMap<TKey, TValue> Hashmap;

        public void Execute(int index)
        {
            Array[index] = UnsafeUtility.ReadArrayElement<TValue>(Hashmap.m_HashMapData.m_Buffer->values, index);
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
        public NativeArray<FPlane> ViewFrustum;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchArray;

        [WriteOnly]
        public NativeArray<FViewMeshBatch> ViewMeshBatchList;

        public void Execute(int index)
        {
            bool CullState = true;
            FMeshBatch MeshBatch = MeshBatchArray[index];

            for (int i = 0; i < 6; i++)
            {
                float3 normal = ViewFrustum[i].normal;
                float distance = ViewFrustum[i].distance;

                float dist = math.dot(normal, MeshBatch.BoundBox.center) + distance;
                float radius = math.dot(math.abs(normal), MeshBatch.BoundBox.extents);

                if (dist + radius < 0) {
                    CullState = false;
                }
            }

            if (CullState) {

            }

            ViewMeshBatchList[index] = new FViewMeshBatch(index);
        }
    }

    [BurstCompile]
    internal struct SortMeshBatch : IJob
    {
        [WriteOnly]
        public NativeArray<FViewMeshBatch> ViewMeshBatchList;

        public void Execute()
        {
            ViewMeshBatchList.Sort();
        }
    }
}
