using Unity.Jobs;
using Unity.Collections;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    //[Serializable]
    public class FMeshBatchCollector
    {
        private JobHandle GatherJobRef;
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public FMeshBatchCollector() { }

        public void Initializ()
        {
            CacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void GatherMeshBatch(NativeArray<FMeshBatch> MeshBatchArray, in bool bParallel = true, in bool bCallThread = false)
        {
            if(CacheMeshBatchStateBuckets.Count() == 0) { return; }

            if (bCallThread)
            {
                FHashmapValueToArrayJob<int, FMeshBatch> HashmapToArrayTask = new FHashmapValueToArrayJob<int, FMeshBatch>();
                {
                    HashmapToArrayTask.Array = MeshBatchArray;
                    HashmapToArrayTask.Hashmap = CacheMeshBatchStateBuckets;
                }
                HashmapToArrayTask.Run();
            } else {
                FHashmapValueToArrayParallelJob<int, FMeshBatch> HashmapToArrayTask = new FHashmapValueToArrayParallelJob<int, FMeshBatch>();
                {
                    HashmapToArrayTask.Array = MeshBatchArray;
                    HashmapToArrayTask.Hashmap = CacheMeshBatchStateBuckets;
                }
                GatherJobRef = HashmapToArrayTask.Schedule(MeshBatchArray.Length, 256);
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
            CacheMeshBatchStateBuckets.Clear();
            CacheMeshBatchStateBuckets.Dispose();
        }
    }
}
