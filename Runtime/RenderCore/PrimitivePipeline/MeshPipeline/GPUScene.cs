using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class GPUScene
    {
        internal FBufferRef bufferRef;
        internal int count
        {
            get
            {
                return m_MeshBatchCollector.count;
            }
        }
        internal NativeArray<float4x4> meshMatrixs
        {
            get
            {
                return m_MeshBatchCollector.cacheMatrixs;
            }
        }
        internal NativeArray<MeshElement> meshElements
        {
            get
            {
                return m_MeshBatchCollector.cacheMeshElements;
            }
        }
        
        private bool m_IsUpdate = true;
        private ResourcePool m_ResourcePool;
        private ProfilingSampler m_ProfileSampler;
        private MeshBatchCollector m_MeshBatchCollector;

        public GPUScene(ResourcePool resourcePool, MeshBatchCollector meshBatchCollector)
        {
            m_ResourcePool = resourcePool;
            m_ProfileSampler = new ProfilingSampler("UpdateGPUSccene");
            m_MeshBatchCollector = meshBatchCollector;
        }

        public void Update(in bool block = false)
        {
            if(block) { return; }

            if(m_MeshBatchCollector.cacheMatrixs.IsCreated)
            {
                using (new ProfilingScope(m_ProfileSampler))
                {
                    bufferRef = m_ResourcePool.GetBuffer(new BufferDescriptor(10000, Marshal.SizeOf(typeof(float4x4))));
                    //Debug.Log(m_MeshBatchCollector.count);
                    if(m_IsUpdate)
                    {
                        m_IsUpdate = false;
                        bufferRef.buffer.SetData(m_MeshBatchCollector.cacheMatrixs, 0, 0, m_MeshBatchCollector.count);
                        //cmdBuffer.SetBufferData(bufferRef.buffer, m_MeshBatchCollector.cacheMatrixs, 0, 0, m_MeshBatchCollector.count);
                    }
                }
            }
        }

        public void Clear()
        {
            if (m_MeshBatchCollector.cacheMatrixs.IsCreated)
            {
                m_ResourcePool.ReleaseBuffer(bufferRef);
            }
        }
    }
}
