using Unity.Jobs;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace InfinityTech.Rendering.MeshPipeline
{
    //[Serializable]
    public unsafe class FMeshBatchCollector
    {
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public FMeshBatchCollector() 
        { 

        }

        public void Initializ()
        {
            CacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void GatherMeshBatch(NativeArray<FMeshBatch> MeshBatchs, in bool bParallel = true)
        {
            if(!CacheMeshBatchStateBuckets.IsCreated) { return; }
            
            if(CacheMeshBatchStateBuckets.Count() == 0) { return; }

            if (!bParallel)
            {
                FHashmapGatherValueJob<int, FMeshBatch> MeshBatchGatherJob = new FHashmapGatherValueJob<int, FMeshBatch>();
                {
                    MeshBatchGatherJob.Array = MeshBatchs;
                    MeshBatchGatherJob.Hashmap = CacheMeshBatchStateBuckets;
                }
                MeshBatchGatherJob.Run();
            } else {
                NativeList<int> MeshBatchMapIndexs = new NativeList<int>(MeshBatchs.Length, Allocator.TempJob);
                int* BucketNext = (int*)CacheMeshBatchStateBuckets.m_HashMapData.m_Buffer->next;
                int* BucketArray = (int*)CacheMeshBatchStateBuckets.m_HashMapData.m_Buffer->buckets;
                byte* HashmapValues = CacheMeshBatchStateBuckets.m_HashMapData.m_Buffer->values;

                FMeshBatchCounterJob MeshBatchCounterJob = new FMeshBatchCounterJob();
                {
                    MeshBatchCounterJob.BucketNext = BucketNext;
                    MeshBatchCounterJob.BucketArray = BucketArray;
                    MeshBatchCounterJob.Count = MeshBatchs.Length;
                    MeshBatchCounterJob.MeshBatchMapIndexs = MeshBatchMapIndexs;
                    MeshBatchCounterJob.Length = CacheMeshBatchStateBuckets.m_HashMapData.m_Buffer->bucketCapacityMask;
                }
                MeshBatchCounterJob.Run();

                FMeshBatchGatherJob MeshBatchParallelGatherJob = new FMeshBatchGatherJob();
                {
                    MeshBatchParallelGatherJob.MeshBatchs = MeshBatchs;
                    MeshBatchParallelGatherJob.HashmapValues = HashmapValues;
                    MeshBatchParallelGatherJob.MeshBatchMapIndexs = MeshBatchMapIndexs;
                }
                MeshBatchParallelGatherJob.Schedule(MeshBatchMapIndexs.Length, 256).Complete();

                /*for (int i = 0; i <= CacheMeshBatchStateBuckets.m_HashMapData.m_Buffer->bucketCapacityMask; ++i)
                {
                    ref int count = ref Count[0];

                    if (count < MeshBatchs.Length)
                    {
                        int bucket = BucketArray[i];

                        while (bucket != -1)
                        {
                            MeshBatchs[count] = UnsafeUtility.ReadArrayElement<FMeshBatch>(HashmapValues, bucket);
                            bucket = BucketNext[bucket];

                            Interlocked.Increment(ref count);
                        }
                    }
                }*/
                MeshBatchMapIndexs.Dispose();
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
