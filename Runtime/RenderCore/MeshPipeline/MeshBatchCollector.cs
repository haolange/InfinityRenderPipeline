using Unity.Jobs;
using Unity.Collections;

namespace InfinityTech.Rendering.MeshPipeline
{
    //[Serializable]
    public class FMeshBatchCollector
    {
        private JobHandle GatherJobRef;
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public FMeshBatchCollector() 
        { 

        }

        public void Initializ()
        {
            CacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void GatherMeshBatch(NativeArray<FMeshBatch> MeshBatchs, in bool bParallel = true, in bool bCallThread = false)
        {
            if(!CacheMeshBatchStateBuckets.IsCreated) { return; }
            
            if(CacheMeshBatchStateBuckets.Count() == 0) { return; }

            if (bCallThread)
            {
                FHashmapGatherValueJob<int, FMeshBatch> HashmapGatherValueJob = new FHashmapGatherValueJob<int, FMeshBatch>();
                {
                    HashmapGatherValueJob.Array = MeshBatchs;
                    HashmapGatherValueJob.Hashmap = CacheMeshBatchStateBuckets;
                }
                HashmapGatherValueJob.Run();
            } else {
                FHashmapParallelGatherValueJob<int, FMeshBatch> HashmapParallelGatherValueJob = new FHashmapParallelGatherValueJob<int, FMeshBatch>();
                {
                    HashmapParallelGatherValueJob.Array = MeshBatchs;
                    HashmapParallelGatherValueJob.Hashmap = CacheMeshBatchStateBuckets;
                }
                GatherJobRef = HashmapParallelGatherValueJob.Schedule(MeshBatchs.Length, 256);
            }

            if (bParallel) { JobHandle.ScheduleBatchedJobs(); }
        }

        public void Sync()
        {
            GatherJobRef.Complete();
        }

        public bool CollectorAvalible()
        {
            return CacheMeshBatchStateBuckets.IsCreated;
        }

        public void AddMeshBatch(in FMeshBatch MeshBatch, in int AddKey)
        {
            CacheMeshBatchStateBuckets.Add(AddKey, MeshBatch);
        }

        public void UpdateMeshBatch(in FMeshBatch MeshBatch, in int UpdateKey)
        {
            CacheMeshBatchStateBuckets[UpdateKey] = MeshBatch;
        }

        public void RemoveMeshBatch(in int RemoveKey)
        {
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
