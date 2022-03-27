using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    public class FMeshBatchCollector
    {
        private int m_Index;
        public int count
        {
            get
            {
                return m_Index + 1;
            }
        }
        public NativeArray<float4x4> cacheMatrixs;
        public NativeArray<FMeshElement> cacheMeshElements;

        public void Initializ()
        {
            m_Index = -1;
            cacheMatrixs = new NativeArray<float4x4>(10000, Allocator.Persistent);
            cacheMeshElements = new NativeArray<FMeshElement>(10000, Allocator.Persistent);
        }

        public int AddMeshBatch(in FMeshElement meshElement, in float4x4 matrix)
        {
            if(m_Index > 10000 - 1){ return 0; }

            ++m_Index;
            cacheMatrixs[m_Index] = matrix;
            cacheMeshElements[m_Index] = meshElement;
            return m_Index;
        }

        public void UpdateMeshBatch(in int index, in FMeshElement meshElement, in float4x4 matrix)
        {
            cacheMatrixs[index] = matrix;
            cacheMeshElements[index] = meshElement;
        }

        public void RemoveMeshBatch(in int key)
        {

        }

        public void Release()
        {
            cacheMatrixs.Dispose();
            cacheMeshElements.Dispose();
        }
    }
}
