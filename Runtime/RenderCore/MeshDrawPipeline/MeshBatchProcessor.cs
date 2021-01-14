using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.Core;

namespace InfinityTech.Rendering.MeshDrawPipeline
{
    public class FMeshBatchProcessor
    {
        public NativeMultiHashMap<int, int> MeshDrawCommands;

        public FMeshBatchProcessor()
        {

        }

        internal void Init(in int Capacity = 2048)
        {
            MeshDrawCommands = new NativeMultiHashMap<int, int>(Capacity, Allocator.TempJob);
        }

        internal void BuildMeshDrawCommand(NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; Index++)
            {
                if (CullingData.ViewMeshBatchs[Index] != 0)
                {
                    FMeshBatch MeshBatch = MeshBatchs[Index];
                    int MatchInstanceID = MeshBatch.MatchForDynamicInstance();

                    //FMeshDrawCommandKey MeshDrawCommandKey = new FMeshDrawCommandKey(MeshBatch.Mesh.Id , MeshBatch.Material.Id, MeshBatch.SubmeshIndex, MatchInstanceID);
                    //FMeshDrawCommandValue MeshDrawCommandValue = new FMeshDrawCommandValue(Index);
                    MeshDrawCommands.Add(MatchInstanceID, Index);
                }
            }
        }

        internal void DispatchDraw(CommandBuffer CmdBuffer, FRenderWorld World)
        {
            /*if (MeshDrawCommands.Length == 0) { return; }

            for (int i = 0; i < MeshDrawCommands.Length; i++)
            {
                FMeshDrawCommand MeshDrawCommand = MeshDrawCommands[i];
                Mesh DrawMesh = World.WorldMeshList.Get(MeshDrawCommand.DrawMesh);
                Material DrawMaterial = World.WorldMaterialList.Get(MeshDrawCommand.DrawMaterial);
                CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshDrawCommand.SubmeshIndex, DrawMaterial, 2, MeshDrawCommand.InstanceCount);
            }*/
        }

        internal void Release()
        {
            MeshDrawCommands.Dispose();
        }
    }
}
