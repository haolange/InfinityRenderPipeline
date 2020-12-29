using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Runtime.Core;
using InfinityTech.Runtime.Core.Geometry;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    internal class FMeshBatchProcessor
    {
        protected NativeArray<FMeshBatch> MeshBatchList;
        protected NativeHashMap<int, FMeshDrawCommand> PassDrawContextList;

        internal FMeshBatchProcessor()
        {
            PassDrawContextList = new NativeHashMap<int, FMeshDrawCommand>(1024, Allocator.Persistent);
        }

        internal void Reset()
        {
            NativeArray<FMeshDrawCommand> MeshDrawCommandList = PassDrawContextList.GetValueArray(Allocator.Temp);
            for (int i = 0; i < MeshDrawCommandList.Length; i++)
            {
                MeshDrawCommandList[i].Reset();
            }
            MeshDrawCommandList.Dispose();

            PassDrawContextList.Clear();
        }

        internal void BuildMeshDrawCommand(in FCullingData CullingData, in FMeshPassDesctiption MeshPassDesctiption)
        {
            /*NativeArray<FViewMeshBatch> ViewMeshBatchList = CullingData.ViewMeshBatchList;
            for (int i = 0; i < ViewMeshBatchList.Length; i++)
            {
                FViewMeshBatch VisibleMeshBatch  = ViewMeshBatchList[i];
                FMeshBatch MeshBatch = MeshBatchList[VisibleMeshBatch.index];

                FMeshDrawCommand MeshDrawCommand;
                int MeshDrawCommandIndex = MeshBatch.MatchForDynamicInstance();

                bool HashMeshGroup = PassDrawContextList.TryGetValue(MeshDrawCommandIndex, out MeshDrawCommand);
                if (HashMeshGroup)
                {
                    PassDrawContextList[MeshBatch.MatchForDynamicInstance()].MeshBatchIndexBuffer.Add(VisibleMeshBatch.index);
                } else {
                    MeshDrawCommand = new FMeshDrawCommand();
                    MeshDrawCommand.Init();
                    PassDrawContextList.Add(MeshBatch.MatchForDynamicInstance(), MeshDrawCommand);
                }
            }*/
        }

        internal void DispatchDraw()
        {

        }

        internal void Release()
        {
            NativeArray<FMeshDrawCommand> MeshDrawCommandList = PassDrawContextList.GetValueArray(Allocator.Temp);
            for (int i = 0; i < MeshDrawCommandList.Length; i++)
            {
                MeshDrawCommandList[i].Release();
            }
            MeshDrawCommandList.Dispose();

            PassDrawContextList.Dispose();
        }
    }
}
