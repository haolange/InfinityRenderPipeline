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

namespace InfinityTech.Rendering.MeshDrawPipeline
{
    public class FMeshPassProcessor
    {
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch> MeshDrawCommandsMap;

        public FMeshPassProcessor()
        {

        }

        internal void DispatchDraw(RDGContext GraphContext, NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            MeshDrawCommandsMap = new NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>(10000, Allocator.TempJob);

            //Gather PassMeshBatch
            switch (MeshPassDesctiption.GatherMethod)
            {
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

                case EGatherMethod.Burst:
                    FViewMeshBatchGatherJob ViewMeshBatchGatherJob = new FViewMeshBatchGatherJob();
                    {
                        ViewMeshBatchGatherJob.MeshBatchs = MeshBatchs;
                        ViewMeshBatchGatherJob.CullingData = CullingData;
                        ViewMeshBatchGatherJob.MeshDrawCommandMaps = MeshDrawCommandsMap;
                    }
                    ViewMeshBatchGatherJob.Run();
                    break;

                case EGatherMethod.Parallel:
                    FViewMeshBatchParallelGatherJob ViewMeshBatchParallelGatherJob = new FViewMeshBatchParallelGatherJob();
                    {
                        ViewMeshBatchParallelGatherJob.MeshBatchs = MeshBatchs;
                        ViewMeshBatchParallelGatherJob.CullingData = CullingData;
                        ViewMeshBatchParallelGatherJob.MeshDrawCommandMaps = MeshDrawCommandsMap.AsParallelWriter();
                    }
                    ViewMeshBatchParallelGatherJob.Schedule(CullingData.ViewMeshBatchs.Length, 256).Complete();
                    break;
            }

            var Keys = MeshDrawCommandsMap.GetUniqueKeyArray(Allocator.TempJob);

            int BatchOffset = 0;
            NativeArray<int> CountArray = new NativeArray<int>(Keys.Item2, Allocator.TempJob);
            NativeArray<int> OffsetArray = new NativeArray<int>(Keys.Item2, Allocator.TempJob);
            NativeArray<int> IndexArray = new NativeArray<int>(MeshDrawCommandsMap.Count(), Allocator.TempJob);

            //Build PassBuffer
            for (int KeyIndex = 0; KeyIndex < Keys.Item2; ++KeyIndex)
            {
                if (MeshDrawCommandsMap.TryGetFirstValue(Keys.Item1[KeyIndex], out FPassMeshBatch Value, out var Iterator))
                {
                    int BatchIndex = 0;

                    do
                    {
                        IndexArray[BatchIndex + BatchOffset] = Value;
                        BatchIndex += 1;
                    }
                    while (MeshDrawCommandsMap.TryGetNextValue(out Value, ref Iterator));

                    CountArray[KeyIndex] = BatchIndex;
                    OffsetArray[KeyIndex] = BatchOffset;
                    BatchOffset += BatchIndex;
                }
            }

            //Pass DrawCall
            for (int BatchIndex = 0; BatchIndex < Keys.Item2; ++BatchIndex)
            {
                int DrawCount = CountArray[BatchIndex];
                int DrawOffset = OffsetArray[BatchIndex];
                FMeshDrawCommand MeshDrawCommand = Keys.Item1[BatchIndex];

                Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshDrawCommand.MeshID);
                Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshDrawCommand.MaterialID);

                for (int InstanceIndex = 0; InstanceIndex < DrawCount; ++InstanceIndex)
                {
                    int DrawIndex = IndexArray[DrawOffset + InstanceIndex];
                    GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatchs[DrawIndex].Matrix_LocalToWorld, DrawMaterial, MeshDrawCommand.SubmeshIndex, 2);
                }
            }

            //GraphContext.CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshDrawCommand.SubmeshIndex, DrawMaterial, 2, MeshDrawCommand.InstanceCount);

            Keys.Item1.Dispose();
            CountArray.Dispose();
            IndexArray.Dispose();
            OffsetArray.Dispose();
            MeshDrawCommandsMap.Dispose();
        }
    }
}
