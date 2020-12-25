using Unity.Jobs;
using Unity.Collections;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    //[Serializable]
    public class FMeshBatchCollector
    {
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public FMeshBatchCollector() { }

        public void Initializ()
        {
            CacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void GatherMeshBatch(NativeArray<FMeshBatch> MeshBatchArray)
        {
            if(CacheMeshBatchStateBuckets.Count() == 0) { return; }

            ConvertMeshBatchStateBucketsToArray ConvertToArrayTask = new ConvertMeshBatchStateBucketsToArray();
            {
                ConvertToArrayTask.StaticMeshBatchList = MeshBatchArray;
                ConvertToArrayTask.CacheMeshBatchStateBuckets = CacheMeshBatchStateBuckets;
            }
            ConvertToArrayTask.Run();
        }

        public bool StaticListAvalible()
        {
            return CacheMeshBatchStateBuckets.IsCreated;
        }

        public void AddStaticMeshBatch(in FMeshBatch MeshBatch, in int AddKey)
        {
            CacheMeshBatchStateBuckets.Add(AddKey, MeshBatch);
        }

        public void UpdateStaticMeshBatch(in FMeshBatch MeshBatch, in int UpdateKey)
        {
            CacheMeshBatchStateBuckets[UpdateKey] = MeshBatch;
        }

        public void RemoveStaticMeshBatch(in int RemoveKey)
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
