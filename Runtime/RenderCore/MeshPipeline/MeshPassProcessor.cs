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
        public bool ExcludeMotionVectorObjects;

        public FMeshPassDesctiption(in RendererList InRendererList)
        {
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

        internal void DispatchGather(RDGContext GraphContext, FGPUScene GPUScene, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (GPUScene.MeshBatchs.IsCreated == false || CullingData.ViewMeshBatchs.IsCreated == false || CullingData.bRendererView != true) { return; }

            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            DispatchGatherInternal(GraphContext, GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);
        }

        private void DispatchGatherInternal(RDGContext GraphContext, in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            //Gather PassMeshBatch
            NativeArray<int> Indexs = new NativeArray<int>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            NativeList<int2> CountOffsets = new NativeList<int2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            NativeList<FPassMeshBatchV2> PassMeshBatchs = new NativeList<FPassMeshBatchV2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            NativeList<FMeshDrawCommandV2> MeshDrawCommands = new NativeList<FMeshDrawCommandV2>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);

            FBuildMeshDrawCommandJob BuildMeshDrawCommandJob = new FBuildMeshDrawCommandJob();
            {
                BuildMeshDrawCommandJob.Indexs = Indexs;
                BuildMeshDrawCommandJob.CullingData = CullingData;
                BuildMeshDrawCommandJob.CountOffsets = CountOffsets;
                BuildMeshDrawCommandJob.MeshBatchs = MeshBatchs;
                BuildMeshDrawCommandJob.PassMeshBatchs = PassMeshBatchs;
                BuildMeshDrawCommandJob.MeshDrawCommands = MeshDrawCommands;
            }
            BuildMeshDrawCommandJob.Run();

            //DrawCall
            /*using (new ProfilingScope(GraphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshDrawPipeline)))
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
            }*/

            Indexs.Dispose();
            CountOffsets.Dispose();
            PassMeshBatchs.Dispose();
            MeshDrawCommands.Dispose();
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
