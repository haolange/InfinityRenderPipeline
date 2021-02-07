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


        public void Gather(FMeshBatchCollector InMeshBatchCollector, in bool Block = false, in bool bParallel = true)
        {
            if(Block) { return; }

            MeshBatchCollector = InMeshBatchCollector;

            if(MeshBatchCollector.CacheMeshBatchStateBuckets.IsCreated)
            {
                MeshBatchs = new NativeArray<FMeshBatch>(MeshBatchCollector.CacheMeshBatchStateBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                MeshBatchCollector.GatherMeshBatch(MeshBatchs, bParallel);
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
