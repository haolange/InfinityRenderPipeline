using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace InfinityTech.Rendering.MeshPipeline
{
    [BurstCompile]
    public struct FListSortJob<T> : IJob where T : struct, IEquatable<T>, IComparable<T>
    {
        public NativeList<T> SortTarget;

        public void Execute()
        {
            SortTarget.Sort();
        }
    }

    [BurstCompile]
    public struct FArraySortJob<T> : IJob where T : struct, IEquatable<T>, IComparable<T>
    {
        public NativeArray<T> SortTarget;

        public void Execute()
        {
            SortTarget.Sort();
        }
    }

    [BurstCompile]
    public struct FSortMeshBatchJob : IJob
    {
        [WriteOnly]
        public NativeArray<FViewMeshBatch> ViewMeshBatchList;

        public void Execute()
        {
            ViewMeshBatchList.Sort();
        }
    }

    [BurstCompile]
    public unsafe struct FMeshBatchCounterJob : IJob
    {
        [ReadOnly]
        public int Count;

        [ReadOnly]
        public int Length;

        [NativeDisableUnsafePtrRestriction]
        public int* BucketNext;

        [NativeDisableUnsafePtrRestriction]
        public int* BucketArray;

        [NativeDisableParallelForRestriction]
        public NativeList<int> MeshBatchMapIndexs;

        public void Execute()
        {
            int count = 0;

            for (int index = 0; index <= Length; ++index)
            {
                if (count < Count)
                {
                    int bucket = BucketArray[index];

                    while (bucket != -1)
                    {
                        MeshBatchMapIndexs.Add(count);
                        bucket = BucketNext[bucket];
                        count++;
                    }
                }
            }
        }
    }

    [BurstCompile]
    public unsafe struct FMeshBatchGatherJob: IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction]
        public byte* HashmapValues;

        [ReadOnly]
        public NativeArray<int> MeshBatchMapIndexs;

        [WriteOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        public void Execute(int index)
        {
            int Offset = MeshBatchMapIndexs[index];
            MeshBatchs[index] = UnsafeUtility.ReadArrayElement<FMeshBatch>(HashmapValues, Offset);
        }
    }

    /*[BurstCompile]
    public unsafe struct FMeshBatchGatherJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction]
        public int* Count;

        [NativeDisableUnsafePtrRestriction]
        public int* BucketNext;

        [NativeDisableUnsafePtrRestriction]
        public int* BucketArray;

        [NativeDisableUnsafePtrRestriction]
        public byte* HashmapValues;

        [WriteOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        public void Execute(int index)
        {
            ref int count = ref Count[0];

            if (count < MeshBatchs.Length)
            {
                int bucket = BucketArray[index];

                while (bucket != -1)
                {
                    MeshBatchs[count] = UnsafeUtility.ReadArrayElement<FMeshBatch>(HashmapValues, bucket);
                    bucket = BucketNext[bucket];

                    Interlocked.Increment(ref count);
                }
            }
        }
    }*/

    [BurstCompile]
    public struct FMeshBatchCullingJob : IJobParallelFor
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

            if (MeshBatch.Visible == 1)
            {
                for (int i = 0; i < 6; ++i)
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
    public struct FFilterMeshBatchCullJob : IJobParallelForFilter
    {
        [ReadOnly]
        public NativeArray<FPlane> ViewFrustum;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        public bool Execute(int index)
        {
            FMeshBatch MeshBatch = MeshBatchs[index];

            for (int i = 0; i < 6; ++i)
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
    public struct FPassMeshBatchGatherJob : IJob
    {
        [ReadOnly]
        public FCullingData CullingData;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [WriteOnly]
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch> MeshDrawCommandsMap;

        public void Execute()
        {
            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; ++Index)
            {
                if (CullingData.ViewMeshBatchs[Index] != 0)
                {
                    FMeshBatch MeshBatch = MeshBatchs[Index];

                    FMeshDrawCommand MeshDrawCommand = new FMeshDrawCommand(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex, FMeshBatch.MatchForDynamicInstance(ref MeshBatch));
                    FPassMeshBatch PassMeshBatch = new FPassMeshBatch(Index);
                    MeshDrawCommandsMap.Add(MeshDrawCommand, PassMeshBatch);
                }
            }
        }
    }

    [BurstCompile]
    public struct FPassMeshBatchGatherJobV2 : IJob
    {
        [ReadOnly]
        public FCullingData CullingData;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [WriteOnly]
        public NativeList<FPassMeshBatchV2> PassMeshBatchs;

        public void Execute()
        {
            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; ++Index)
            {
                if (CullingData.ViewMeshBatchs[Index] != 0)
                {
                    FMeshBatch MeshBatch = MeshBatchs[Index];
                    FPassMeshBatchV2 PassMeshBatch = new FPassMeshBatchV2(FMeshBatch.MatchForDynamicInstance(ref MeshBatch), Index);
                    PassMeshBatchs.Add(PassMeshBatch);
                }
            }
        }
    }

    [BurstCompile]
    public struct FBuildMeshDrawCommandJob : IJob
    {
        [WriteOnly]
        public NativeArray<int> Indexs;

        [ReadOnly]
        public FCullingData CullingData;

        public NativeList<int2> CountOffsets;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [ReadOnly]
        public FMeshPassDesctiption MeshPassDesctiption;

        public NativeList<FPassMeshBatchV2> PassMeshBatchs;

        [WriteOnly]
        public NativeList<FMeshDrawCommandV2> MeshDrawCommands;

        public void Execute()
        {
            //Gather PassMeshBatch
            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; ++Index)
            {
                if (CullingData.ViewMeshBatchs[Index] != 0)
                {
                    FMeshBatch MeshBatch = MeshBatchs[Index];

                    if(MeshBatch.Priority >= MeshPassDesctiption.RenderQueueMin && MeshBatch.Priority <= MeshPassDesctiption.RenderQueueMax)
                    {
                        FPassMeshBatchV2 PassMeshBatch = new FPassMeshBatchV2(FMeshBatch.MatchForDynamicInstance(ref MeshBatch), Index);
                        PassMeshBatchs.Add(PassMeshBatch);
                    }
                }
            }

            //Sort PassMeshBatch
            PassMeshBatchs.Sort();

            //Build MeshDrawCommand
            FPassMeshBatchV2 CachePassMeshBatch = new FPassMeshBatchV2(-1, -1);
            for (int i = 0; i < PassMeshBatchs.Length; ++i)
            {
                FPassMeshBatchV2 PassMeshBatch = PassMeshBatchs[i];
                Indexs[i] = PassMeshBatch.MeshBatchIndex;
                FMeshBatch MeshBatch = MeshBatchs[PassMeshBatch.MeshBatchIndex];

                if (!PassMeshBatch.Equals(CachePassMeshBatch))
                {
                    CachePassMeshBatch = PassMeshBatch;

                    CountOffsets.Add(new int2(0, i));
                    MeshDrawCommands.Add(new FMeshDrawCommandV2(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex));
                }

                int2 CountOffset = CountOffsets[CountOffsets.Length - 1];
                CountOffsets[CountOffsets.Length - 1] = CountOffset + new int2(1, 0);
            }
        }
    }

    [BurstCompile]
    public struct FMeshDrawCommandBuildJob : IJob
    {
        [WriteOnly]
        public NativeArray<int> Indexs;

        //[WriteOnly]
        public NativeList<int2> CountOffsets;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [ReadOnly]
        public NativeList<FPassMeshBatchV2> PassMeshBatchs;

        [WriteOnly]
        public NativeList<FMeshDrawCommandV2> MeshDrawCommands;

        public void Execute()
        {
            FPassMeshBatchV2 CachePassMeshBatch = new FPassMeshBatchV2(-1, -1);

            for (int i = 0; i < PassMeshBatchs.Length; ++i)
            {
                FPassMeshBatchV2 PassMeshBatch = PassMeshBatchs[i];
                Indexs[i] = PassMeshBatch.MeshBatchIndex;
                FMeshBatch MeshBatch = MeshBatchs[PassMeshBatch.MeshBatchIndex];

                if (!PassMeshBatch.Equals(CachePassMeshBatch))
                {
                    CachePassMeshBatch = PassMeshBatch;

                    CountOffsets.Add(new int2(0, i));
                    MeshDrawCommands.Add(new FMeshDrawCommandV2(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex));
                }

                int2 CountOffset = CountOffsets[CountOffsets.Length - 1];
                CountOffsets[CountOffsets.Length - 1] = CountOffset + new int2(1, 0);
            }
        }
    }

    [BurstCompile]
    public struct FPassMeshBatchConvertJob : IJob
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

            for (int Index = 0; Index < Count; ++Index)
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
                    //Interlocked.Add(ref BatchOffset, BatchIndex);
                    BatchOffset += BatchIndex;
                }
            }
        }
    }

    [BurstCompile]
    public struct FArrayUniqueCounterJob<T> : IJob where T : struct, IEquatable<T>
    {
        [WriteOnly]
        public NativeArray<int> Counter;

        public NativeArray<T> CounteTarget;

        public void Execute()
        {
            Counter[0] = CounteTarget.Unique();
        }
    }

    [BurstCompile]
    public struct FPassMeshBatchParallelGatherJob : IJobParallelFor
    {
        [ReadOnly]
        public FCullingData CullingData;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [WriteOnly]
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>.ParallelWriter MeshDrawCommandsMap;

        public void Execute(int Index)
        {
            if (CullingData.ViewMeshBatchs[Index] != 0)
            {
                FMeshBatch MeshBatch = MeshBatchs[Index];

                FMeshDrawCommand MeshDrawCommand = new FMeshDrawCommand(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex, FMeshBatch.MatchForDynamicInstance(ref MeshBatch));
                FPassMeshBatch PassMeshBatch = new FPassMeshBatch(Index);
                MeshDrawCommandsMap.Add(MeshDrawCommand, PassMeshBatch);
            }
        }
    }

    [BurstCompile]
    public struct FHashmapGatherKeyJob<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TKey> Array;

        [ReadOnly]
        public NativeHashMap<TKey, TValue> Hashmap;

        public void Execute()
        {
            Hashmap.GetKeyArray(Array);
        }
    }

    [BurstCompile]
    public struct FHashmapGatherValueJob<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
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
    public struct FMultiHashmapGatherKeyJob<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TKey> Array;

        [ReadOnly]
        public NativeMultiHashMap<TKey, TValue> MultiHashmap;

        public void Execute()
        {
            MultiHashmap.GetKeyArray(Array);
        }
    }

    [BurstCompile]
    public struct FMultiHashmapGatherValueJob<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TValue> Array;

        [ReadOnly]
        public NativeMultiHashMap<TKey, TValue> MultiHashmap;

        public void Execute()
        {
            MultiHashmap.GetValueArray(Array);
        }
    }

    [BurstCompile]
    public unsafe struct FHashmapParallelGatherKeyJob<TKey, TValue> : IJobParallelFor where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TKey> Array;

        [ReadOnly]
        public NativeHashMap<TKey, TValue> Hashmap;

        public void Execute(int index)
        {
            Array[index] = UnsafeUtility.ReadArrayElement<TKey>(Hashmap.m_HashMapData.m_Buffer->keys, index);
        }
    }

    [BurstCompile]
    public unsafe struct FHashmapParallelGatherValueJob<TKey, TValue> : IJobParallelFor where TKey : struct, IEquatable<TKey> where TValue : struct
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
    public unsafe struct FMultiHashmapParallelGatherKeyJob<TKey, TValue> : IJobParallelFor where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TKey> Array;

        [ReadOnly]
        public NativeMultiHashMap<TKey, TValue> MultiHashmap;

        public void Execute(int index)
        {
            Array[index] = UnsafeUtility.ReadArrayElement<TKey>(MultiHashmap.m_MultiHashMapData.m_Buffer->keys, index);
        }
    }

    [BurstCompile]
    public unsafe struct FMultiHashmapParallelGatherValueJob<TKey, TValue> : IJobParallelFor where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TValue> Array;

        [ReadOnly]
        public NativeMultiHashMap<TKey, TValue> MultiHashmap;

        public void Execute(int index)
        {
            Array[index] = UnsafeUtility.ReadArrayElement<TValue>(MultiHashmap.m_MultiHashMapData.m_Buffer->values, index);
        }
    }
}
