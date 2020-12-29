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
    internal struct FSortMeshBatch : IJob
    {
        [WriteOnly]
        public NativeArray<FViewMeshBatch> ViewMeshBatchList;

        public void Execute()
        {
            ViewMeshBatchList.Sort();
        }
    }

    [BurstCompile]
    internal struct FCullMeshBatchJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<FPlane> ViewFrustum;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchArray;

        [NativeDisableParallelForRestriction]
        public NativeList<FViewMeshBatch> ViewMeshBatchList;

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

                if (dist + radius < 0)
                {
                    CullState = false;
                }
            }

            if (CullState)
            {
                ViewMeshBatchList.Add(new FViewMeshBatch(index));
            }
        }
    }

    [BurstCompile]
    internal unsafe struct FHashmapValueToArrayJob<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
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
    public unsafe struct FHashmapValueToArrayParallelJob<TKey, TValue> : IJobParallelFor where TKey : struct, IEquatable<TKey> where TValue : struct
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
}
