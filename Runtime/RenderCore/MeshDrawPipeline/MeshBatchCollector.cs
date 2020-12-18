using Unity.Jobs;
using Unity.Collections;
using System.Runtime.CompilerServices;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    //[Serializable]
    public class FMeshBatchCollector
    {
        public NativeList<FMeshBatch> DynamicMeshBatchList;
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public FMeshBatchCollector() { }

        public void Initializ()
        {
            DynamicMeshBatchList = new NativeList<FMeshBatch>(1000, Allocator.Persistent);
            CacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void CopyStaticToDynamic()
        {
            if(CacheMeshBatchStateBuckets.Count() == 0) { return; }

            //Copy Cache MeshBatch StateMap to NativeArray
            NativeArray<FMeshBatch> StaticMeshBatchList = new NativeArray<FMeshBatch>(CacheMeshBatchStateBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            ConvertHashMapToArray ConvertHashMapToArrayTask = new ConvertHashMapToArray();
            {
                ConvertHashMapToArrayTask.StaticMeshBatchList = StaticMeshBatchList;
                ConvertHashMapToArrayTask.CacheMeshBatchStateBuckets = CacheMeshBatchStateBuckets;
            }
            ConvertHashMapToArrayTask.Run();

            //Resize DynamicMeshBatchSize to StaticSize
            if(DynamicMeshBatchList.Length < StaticMeshBatchList.Length) {
                DynamicMeshBatchList.Resize(StaticMeshBatchList.Length + 2000, NativeArrayOptions.ClearMemory);
            }

            //Parallel copy StaticData to DynamicData
            CopyStaticMeshBatch CopyTask = new CopyStaticMeshBatch();
            {
                CopyTask.StaticMeshBatchList = StaticMeshBatchList;
                CopyTask.DynamicMeshBatchList = DynamicMeshBatchList.AsDeferredJobArray();
            }
            CopyTask.Schedule(StaticMeshBatchList.Length, 256).Complete();
            StaticMeshBatchList.Dispose();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetDynamicCollector()
        {
            DynamicMeshBatchList.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddDynamicMeshBatch(in FMeshBatch MeshBatch)
        {
            DynamicMeshBatchList.Add(MeshBatch);
        }

        public NativeList<FMeshBatch> GetMeshBatchList()
        {
            return DynamicMeshBatchList;
        }

        public void Reset()
        {
            DynamicMeshBatchList.Clear();
            CacheMeshBatchStateBuckets.Clear();
        }

        public void Release()
        {
            DynamicMeshBatchList.Clear();
            DynamicMeshBatchList.Dispose();
            CacheMeshBatchStateBuckets.Clear();
            CacheMeshBatchStateBuckets.Dispose();
        }
    }
}
