using UnityEngine;
using Unity.Collections;

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
        public NativeArray<FMeshElement> cacheMeshElements;

        public void Initializ()
        {
            m_Index = -1;
            cacheMeshElements = new NativeArray<FMeshElement>(10000, Allocator.Persistent);
        }

        public int AddMeshBatch(in FMeshElement meshElement)
        {
            if(m_Index > 10000 - 1){ return 0; }

            m_Index += 1;
            cacheMeshElements[m_Index] = meshElement;
            return m_Index;
        }

        public void UpdateMeshBatch(in int index, in FMeshElement meshElement)
        {
            cacheMeshElements[index] = meshElement;
        }

        public void RemoveMeshBatch(in int key)
        {

        }

        public void Release()
        {
            cacheMeshElements.Dispose();
        }
    }
}
