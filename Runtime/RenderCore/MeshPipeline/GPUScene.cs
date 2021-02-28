using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FGPUScene
    {
        public BufferRef BufferHandle;
        public NativeArray<FMeshBatch> MeshBatchs;
        protected FMeshBatchCollector MeshBatchCollector;


        public void Gather(FMeshBatchCollector InMeshBatchCollector, FResourceFactory ResourcePool, CommandBuffer CmdBuffer, in int Methdo = 0, in bool Block = false)
        {
            if(Block) { return; }

            MeshBatchCollector = InMeshBatchCollector;

            if(MeshBatchCollector.CacheMeshBatchStateBuckets.IsCreated)
            {
                MeshBatchs = new NativeArray<FMeshBatch>(MeshBatchCollector.CacheMeshBatchStateBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                MeshBatchCollector.GatherMeshBatch(MeshBatchs, Methdo);

                BufferHandle = ResourcePool.AllocateBuffer(new BufferDescription(64000, Marshal.SizeOf(typeof(FMeshBatch))));
                CmdBuffer.SetComputeBufferData(BufferHandle.Buffer, MeshBatchs);
            }
        }

        public void Release(FResourceFactory ResourcePool)
        {
            if (MeshBatchs.IsCreated)
            {
                MeshBatchs.Dispose();
                ResourcePool.ReleaseBuffer(BufferHandle);
            }
        }
    }
}
