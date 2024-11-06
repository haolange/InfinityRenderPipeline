using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public class TerrainElementCollector
    {
        public NativeArray<TerrainElement> terrainBatchs;

        public TerrainElementCollector() 
        { 

        }

        public void Initializ(in int Length)
        {
            if (!terrainBatchs.IsCreated)
            {
                terrainBatchs = new NativeArray<TerrainElement>(Length, Allocator.TempJob);
            }
        }

        public void GetMeshBatch(in NativeArray<TerrainSection> terrainSections)
        {
            if (!terrainBatchs.IsCreated) { return; }

            for (int i = 0; i < terrainSections.Length; ++i)
            {
                TerrainSection terrainSection = terrainSections[i];

                TerrainElement terrainBatch;
                terrainBatch.numQuad = terrainSection.numQuad;
                terrainBatch.lODIndex = terrainSection.lodIndex;
                terrainBatch.pivotPos = terrainSection.pivotPos;
                terrainBatch.boundBox = terrainSection.boundBox;
                terrainBatch.fractionLOD = terrainSection.fractionLOD;
                terrainBatchs[i] = terrainBatch;
            }
        }

        public void Release()
        {
            if (terrainBatchs.IsCreated)
            {
                terrainBatchs.Dispose();
            }
        }
    }
}
