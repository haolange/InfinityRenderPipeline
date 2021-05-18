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
                ref FPlane FrustumPlane = ref FrustumPlanes[i];
                distRadius.x = math.dot(FrustumPlane.normalDist.xyz, MeshBatch.boundBox.center) + FrustumPlane.normalDist.w;
                distRadius.y = math.dot(math.abs(FrustumPlane.normalDist.xyz), MeshBatch.boundBox.extents);

                VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
            }

            ViewMeshBatchs[index] = math.select(0, VisibleState, MeshBatch.visible == 1);
        }
    }

    [BurstCompile]
    public struct FMeshDrawCommandBuildJob : IJob
    {
        [ReadOnly]
        public FCullingData CullingData;

        [WriteOnly]
        public NativeArray<int> Indexs;

        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchs;

        [ReadOnly]
        public FMeshPassDesctiption MeshPassDesctiption;

        public NativeList<FPassMeshBatch> PassMeshBatchs;

        public NativeList<FMeshDrawCommand> MeshDrawCommands;


        public void Execute()
        {
            FMeshBatch meshBatch;

            //Gather PassMeshBatch
            for (int i = 0; i < CullingData.viewMeshBatchs.Length; ++i)
            {
                if (CullingData.viewMeshBatchs[i] != 0)
                {
                    meshBatch = MeshBatchs[i];

                    if(meshBatch.priority >= MeshPassDesctiption.renderQueueMin && meshBatch.priority <= MeshPassDesctiption.renderQueueMax)
                    {
                        FPassMeshBatch PassMeshBatch = new FPassMeshBatch(i, FMeshBatch.MatchForDynamicInstance(ref meshBatch));
                        PassMeshBatchs.Add(PassMeshBatch);
                    }
                }
            }

            //Sort PassMeshBatch
            PassMeshBatchs.Sort();

            //Build MeshDrawCommand
            FPassMeshBatch passMeshBatch;
            FPassMeshBatch cachePassMeshBatch = new FPassMeshBatch(-1, -1);

            FMeshDrawCommand meshDrawCommand;
            FMeshDrawCommand cacheMeshDrawCommand;

            for (int j = 0; j < PassMeshBatchs.Length; ++j)
            {
                passMeshBatch = PassMeshBatchs[j];
                Indexs[j] = passMeshBatch.meshBatchId;
                meshBatch = MeshBatchs[passMeshBatch.meshBatchId];

                if (!passMeshBatch.Equals(cachePassMeshBatch))
                {
                    cachePassMeshBatch = passMeshBatch;

                    meshDrawCommand.meshIndex = meshBatch.staticMeshRef.Id;
                    meshDrawCommand.submeshIndex = meshBatch.submeshIndex;
                    meshDrawCommand.materialindex = meshBatch.materialRef.Id;
                    meshDrawCommand.countOffset.x = 0;
                    meshDrawCommand.countOffset.y = j;
                    MeshDrawCommands.Add(meshDrawCommand);
                }

                cacheMeshDrawCommand = MeshDrawCommands[MeshDrawCommands.Length - 1];
                cacheMeshDrawCommand.countOffset.x += 1;
                MeshDrawCommands[MeshDrawCommands.Length - 1] = cacheMeshDrawCommand;
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
