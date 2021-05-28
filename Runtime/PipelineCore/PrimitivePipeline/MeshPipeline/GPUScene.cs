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
        public NativeArray<FMeshElement> meshElements;
        private FMeshBatchCollector m_MeshBatchCollector;

        public void Gather(FMeshBatchCollector meshBatchCollector, FResourceFactory resourceFactory, CommandBuffer cmdBuffer, in int methdo = 0, in bool block = false)
        {
            if(block) { return; }

            m_MeshBatchCollector = meshBatchCollector;
            if(meshBatchCollector.cacheMeshElementsBuckets.IsCreated)
            {
                meshElements = new NativeArray<FMeshElement>(meshBatchCollector.cacheMeshElementsBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                meshBatchCollector.GatherMeshBatch(ref meshElements, methdo);

                bufferRef = resourceFactory.AllocateBuffer(new BufferDescription(10000, Marshal.SizeOf(typeof(FMeshElement))));

                if (m_IsUpdate)
                {
                    m_IsUpdate = false;
                    cmdBuffer.SetBufferData(bufferRef.buffer, meshElements);
                }
            }
        }

        public void Release(FResourceFactory resourceFactory)
        {
            if (meshElements.IsCreated)
            {
                meshElements.Dispose();
                resourceFactory.ReleaseBuffer(bufferRef);
            }
        }
    }
}
