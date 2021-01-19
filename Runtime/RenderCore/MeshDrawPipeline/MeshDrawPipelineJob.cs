using System;
using Unity.Jobs;
using Unity.Burst;
using System.Threading;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;

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
    internal struct FMarkMeshBatchCullJob : IJobParallelFor
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
    internal struct FFilterMeshBatchCullJob : IJobParallelForFilter
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

    internal struct FMeshPassDesctiption
    {
        public int RenderQueueMin;
        public int RenderQueueMax;
        public int RenderLayerMask;
        public EGatherMethod GatherMethod;
        public bool ExcludeMotionVectorObjects;

        public FMeshPassDesctiption(in RendererList InRendererList, in EGatherMethod InGatherMethod = EGatherMethod.Dots)
        {
            GatherMethod = InGatherMethod;
            RenderLayerMask = (int)InRendererList.filteringSettings.renderingLayerMask;
            RenderQueueMin = InRendererList.filteringSettings.renderQueueRange.lowerBound;
            RenderQueueMax = InRendererList.filteringSettings.renderQueueRange.upperBound;
            ExcludeMotionVectorObjects = InRendererList.filteringSettings.excludeMotionVectorObjects;
        }
    }

    [BurstCompile]
    internal struct FPassMeshBatchGatherJob : IJob
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

                    FMeshDrawCommand MeshDrawCommand = new FMeshDrawCommand(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex, FMeshBatch.MatchForDynamicInstance(ref MeshBatch));
                    FPassMeshBatch PassMeshBatch = new FPassMeshBatch(Index);
                    MeshDrawCommandMaps.Add(MeshDrawCommand, PassMeshBatch);
                }
            }
        }
    }

    [BurstCompile]
    internal struct FPassMeshBatchParallelGatherJob : IJobParallelFor
    {
        [ReadOnly]
        public FCullingData CullingData;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [WriteOnly]
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>.ParallelWriter MeshDrawCommandMaps;

        public void Execute(int Index)
        {
            if (CullingData.ViewMeshBatchs[Index] != 0)
            {
                FMeshBatch MeshBatch = MeshBatchs[Index];

                FMeshDrawCommand MeshDrawCommand = new FMeshDrawCommand(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex, FMeshBatch.MatchForDynamicInstance(ref MeshBatch));
                FPassMeshBatch PassMeshBatch = new FPassMeshBatch(Index);
                MeshDrawCommandMaps.Add(MeshDrawCommand, PassMeshBatch);
            }
        }
    }

    [BurstCompile]
    internal struct FPassMeshBatchConvertJob : IJob
    {
        [ReadOnly]
        public int Count;

        [WriteOnly]
        public NativeArray<int> IndexArray;

        [WriteOnly]
        public NativeArray<int2> CountOffsetArray;

        [ReadOnly]
        public NativeArray<FMeshDrawCommand> MeshDrawCommands;

        [ReadOnly]
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch> MeshDrawCommandsMap;


        public void Execute()
        {
            int BatchOffset = 0;

            for (int Index = 0; Index < Count; Index++)
            {
                if (MeshDrawCommandsMap.TryGetFirstValue(MeshDrawCommands[Index], out FPassMeshBatch Value, out var Iterator))
                {
                    int BatchIndex = 0;

                    do
                    {
                        IndexArray[BatchIndex + BatchOffset] = Value;
                        BatchIndex += 1;
                    }
                    while (MeshDrawCommandsMap.TryGetNextValue(out Value, ref Iterator));

                    CountOffsetArray[Index] = new int2(BatchIndex, BatchOffset);
                    Interlocked.Add(ref BatchOffset, BatchIndex);
                }
            }
        }
    }
}
