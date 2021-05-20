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
        private bool m_IsUpdate = true;
        public BufferRef bufferRef;
        public NativeArray<FMeshBatch> meshBatchs;
        private FMeshBatchCollector m_MeshBatchCollector;

        public void Gather(FMeshBatchCollector meshBatchCollector, FResourceFactory resourceFactory, CommandBuffer cmdBuffer, in int methdo = 0, in bool block = false)
        {
            if(block) { return; }

            m_MeshBatchCollector = meshBatchCollector;
            if(meshBatchCollector.cacheMeshBatchStateBuckets.IsCreated)
            {
                meshBatchs = new NativeArray<FMeshBatch>(meshBatchCollector.cacheMeshBatchStateBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                meshBatchCollector.GatherMeshBatch(ref meshBatchs, methdo);

                bufferRef = resourceFactory.AllocateBuffer(new BufferDescription(10000, Marshal.SizeOf(typeof(FMeshBatch))));

                if (m_IsUpdate)
                {
                    m_IsUpdate = false;
                    cmdBuffer.SetBufferData(bufferRef.buffer, meshBatchs);
                }
            }
        }

        public void Release(FResourceFactory resourceFactory)
        {
            if (meshBatchs.IsCreated)
            {
                meshBatchs.Dispose();
                resourceFactory.ReleaseBuffer(bufferRef);
            }
        }
    }
}
