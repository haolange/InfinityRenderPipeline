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
        public NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch> MeshDrawCommandMaps;

        public FMeshPassProcessor()
        {

        }

        internal void DispatchDraw(RDGContext GraphContext, NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            MeshDrawCommandMaps = new NativeMultiHashMap<FMeshDrawCommand, FPassMeshBatch>(10000, Allocator.TempJob);

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
                            MeshDrawCommandMaps.Add(MeshDrawCommand, PassMeshBatch);

                            Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshBatch.Mesh);
                            Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshBatch.Material);
                            if (DrawMesh && DrawMaterial)
                            {
                                GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatch.Matrix_LocalToWorld, DrawMaterial, MeshBatch.SubmeshIndex, 2);
                            }
                        }
                    }
                    break;

                case EGatherMethod.Burst:
                    FViewMeshBatchGatherJob ViewMeshBatchGatherJob = new FViewMeshBatchGatherJob();
                    {
                        ViewMeshBatchGatherJob.MeshBatchs = MeshBatchs;
                        ViewMeshBatchGatherJob.CullingData = CullingData;
                        ViewMeshBatchGatherJob.MeshDrawCommandMaps = MeshDrawCommandMaps;
                    }
                    ViewMeshBatchGatherJob.Run();
                    break;

                case EGatherMethod.Parallel:
                    FViewMeshBatchParallelGatherJob ViewMeshBatchParallelGatherJob = new FViewMeshBatchParallelGatherJob();
                    {
                        ViewMeshBatchParallelGatherJob.MeshBatchs = MeshBatchs;
                        ViewMeshBatchParallelGatherJob.CullingData = CullingData;
                        ViewMeshBatchParallelGatherJob.MeshDrawCommandMaps = MeshDrawCommandMaps.AsParallelWriter();
                    }
                    ViewMeshBatchParallelGatherJob.Schedule(CullingData.ViewMeshBatchs.Length, 256).Complete();
                    break;
            }

            /*for (int i = 0; i < MeshDrawCommandMaps.Count(); i++)
            {
                FMeshDrawCommandValue MeshBatchIndex;
                if (MeshDrawCommandMaps.TryGetFirstValue(MeshDrawCommandKeys[i], out MeshBatchIndex, out var iterator))
                {
                    while (MeshDrawCommandMaps.TryGetNextValue(out MeshBatchIndex, ref iterator))
                    {
                        Matrixs.Add(MeshBatchs[MeshBatchIndex].Matrix_LocalToWorld);
                    }

                    Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshBatchs[MeshBatchIndex].Mesh);
                    Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshBatchs[MeshBatchIndex].Material);
                    GraphContext.CmdBuffer.DrawMeshInstanced(DrawMesh, 0, DrawMaterial, 2, Matrixs.ToArray(), Matrixs.Count);
                }
                //GraphContext.CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshDrawCommand.SubmeshIndex, DrawMaterial, 2, MeshDrawCommand.InstanceCount);
            }*/

            MeshDrawCommandMaps.Dispose();
        }
    }
}
