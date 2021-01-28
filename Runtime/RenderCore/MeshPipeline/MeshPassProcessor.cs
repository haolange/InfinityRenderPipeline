using Unity.Jobs;
using UnityEngine;
using System.Threading;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Native;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Pipeline;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct FMeshPassDesctiption
    {
        public int RenderQueueMin;
        public int RenderQueueMax;
        public int RenderLayerMask;
        public EGatherMethod GatherMethod;
        public bool ExcludeMotionVectorObjects;

        public FMeshPassDesctiption(in RendererList InRendererList, in EGatherMethod InGatherMethod = EGatherMethod.DotsV2)
        {
            GatherMethod = InGatherMethod;
            RenderLayerMask = (int)InRendererList.filteringSettings.renderingLayerMask;
            RenderQueueMin = InRendererList.filteringSettings.renderQueueRange.lowerBound;
            RenderQueueMax = InRendererList.filteringSettings.renderQueueRange.upperBound;
            ExcludeMotionVectorObjects = InRendererList.filteringSettings.excludeMotionVectorObjects;
        }
    }

    public class FMeshPassProcessor
    {
        public FMeshPassProcessor()
        {

        }

        internal void DispatchDraw(RDGContext GraphContext, FGPUScene GPUScene, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (GPUScene.MeshBatchs.IsCreated == false || CullingData.ViewMeshBatchs.IsCreated == false || CullingData.bRendererView != true) { return; }

            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            switch (MeshPassDesctiption.GatherMethod)
            {
                case EGatherMethod.DotsV1:
                    DispatchDrawDotsV1(GraphContext, GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);
                    break;

                case EGatherMethod.DotsV2:
                    DispatchDrawDotsV2(GraphContext, GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);
                    break;

                case EGatherMethod.DefaultV1:
                    DispatchDrawDefaultV1(GraphContext, GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);
                    break;

                case EGatherMethod.DefaultV2:
                    DispatchDrawDefaultV2(GraphContext, GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);
                    break;
            }
        }

        private void DispatchDrawDotsV1(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>  MeshDrawCommandsMap = new NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>(10000, Allocator.TempJob);

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

            //DrawCall
            using (new ProfilingScope(GraphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshDrawPipeline)))
            {
                for (int BatchIndex = 0; BatchIndex < ElementCounter[0]; BatchIndex++)
                {
                    int2 CountOffset = CountOffsetArray[BatchIndex];
                    FMeshDrawCommand MeshDrawCommand = MeshDrawCommandsKey[BatchIndex];

                    Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
                    Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

                    for (int InstanceIndex = 0; InstanceIndex < CountOffset.x; ++InstanceIndex)
                    {
                        int DrawIndex = IndexArray[CountOffset.y + InstanceIndex];
                        GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
                    }
                }
            }

            IndexArray.Dispose();
            ElementCounter.Dispose();
            CountOffsetArray.Dispose();
            MeshDrawCommandsKey.Dispose();
            MeshDrawCommandsMap.Dispose();
        }

        private void DispatchDrawDotsV2(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            //Gather PassMeshBatch
            NativeArray<int> Indexs = new NativeArray<int>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            NativeList<int2> CountOffsets = new NativeList<int2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            NativeList<FPassMeshBatchV2> PassMeshBatchs = new NativeList<FPassMeshBatchV2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            NativeList<FMeshDrawCommandV2> MeshDrawCommands = new NativeList<FMeshDrawCommandV2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);

            FPassMeshBatchGatherJobV2 PassMeshBatchGatherJob = new FPassMeshBatchGatherJobV2();
            {
                PassMeshBatchGatherJob.MeshBatchs = MeshBatchs;
                PassMeshBatchGatherJob.CullingData = CullingData;
                PassMeshBatchGatherJob.PassMeshBatchs = PassMeshBatchs;
            }
            PassMeshBatchGatherJob.Run();

            //Sort PassMeshBatch
            FArraySortJob<FPassMeshBatchV2> ArraySortJob = new FArraySortJob<FPassMeshBatchV2>();
            {
                ArraySortJob.SortTarget = PassMeshBatchs;
            }
            ArraySortJob.Run();

            //Build MeshDrawCommand
            FMeshDrawCommandBuildJob MeshDrawCommandBuildJob = new FMeshDrawCommandBuildJob();
            {
                MeshDrawCommandBuildJob.Indexs = Indexs;
                MeshDrawCommandBuildJob.CountOffsets = CountOffsets;
                MeshDrawCommandBuildJob.MeshBatchs = MeshBatchs;
                MeshDrawCommandBuildJob.PassMeshBatchs = PassMeshBatchs;
                MeshDrawCommandBuildJob.MeshDrawCommands = MeshDrawCommands;
            }
            MeshDrawCommandBuildJob.Run();

            //DrawCall
            using (new ProfilingScope(GraphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshDrawPipeline)))
            {
                for (int BatchIndex = 0; BatchIndex < CountOffsets.Length; BatchIndex++)
                {
                    int2 CountOffset = CountOffsets[BatchIndex];
                    FMeshDrawCommandV2 MeshDrawCommand = MeshDrawCommands[BatchIndex];

                    Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
                    Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

                    for (int InstanceIndex = 0; InstanceIndex < CountOffset.x; ++InstanceIndex)
                    {
                        int DrawIndex = Indexs[CountOffset.y + InstanceIndex];
                        GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
                    }
                }
            }

            Indexs.Dispose();
            CountOffsets.Dispose();
            PassMeshBatchs.Dispose();
            MeshDrawCommands.Dispose();
        }

        private void DispatchDrawDefaultV1(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>  MeshDrawCommandsMap = new NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>(10000, Allocator.TempJob);

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

            //DrawCall
            using (new ProfilingScope(GraphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshDrawPipeline)))
            {
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
            }
            //GraphContext.CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshDrawCommand.SubmeshIndex, DrawMaterial, 2, MeshDrawCommand.InstanceCount);

            IndexArray.Dispose();
            CountOffsetArray.Dispose();
            MeshDrawCommandsMap.Dispose();
            MeshDrawCommandsKey.Item1.Dispose();
        }

        private void DispatchDrawDefaultV2(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            //Gather MeshBatch
            FMeshBatch MeshBatch;
            FPassMeshBatchV2 PassMeshBatch;
            NativeList<FPassMeshBatchV2> PassMeshBatchs = new NativeList<FPassMeshBatchV2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);

            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; Index++)
            {
                if (CullingData.ViewMeshBatchs[Index] != 0)
                {
                    MeshBatch = MeshBatchs[Index];
                    PassMeshBatch = new FPassMeshBatchV2(FMeshBatch.MatchForDynamicInstance(ref MeshBatch), Index);
                    PassMeshBatchs.Add(PassMeshBatch);
                }
            }

            if (PassMeshBatchs.Length != 0)
            {
                //Sort MeshBatch
                PassMeshBatchs.Sort();

                //Build DrawCall
                FPassMeshBatchV2 CachePassMeshBatch = new FPassMeshBatchV2(-1, -1);
                NativeArray<int> Indexs = new NativeArray<int>(PassMeshBatchs.Length, Allocator.TempJob);
                NativeList<int2> CountOffsets = new NativeList<int2>(PassMeshBatchs.Length, Allocator.TempJob);
                NativeList<FMeshDrawCommandV2> MeshDrawCommands = new NativeList<FMeshDrawCommandV2>(PassMeshBatchs.Length, Allocator.TempJob);

                for (int i = 0; i < PassMeshBatchs.Length; i++)
                {
                    PassMeshBatch = PassMeshBatchs[i];
                    Indexs[i] = PassMeshBatch.MeshBatchIndex;
                    MeshBatch = MeshBatchs[PassMeshBatch.MeshBatchIndex];

                    if (!PassMeshBatch.Equals(CachePassMeshBatch))
                    {
                        CachePassMeshBatch = PassMeshBatch;

                        CountOffsets.Add(new int2(0, i));
                        MeshDrawCommands.Add(new FMeshDrawCommandV2(MeshBatch.Mesh.Id, MeshBatch.Material.Id, MeshBatch.SubmeshIndex));
                    }

                    int2 CountOffset = CountOffsets[CountOffsets.Length - 1];
                    CountOffsets[CountOffsets.Length - 1] = CountOffset + new int2(1, 0);
                }


                //DrawCall
                using (new ProfilingScope(GraphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshDrawPipeline)))
                {
                    for (int BatchIndex = 0; BatchIndex < CountOffsets.Length; BatchIndex++)
                    {
                        int2 CountOffset = CountOffsets[BatchIndex];
                        FMeshDrawCommandV2 MeshDrawCommand = MeshDrawCommands[BatchIndex];

                        Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
                        Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

                        for (int InstanceIndex = 0; InstanceIndex < CountOffset.x; ++InstanceIndex)
                        {
                            int DrawIndex = Indexs[CountOffset.y + InstanceIndex];
                            GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
                        }
                    }
                }
                Indexs.Dispose();
                CountOffsets.Dispose();
                PassMeshBatchs.Dispose();
                MeshDrawCommands.Dispose();
            }
            else
            {
                PassMeshBatchs.Dispose();
            }
        }
    }
}






