using Unity.Jobs;
using UnityEngine;
using System.Threading;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Native;
using InfinityTech.Rendering.RDG;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FMeshPassProcessor
    {
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch> MeshDrawCommandsMap;

        public FMeshPassProcessor()
        {

        }

        internal void DispatchDraw(RDGContext GraphContext, FGPUScene GPUScene, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (GPUScene.MeshBatchs.IsCreated == false || CullingData.ViewMeshBatchs.IsCreated == false) { return; }

            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            switch (MeshPassDesctiption.GatherMethod)
            {
                case EGatherMethod.Dots:
                        DispatchDrawDots(GraphContext, GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);
                    break;
                        
                case EGatherMethod.Default:
                        DispatchDrawDefault(GraphContext, GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);
                    break;
            }
        }

        private void DispatchDrawDots(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            MeshDrawCommandsMap = new NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>(10000, Allocator.TempJob);

            //Gather PassMeshBatch
            FPassMeshBatchGatherJob PassMeshBatchGatherJob = new FPassMeshBatchGatherJob();
            {
                PassMeshBatchGatherJob.MeshBatchs = MeshBatchs;
                PassMeshBatchGatherJob.CullingData = CullingData;
                PassMeshBatchGatherJob.MeshDrawCommandsMap = MeshDrawCommandsMap;
            }
            PassMeshBatchGatherJob.Run();

            /*FPassMeshBatchParallelGatherJob PassMeshBatchParallelGatherJob = new FPassMeshBatchParallelGatherJob();
            {
                PassMeshBatchParallelGatherJob.MeshBatchs = MeshBatchs;
                PassMeshBatchParallelGatherJob.CullingData = CullingData;
                PassMeshBatchParallelGatherJob.MeshDrawCommandsMap = MeshDrawCommandsMap.AsParallelWriter();
            }
            PassMeshBatchParallelGatherJob.Schedule(CullingData.ViewMeshBatchs.Length, 256).Complete();*/

            //Gather MeshDrawCommandKey
            NativeArray<FMeshDrawCommand> MeshDrawCommandsKey = new NativeArray<FMeshDrawCommand>(MeshDrawCommandsMap.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            FMultiHashmapParallelGatherKeyJob<FMeshDrawCommand, FPassMeshBatch> MultiHashmapParallelGatherKeyJob = new FMultiHashmapParallelGatherKeyJob<FMeshDrawCommand, FPassMeshBatch>();
            {
                MultiHashmapParallelGatherKeyJob.Array = MeshDrawCommandsKey;
                MultiHashmapParallelGatherKeyJob.MultiHashmap = MeshDrawCommandsMap;
            }
            JobHandle ConvertHandle = MultiHashmapParallelGatherKeyJob.Schedule(MeshDrawCommandsKey.Length, 256);
            //if (MeshDrawCommandsKey.Length == 0) { return; }

            //Sort MeshDrawCommandKey
            FArraySortJob<FMeshDrawCommand> ArraySortJob = new FArraySortJob<FMeshDrawCommand>();
            {
                ArraySortJob.SortTarget = MeshDrawCommandsKey;
            }
            JobHandle SortHandle = ArraySortJob.Schedule(ConvertHandle);
            //JobHandle SortHandle = FSortFactory.ParallelSort(MeshDrawCommandsKey, ConvertHandle);

            //Counter MeshDrawCommandKey
            NativeArray<int> ElementCounter = new NativeArray<int>(1, Allocator.TempJob);
            FArrayUniqueCounterJob<FMeshDrawCommand> ArrayUniqueCounterJob = new FArrayUniqueCounterJob<FMeshDrawCommand>();
            {
                ArrayUniqueCounterJob.Counter = ElementCounter;
                ArrayUniqueCounterJob.CounteTarget = MeshDrawCommandsKey;
            }
            ArrayUniqueCounterJob.Schedule(SortHandle).Complete();

            //Gather MeshPassBuffer
            NativeArray<int> IndexArray = new NativeArray<int>(MeshDrawCommandsMap.Count(), Allocator.TempJob);
            NativeArray<int2> CountOffsetArray = new NativeArray<int2>(ElementCounter[0], Allocator.TempJob);
            FPassMeshBatchConvertJob PassMeshBatchConvertJob = new FPassMeshBatchConvertJob();
            {
                PassMeshBatchConvertJob.Count = ElementCounter[0];
                PassMeshBatchConvertJob.IndexArray = IndexArray;
                PassMeshBatchConvertJob.CountOffsetArray = CountOffsetArray;
                PassMeshBatchConvertJob.MeshDrawCommands = MeshDrawCommandsKey;
                PassMeshBatchConvertJob.MeshDrawCommandsMap = MeshDrawCommandsMap;
            }
            PassMeshBatchConvertJob.Run();

            //DrawCall for MeshPass
            for (int BatchIndex = 0; BatchIndex < ElementCounter[0]; BatchIndex++)
            {
                int2 CountOffset = CountOffsetArray[BatchIndex];
                FMeshDrawCommand MeshDrawCommand = MeshDrawCommandsKey[BatchIndex];

                Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
                Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

                /*for (int InstanceIndex = 0; InstanceIndex < CountOffset.x; ++InstanceIndex)
                {
                    int DrawIndex = IndexArray[CountOffset.y + InstanceIndex];
                    GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
                }*/
            }

            IndexArray.Dispose();
            ElementCounter.Dispose();
            CountOffsetArray.Dispose();
            MeshDrawCommandsKey.Dispose();
            MeshDrawCommandsMap.Dispose();
        }

        private void DispatchDrawDefault(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            MeshDrawCommandsMap = new NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>(10000, Allocator.TempJob);

            //Gather PassMeshBatch
            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; Index++)
            {
                if (CullingData.ViewMeshBatchs[Index] != 0)
                {
                    FMeshBatch MeshBatch = MeshBatchs[Index];

                    FMeshDrawCommand MeshDrawCommand = new FMeshDrawCommand(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex, FMeshBatch.MatchForDynamicInstance(ref MeshBatch));
                    FPassMeshBatch PassMeshBatch = new FPassMeshBatch(Index);
                    MeshDrawCommandsMap.Add(MeshDrawCommand, PassMeshBatch);
                }
            }

            //Build MeshDrawCommandKey
            (NativeArray<FMeshDrawCommand>, int) MeshDrawCommandsKey = MeshDrawCommandsMap.GetUniqueKeyArray(Allocator.TempJob);

            //Build MeshPassBuffer
            int BatchOffset = 0;
            NativeArray<int> IndexArray = new NativeArray<int>(MeshDrawCommandsMap.Count(), Allocator.TempJob);
            NativeArray<int2> CountOffsetArray = new NativeArray<int2>(MeshDrawCommandsKey.Item2, Allocator.TempJob);

            for (int KeyIndex = 0; KeyIndex < MeshDrawCommandsKey.Item2; KeyIndex++)
            {
                if (MeshDrawCommandsMap.TryGetFirstValue(MeshDrawCommandsKey.Item1[KeyIndex], out FPassMeshBatch Value, out var Iterator))
                {
                    int BatchIndex = 0;

                    do
                    {
                        IndexArray[BatchIndex + BatchOffset] = Value;
                        BatchIndex += 1;
                    }
                    while (MeshDrawCommandsMap.TryGetNextValue(out Value, ref Iterator));

                    CountOffsetArray[KeyIndex] = new int2(BatchIndex, BatchOffset);
                    Interlocked.Add(ref BatchOffset, BatchIndex);
                }
            }

            //DrawCall for MeshPass
            for (int BatchIndex = 0; BatchIndex < MeshDrawCommandsKey.Item2; BatchIndex++)
            {
                int2 CountOffset = CountOffsetArray[BatchIndex];
                FMeshDrawCommand MeshDrawCommand = MeshDrawCommandsKey.Item1[BatchIndex];

                Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
                Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

                /*for (int InstanceIndex = 0; InstanceIndex < CountOffset.x; ++InstanceIndex)
                {
                    int DrawIndex = IndexArray[CountOffset.y + InstanceIndex];
                    GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
                }*/
            }

            //GraphContext.CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshDrawCommand.SubmeshIndex, DrawMaterial, 2, MeshDrawCommand.InstanceCount);

            IndexArray.Dispose();
            CountOffsetArray.Dispose();
            MeshDrawCommandsMap.Dispose();
            MeshDrawCommandsKey.Item1.Dispose();
        }
    }
}
