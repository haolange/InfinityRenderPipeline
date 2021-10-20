using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FGPUScene
    {
        private bool m_IsUpdate = true;
        public FBufferRef bufferRef;
        public int count
        {
            get
            {
                return m_MeshBatchCollector.count;
            }
        }
        public NativeArray<FMeshElement> meshElements
        {
            get
            {
                return m_MeshBatchCollector.cacheMeshElements;
            }
        }
        private FMeshBatchCollector m_MeshBatchCollector;
        
        public void Update(FMeshBatchCollector meshBatchCollector, FResourcePool resourcePool, CommandBuffer cmdBuffer, in bool block = false)
        {
            if(block) { return; }

            m_MeshBatchCollector = meshBatchCollector;
            if(meshBatchCollector.cacheMeshElements.IsCreated)
            {
                bufferRef = resourcePool.AllocateBuffer(new FBufferDescription(10000, Marshal.SizeOf(typeof(FMeshElement))));

                if (m_IsUpdate)
                {
                    m_IsUpdate = false;
                    cmdBuffer.SetBufferData(bufferRef.buffer, meshBatchCollector.cacheMeshElements, 0, 0, m_MeshBatchCollector.count);
                }
            }
        }

        public void Release(FResourcePool resourcePool)
        {
            if (m_MeshBatchCollector.cacheMeshElements.IsCreated)
            {
                resourcePool.ReleaseBuffer(bufferRef);
            }
        }
    }
}