/*private void DispatchDrawDotsV2(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
{
    //Gather PassMeshBatch
    NativeList<FPassMeshBatchV2> PassMeshBatchs = new NativeList<FPassMeshBatchV2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);

    FPassMeshBatchGatherJobV2 PassMeshBatchGatherJob = new FPassMeshBatchGatherJobV2();
    {
        PassMeshBatchGatherJob.MeshBatchs = MeshBatchs;
        PassMeshBatchGatherJob.CullingData = CullingData;
        PassMeshBatchGatherJob.PassMeshBatchs = PassMeshBatchs;
    }
    PassMeshBatchGatherJob.Run();

    //if (PassMeshBatchs.Length != 0) 
    //{
    //Sort PassMeshBatch
    FArraySortJob<FPassMeshBatchV2> ArraySortJob = new FArraySortJob<FPassMeshBatchV2>();
    {
        ArraySortJob.SortTarget = PassMeshBatchs;
    }
    ArraySortJob.Run();

    //Build MeshDrawCommand
    NativeArray<int> Indexs = new NativeArray<int>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
    NativeList<int2> CountOffsets = new NativeList<int2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
    NativeList<FMeshDrawCommandV2> MeshDrawCommands = new NativeList<FMeshDrawCommandV2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);

    FMeshDrawCommandBuildJob MeshDrawCommandBuildJob = new FMeshDrawCommandBuildJob();
    {
        MeshDrawCommandBuildJob.Indexs = Indexs;
        MeshDrawCommandBuildJob.CountOffsets = CountOffsets;
        MeshDrawCommandBuildJob.MeshBatchs = MeshBatchs;
        MeshDrawCommandBuildJob.PassMeshBatchs = PassMeshBatchs;
        MeshDrawCommandBuildJob.MeshDrawCommands = MeshDrawCommands;
    }
    MeshDrawCommandBuildJob.Run();

    //DrawCall
    using (new ProfilingScope(GraphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshDrawPipeline)))
    {
        for (int BatchIndex = 0; BatchIndex < CountOffsets.Length; BatchIndex++)
        {
            int2 CountOffset = CountOffsets[BatchIndex];
            FMeshDrawCommandV2 MeshDrawCommand = MeshDrawCommands[BatchIndex];

            Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
            Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

            for (int InstanceIndex = 0; InstanceIndex < CountOffset.x; ++InstanceIndex)
            {
                int DrawIndex = Indexs[CountOffset.y + InstanceIndex];
                GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
            }
        }
    }

    Indexs.Dispose();
    CountOffsets.Dispose();
    PassMeshBatchs.Dispose();
    MeshDrawCommands.Dispose();
    } else {
    PassMeshBatchs.Dispose();
    }
}*/
