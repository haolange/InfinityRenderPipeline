using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Runtime.Core;
using InfinityTech.Runtime.Core.Geometry;
using InfinityTech.Runtime.Rendering.Core;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    public class FMeshBatchProcessor
    {
        public NativeHashMap<int, int> MeshDrawCommandMap;
        public NativeList<FMeshDrawCommand> MeshDrawCommands;

        public FMeshBatchProcessor()
        {

        }

        internal void Init(in int Capacity = 2048)
        {
            MeshDrawCommands = new NativeList<FMeshDrawCommand>(Capacity, Allocator.TempJob);
            MeshDrawCommandMap = new NativeHashMap<int, int>(Capacity, Allocator.TempJob);
        }

        internal void BuildMeshDrawCommand(NativeArray<FMeshBatch> MeshBatchs, in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            if (CullingData.ViewMeshBatchs.Length == 0) { return; }

            int MeshGroupIndex = 0;

            for (int Index = 0; Index < CullingData.ViewMeshBatchs.Length; Index++)
            {
                FViewMeshBatch ViewMeshBatch = CullingData.ViewMeshBatchs[Index];

                if (ViewMeshBatch.index != 0)
                {
                    int MeshDrawCommandIndex;
                    FMeshBatch MeshBatch = MeshBatchs[Index];
                    int InstanceHashCode = MeshBatch.MatchForDynamicInstance();

                    bool bMeshGroup = MeshDrawCommandMap.TryGetValue(InstanceHashCode, out MeshDrawCommandIndex);
                    if (bMeshGroup)
                    {
                        MeshDrawCommands[MeshDrawCommandIndex].MeshBatchIndexs.Add(Index);
                    } else {
                        NativeList<int> MeshBatchIndexs = new NativeList<int>(2048, Allocator.TempJob);
                        MeshBatchIndexs.Add(Index);

                        FMeshDrawCommand MeshDrawCommand = new FMeshDrawCommand(MeshBatch, ref MeshBatchIndexs);
                        MeshDrawCommands.Add(MeshDrawCommand);
                        MeshDrawCommandMap.Add(InstanceHashCode, MeshGroupIndex);

                        MeshGroupIndex += 1;
                    }
                }
            }
        }

        internal void DispatchDraw(CommandBuffer CmdBuffer, FRenderWorld World)
        {
            if (MeshDrawCommands.Length == 0) { return; }

            for (int i = 0; i < MeshDrawCommands.Length; i++)
            {
                FMeshDrawCommand MeshDrawCommand = MeshDrawCommands[i];
                Mesh DrawMesh = World.WorldMeshList.Get(MeshDrawCommand.DrawMesh);
                Material DrawMaterial = World.WorldMaterialList.Get(MeshDrawCommand.DrawMaterial);
                CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshDrawCommand.SubmeshIndex, DrawMaterial, 2, MeshDrawCommand.InstanceCount);
            }
        }

        internal void Release()
        {
            for (int i = 0; i < MeshDrawCommands.Length; i++) { MeshDrawCommands[i].Release(); }

            MeshDrawCommands.Dispose();
            MeshDrawCommandMap.Dispose();
        }
    }
}
