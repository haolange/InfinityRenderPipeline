using Unity.Jobs;
using Unity.Collections;

namespace InfinityTech.Rendering.MeshPipeline
{
    //[Serializable]
    public class FMeshBatchCollector
    {
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public FMeshBatchCollector() 
        { 

        }

        public void Initializ()
        {
            CacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void GatherMeshBatch(in NativeArray<FMeshBatch> MeshBatchs, in bool bParallel = true)
        {
            if(!CacheMeshBatchStateBuckets.IsCreated) { return; }
            
            if(CacheMeshBatchStateBuckets.Count() == 0) { return; }

            if (!bParallel)
            {
                CacheMeshBatchStateBuckets.GetValueArray(MeshBatchs);
                /*FHashmapGatherValueJob<int, FMeshBatch> MeshBatchGatherJob = new FHashmapGatherValueJob<int, FMeshBatch>();
                {
                    MeshBatchGatherJob.Array = MeshBatchs;
                    MeshBatchGatherJob.Hashmap = CacheMeshBatchStateBuckets;
                }
                MeshBatchGatherJob.Run();*/
            } else {
                FMeshBatchGatherJob MeshBatchParallelGatherJob = new FMeshBatchGatherJob();
                {
                    MeshBatchParallelGatherJob.Array = MeshBatchs;
                    MeshBatchParallelGatherJob.Hashmap = CacheMeshBatchStateBuckets;
                }
                MeshBatchParallelGatherJob.Schedule(MeshBatchs.Length, 256).Complete();
            }
        }

        public void AddMeshBatch(in FMeshBatch MeshBatch, in int AddKey)
        {
            CacheMeshBatchStateBuckets.TryAdd(AddKey, MeshBatch);
        }

        public void UpdateMeshBatch(in FMeshBatch MeshBatch, in int UpdateKey)
        {
            CacheMeshBatchStateBuckets[UpdateKey] = MeshBatch;
        }

        public void RemoveMeshBatch(in int RemoveKey)
        {
            if(CacheMeshBatchStateBuckets.IsCreated == false) { return; }
            CacheMeshBatchStateBuckets.Remove(RemoveKey);
        }

        public void Reset()
        {
            CacheMeshBatchStateBuckets.Clear();
        }

        public void Release()
        {
            CacheMeshBatchStateBuckets.Dispose();
        }
    }
}
