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

    [BurstCompile]
    public unsafe struct FMeshBatchCullingJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* FrustumPlanes;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FMeshBatch* MeshBatchs;

        [WriteOnly]
        public NativeArray<int> ViewMeshBatchs;


        public void Execute(int index)
        {
            int VisibleState = 1;
            float2 distRadius = new float2(0, 0);
            ref FMeshBatch MeshBatch = ref MeshBatchs[index];

            for (int i = 0; i < 6; ++i)
            {
                Unity.Burst.CompilerServices.Loop.ExpectVectorized();

                ref FPlane FrustumPlane = ref FrustumPlanes[i];
                distRadius.x = math.dot(FrustumPlane.normalDist.xyz, MeshBatch.BoundBox.center) + FrustumPlane.normalDist.w;
                distRadius.y = math.dot(math.abs(FrustumPlane.normalDist.xyz), MeshBatch.BoundBox.extents);

                VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
            }

            ViewMeshBatchs[index] = math.select(0, VisibleState, MeshBatch.Visible == 1);
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

        public NativeList<FPassMeshBatch> PassMeshBatchs;

        [WriteOnly]
        public NativeList<FMeshDrawCommand> MeshDrawCommands;


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
                        FPassMeshBatch PassMeshBatch = new FPassMeshBatch(FMeshBatch.MatchForDynamicInstance(ref MeshBatch), Index);
                        PassMeshBatchs.Add(PassMeshBatch);
                    }
                }
            }

            //Sort PassMeshBatch
            PassMeshBatchs.Sort();

            //Build MeshDrawCommand
            FPassMeshBatch CachePassMeshBatch = new FPassMeshBatch(-1, -1);
            for (int i = 0; i < PassMeshBatchs.Length; ++i)
            {
                FPassMeshBatch PassMeshBatch = PassMeshBatchs[i];
                Indexs[i] = PassMeshBatch.MeshBatchIndex;
                FMeshBatch MeshBatch = MeshBatchs[PassMeshBatch.MeshBatchIndex];

                if (!PassMeshBatch.Equals(CachePassMeshBatch))
                {
                    CachePassMeshBatch = PassMeshBatch;

                    CountOffsets.Add(new int2(0, i));
                    MeshDrawCommands.Add(new FMeshDrawCommand(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex));
                }

                int2 CountOffset = CountOffsets[CountOffsets.Length - 1];
                CountOffsets[CountOffsets.Length - 1] = CountOffset + new int2(1, 0);
            }
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
}
