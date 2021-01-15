using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshDrawPipeline
{
    [BurstCompile]
    internal struct FSortMeshBatchJob : IJob
    {
        [WriteOnly]
        public NativeArray<FViewMeshBatch> ViewMeshBatchList;

        public void Execute()
        {
            ViewMeshBatchList.Sort();
        }
    }

    [BurstCompile]
    internal struct FCullMeshBatchForMarkJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [ReadOnly]
        public NativeArray<FPlane> ViewFrustum;

        [WriteOnly]
        public NativeArray<int> ViewMeshBatchs;

        public void Execute(int index)
        {
            int VisibleState = 1;
            FMeshBatch MeshBatch = MeshBatchs[index];

            if (MeshBatch.Visible)
            {
                for (int i = 0; i < 6; i++)
                {
                    float3 normal = ViewFrustum[i].normal;
                    float distance = ViewFrustum[i].distance;

                    float dist = math.dot(normal, MeshBatch.BoundBox.center) + distance;
                    float radius = math.dot(math.abs(normal), MeshBatch.BoundBox.extents);

                    if (dist + radius < 0) { VisibleState = 0; }
                }
            } else {
                VisibleState = 0;
            }

            ViewMeshBatchs[index] = VisibleState;
        }
    }

    [BurstCompile]
    internal struct FCullMeshBatchForFilterJob : IJobParallelForFilter
    {
        [ReadOnly]
        public NativeArray<FPlane> ViewFrustum;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        public bool Execute(int index)
        {
            FMeshBatch MeshBatch = MeshBatchs[index];

            for (int i = 0; i < 6; i++)
            {
                float3 normal = ViewFrustum[i].normal;
                float distance = ViewFrustum[i].distance;

                float dist = math.dot(normal, MeshBatch.BoundBox.center) + distance;
                float radius = math.dot(math.abs(normal), MeshBatch.BoundBox.extents);

                if (dist + radius < 0) {
                    return false;
                }
            }

            return true;
        }
    }

    internal struct FMeshPassDesctiption
    {
        public int RenderQueueMin;
        public int RenderQueueMax;
        public int RenderLayerMask;
        public bool ExcludeMotionVectorObjects;
    }

    [BurstCompile]
    internal struct FMeshPassFilterJob : IJobParallelForFilter
    {
        [ReadOnly]
        FMeshPassDesctiption MeshPassDesctiption;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchArray;

        [ReadOnly]
        public NativeList<int> ViewMeshBatchList;

        public bool Execute(int index)
        {
            FViewMeshBatch ViewMeshBatch = ViewMeshBatchList[index];
            FMeshBatch MeshBatch = MeshBatchArray[ViewMeshBatch.Flag];

            if ((MeshBatch.MotionType == 1 ? true : false) == MeshPassDesctiption.ExcludeMotionVectorObjects && 
                 MeshBatch.RenderLayer == MeshPassDesctiption.RenderLayerMask && 
                 MeshBatch.Priority >= MeshPassDesctiption.RenderQueueMin && 
                 MeshBatch.Priority <= MeshPassDesctiption.RenderQueueMax)
            {
                return true;
            }

            return false;
        }
    }

    [BurstCompile]
    public unsafe struct FHashmapValueToArrayJob<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
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

    [BurstCompile]
    internal struct FViewMeshBatchGatherJob : IJob
    {
        [ReadOnly]
        public FCullingData CullingData;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [WriteOnly]
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch> MeshDrawCommandMaps;

        public void Execute()
        {
            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; Index++)
            {
                if (CullingData.ViewMeshBatchs[Index] != 0)
                {
                    FMeshBatch MeshBatch = MeshBatchs[Index];

                    FMeshDrawCommand MeshDrawCommand = new FMeshDrawCommand(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex, MeshBatch.MatchForDynamicInstance());
                    FPassMeshBatch PassMeshBatch = new FPassMeshBatch(Index);
                    MeshDrawCommandMaps.Add(MeshDrawCommand, PassMeshBatch);
                }
            }
        }
    }
}
