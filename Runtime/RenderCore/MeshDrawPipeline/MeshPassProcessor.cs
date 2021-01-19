using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Core;
using System.Threading;

namespace InfinityTech.Rendering.MeshDrawPipeline
{
    public class FMeshPassProcessor
    {
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch> MeshDrawCommandsMap;

        public FMeshPassProcessor()
        {

        }

        internal void DispatchDraw(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            MeshDrawCommandsMap = new NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>(10000, Allocator.TempJob);

            //Gather PassMeshBatch
            switch (MeshPassDesctiption.GatherMethod)
            {
                case EGatherMethod.Dots:
                    FPassMeshBatchGatherJob PassMeshBatchGatherJob = new FPassMeshBatchGatherJob();
                    {
                        PassMeshBatchGatherJob.MeshBatchs = MeshBatchs;
                        PassMeshBatchGatherJob.CullingData = CullingData;
                        PassMeshBatchGatherJob.MeshDrawCommandMaps = MeshDrawCommandsMap;
                    }
                    PassMeshBatchGatherJob.Run();
                    break;

                case EGatherMethod.Default:
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
                    break;
            }

            //Gather MeshDrawCommandKey
            var MeshDrawCommandsKey = MeshDrawCommandsMap.GetUniqueKeyArray(Allocator.TempJob);
            NativeArray<int> IndexArray = new NativeArray<int>(MeshDrawCommandsMap.Count(), Allocator.TempJob);
            NativeArray<int2> CountOffsetArray = new NativeArray<int2>(MeshDrawCommandsKey.Item2, Allocator.TempJob);

            //Gather MeshPassBuffer
            switch (MeshPassDesctiption.GatherMethod)
            {
                case EGatherMethod.Dots:
                    FPassMeshBatchConvertJob PassMeshBatchConvertJob = new FPassMeshBatchConvertJob();
                    {
                        PassMeshBatchConvertJob.Count = MeshDrawCommandsKey.Item2;
                        PassMeshBatchConvertJob.IndexArray = IndexArray;
                        PassMeshBatchConvertJob.CountOffsetArray = CountOffsetArray;
                        PassMeshBatchConvertJob.MeshDrawCommands = MeshDrawCommandsKey.Item1;
                        PassMeshBatchConvertJob.MeshDrawCommandsMap = MeshDrawCommandsMap;
                    }
                    PassMeshBatchConvertJob.Run();
                    break;

                case EGatherMethod.Default:
                    int BatchOffset = 0;
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
                    break;
            }

            //DrawCall for MeshPass
            for (int BatchIndex = 0; BatchIndex < MeshDrawCommandsKey.Item2; BatchIndex++)
            {
                int2 CountOffset = CountOffsetArray[BatchIndex];
                FMeshDrawCommand MeshDrawCommand = MeshDrawCommandsKey.Item1[BatchIndex];

                Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
                Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

                for (int InstanceIndex = 0; InstanceIndex < CountOffset.x; ++InstanceIndex)
                {
                    int DrawIndex = IndexArray[CountOffset.y + InstanceIndex];
                    GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
                }
            }

            //GraphContext.CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshDrawCommand.SubmeshIndex, DrawMaterial, 2, MeshDrawCommand.InstanceCount);

            IndexArray.Dispose();
            CountOffsetArray.Dispose();
            MeshDrawCommandsMap.Dispose();
            MeshDrawCommandsKey.Item1.Dispose();
        }
    }
}
