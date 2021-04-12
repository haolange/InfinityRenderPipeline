using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;
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

        public FMeshPassDesctiption(in int MinQueue, in int MaxQueue)
        {
            RenderLayerMask = 0;
            RenderQueueMin = MinQueue;
            RenderQueueMax = MaxQueue;
            ExcludeMotionVectorObjects = false;
        }
    }

    public class FMeshPassProcessor
    {
        internal bool bGatherState;
        internal bool bScheduleState;
        internal FGPUScene GPUScene;
        internal JobHandle GatherHandle;
        internal MaterialPropertyBlock BatchPropertyBlock;

        internal NativeArray<int> Indexs;
        internal NativeList<FPassMeshBatch> PassMeshBatchs;
        internal NativeList<FMeshDrawCommand> MeshDrawCommands;

        public FMeshPassProcessor(FGPUScene InGPUScene)
        {
            GPUScene = InGPUScene;
            BatchPropertyBlock = new MaterialPropertyBlock();
        }

        internal void DispatchSetup(in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            bGatherState = false;
            bScheduleState = false;

            if (GPUScene.MeshBatchs.IsCreated == false || CullingData.ViewMeshBatchs.IsCreated == false || CullingData.bRendererView != true) { return; }

            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            DispatchGatherInternal(GPUScene.MeshBatchs, CullingData, MeshPassDesctiption);

            bGatherState = true;
        }

        internal void WaitSetupFinish()
        {
            if (bScheduleState == false) { return; }
                GatherHandle.Complete();
        }

        internal void DispatchDraw(RDGContext GraphContext, in int PassIndex)
        {
            if (bGatherState == false) { return; }

            using (new ProfilingScope(GraphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshBatch)))
            {
                BufferRef BufferHandle = GraphContext.ResourcePool.AllocateBuffer(new BufferDescription(64000, Marshal.SizeOf(typeof(int))));
                GraphContext.CmdBuffer.SetComputeBufferData(BufferHandle.Buffer, Indexs);

                for (int BatchIndex = 0; BatchIndex < MeshDrawCommands.Length; ++BatchIndex)
                {
                    FMeshDrawCommand meshDrawCommand = MeshDrawCommands[BatchIndex];
                    Mesh mesh = GraphContext.World.meshAssetList.Get(meshDrawCommand.meshIndex);
                    Material material = GraphContext.World.materialAssetList.Get(meshDrawCommand.materialindex);

                    BatchPropertyBlock.SetInt(InfinityShaderIDs.MeshBatchOffset, meshDrawCommand.countOffset.y);
                    BatchPropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchIndexs, BufferHandle.Buffer);
                    BatchPropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchBuffer, GPUScene.BufferHandle.Buffer);
                    GraphContext.CmdBuffer.DrawMeshInstancedProcedural(mesh, meshDrawCommand.submeshIndex, material, PassIndex, meshDrawCommand.countOffset.x, BatchPropertyBlock);
                }

                GraphContext.ResourcePool.ReleaseBuffer(BufferHandle);
            }

            Indexs.Dispose();
            PassMeshBatchs.Dispose();
            MeshDrawCommands.Dispose();
        }

        private void DispatchGatherInternal(in NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            Indexs = new NativeArray<int>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            PassMeshBatchs = new NativeList<FPassMeshBatch>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);
            MeshDrawCommands = new NativeList<FMeshDrawCommand>(CullingData.ViewMeshBatchs.Length, Allocator.TempJob);

            bScheduleState = true;
            FMeshDrawCommandBuildJob meshDrawCommandBuildJob = new FMeshDrawCommandBuildJob();
            {
                meshDrawCommandBuildJob.Indexs = Indexs;
                meshDrawCommandBuildJob.MeshBatchs = MeshBatchs;
                meshDrawCommandBuildJob.CullingData = CullingData;
                meshDrawCommandBuildJob.PassMeshBatchs = PassMeshBatchs;
                meshDrawCommandBuildJob.MeshDrawCommands = MeshDrawCommands;
                meshDrawCommandBuildJob.MeshPassDesctiption = MeshPassDesctiption;
            }
            GatherHandle = meshDrawCommandBuildJob.Schedule();
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
        for (int BatchIndex = 0; BatchIndex < CountOffsets.Length; ++BatchIndex)
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
