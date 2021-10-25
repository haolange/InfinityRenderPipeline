using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FGPUScene
    {
        internal FBufferRef bufferRef;
        internal int count
        {
            get
            {
                return m_MeshBatchCollector.count;
            }
        }
        internal NativeArray<FMeshElement> meshElements
        {
            get
            {
                return m_MeshBatchCollector.cacheMeshElements;
            }
        }
        
        private bool m_IsUpdate = true;
        private FResourcePool m_ResourcePool;
        private FMeshBatchCollector m_MeshBatchCollector;

        public FGPUScene(FResourcePool resourcePool, FMeshBatchCollector meshBatchCollector)
        {
            m_ResourcePool = resourcePool;
            m_MeshBatchCollector = meshBatchCollector;
        }

        public void Update(CommandBuffer cmdBuffer, in bool block = false)
        {
            if(block) { return; }

            if(m_MeshBatchCollector.cacheMeshElements.IsCreated)
            {
                bufferRef = m_ResourcePool.AllocateBuffer(new FBufferDescription(10000, Marshal.SizeOf(typeof(FMeshElement))));

                if (m_IsUpdate)
                {
                    m_IsUpdate = false;
                    cmdBuffer.SetBufferData(bufferRef.buffer, m_MeshBatchCollector.cacheMeshElements, 0, 0, m_MeshBatchCollector.count);
                }
            }
        }

        public void Clear()
        {
            if (m_MeshBatchCollector.cacheMeshElements.IsCreated)
            {
                m_ResourcePool.ReleaseBuffer(bufferRef);
            }
        }
    }
}
