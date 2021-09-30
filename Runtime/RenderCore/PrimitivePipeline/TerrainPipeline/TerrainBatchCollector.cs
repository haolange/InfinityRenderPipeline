using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public class FTerrainBatchCollector
    {
        public NativeArray<FTerrainBatch> terrainBatchs;

        public FTerrainBatchCollector() 
        { 

        }

        public void Initializ(in int Length)
        {
            if (!terrainBatchs.IsCreated)
            {
                terrainBatchs = new NativeArray<FTerrainBatch>(Length, Allocator.TempJob);
            }
        }

        public void GetMeshBatch(in NativeArray<FTerrainSection> terrainSections)
        {
            if (!terrainBatchs.IsCreated) { return; }

            for (int i = 0; i < terrainSections.Length; ++i)
            {
                FTerrainSection terrainSection = terrainSections[i];

                FTerrainBatch terrainBatch;
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
