using Unity.Jobs;
using Unity.Collections;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FMeshBatchCollector
    {
        public NativeHashMap<int, FMeshElement> cacheMeshElementsBuckets;

        public FMeshBatchCollector() 
        { 

        }

        public void Initializ()
        {
            cacheMeshElementsBuckets = new NativeHashMap<int, FMeshElement>(10000, Allocator.Persistent);
        }

        public void GatherMeshBatch(ref NativeArray<FMeshElement> meshElements, in int methdo = 0)
        {
            if(!cacheMeshElementsBuckets.IsCreated) { return; }
            
            if(cacheMeshElementsBuckets.Count() == 0) { return; }

            if (methdo == 0)
            {
                FHashmapGatherValueJob<int, FMeshElement> MeshBatchGatherJob = new FHashmapGatherValueJob<int, FMeshElement>();
                MeshBatchGatherJob.dscArray = meshElements;
                MeshBatchGatherJob.srcMap = cacheMeshElementsBuckets;
                MeshBatchGatherJob.Run();
            } else {
                FHashmapParallelGatherValueJob<int, FMeshElement> MeshBatchGatherJob = new FHashmapParallelGatherValueJob<int, FMeshElement>();
                MeshBatchGatherJob.dscArray = meshElements;
                MeshBatchGatherJob.srcMap = cacheMeshElementsBuckets;
                MeshBatchGatherJob.Schedule(meshElements.Length, 256).Complete();
            }
        }

        public void AddMeshBatch(in FMeshElement meshElement, in int key)
        {
            cacheMeshElementsBuckets.TryAdd(key, meshElement);
        }

        public void UpdateMeshBatch(in FMeshElement meshElement, in int key)
        {
            cacheMeshElementsBuckets[key] = meshElement;
        }

        public void RemoveMeshBatch(in int key)
        {
            if(cacheMeshElementsBuckets.IsCreated == false) { return; }
            cacheMeshElementsBuckets.Remove(key);
        }

        public void Reset()
        {
            cacheMeshElementsBuckets.Clear();
        }

        public void Release()
        {
            cacheMeshElementsBuckets.Dispose();
        }
    }
}
