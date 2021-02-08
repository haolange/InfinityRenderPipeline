using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FGPUScene
    {
        public NativeArray<FMeshBatch> MeshBatchs;
        protected FMeshBatchCollector MeshBatchCollector;


        public void Gather(FMeshBatchCollector InMeshBatchCollector, in int Methdo = 0, in bool Block = false)
        {
            if(Block) { return; }

            MeshBatchCollector = InMeshBatchCollector;

            if(MeshBatchCollector.CacheMeshBatchStateBuckets.IsCreated)
            {
                MeshBatchs = new NativeArray<FMeshBatch>(MeshBatchCollector.CacheMeshBatchStateBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                MeshBatchCollector.GatherMeshBatch(MeshBatchs, Methdo);
            }
        }

        public void Release()
        {
            if(MeshBatchs.IsCreated)
            {
                MeshBatchs.Dispose();
            }
        }
    }
}
