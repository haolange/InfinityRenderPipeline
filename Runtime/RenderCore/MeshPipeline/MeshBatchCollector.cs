using Unity.Jobs;
using Unity.Collections;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FMeshBatchCollector
    {
        public NativeHashMap<int, FMeshBatch> cacheMeshBatchStateBuckets;

        public FMeshBatchCollector() 
        { 

        }

        public void Initializ()
        {
            cacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void GatherMeshBatch(ref NativeArray<FMeshBatch> meshBatchs, in int methdo = 0)
        {
            if(!cacheMeshBatchStateBuckets.IsCreated) { return; }
            
            if(cacheMeshBatchStateBuckets.Count() == 0) { return; }

            if (methdo == 0)
            {
                FHashmapGatherValueJob<int, FMeshBatch> MeshBatchGatherJob = new FHashmapGatherValueJob<int, FMeshBatch>();
                MeshBatchGatherJob.Array = meshBatchs;
                MeshBatchGatherJob.Hashmap = cacheMeshBatchStateBuckets;
                MeshBatchGatherJob.Run();
            } else {
                FHashmapParallelGatherValueJob<int, FMeshBatch> MeshBatchGatherJob = new FHashmapParallelGatherValueJob<int, FMeshBatch>();
                MeshBatchGatherJob.Array = meshBatchs;
                MeshBatchGatherJob.Hashmap = cacheMeshBatchStateBuckets;
                MeshBatchGatherJob.Schedule(meshBatchs.Length, 256).Complete();
            }
        }

        public void AddMeshBatch(in FMeshBatch MeshBatch, in int AddKey)
        {
            cacheMeshBatchStateBuckets.TryAdd(AddKey, MeshBatch);
        }

        public void UpdateMeshBatch(in FMeshBatch MeshBatch, in int UpdateKey)
        {
            cacheMeshBatchStateBuckets[UpdateKey] = MeshBatch;
        }

        public void RemoveMeshBatch(in int RemoveKey)
        {
            if(cacheMeshBatchStateBuckets.IsCreated == false) { return; }
            cacheMeshBatchStateBuckets.Remove(RemoveKey);
        }

        public void Reset()
        {
            cacheMeshBatchStateBuckets.Clear();
        }

        public void Release()
        {
            cacheMeshBatchStateBuckets.Dispose();
        }
    }
}
