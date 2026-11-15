using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public class TerrainElementCollector
    {
        public NativeArray<TerrainElement> terrainBatches;

        public TerrainElementCollector() 
        { 

        }

        public void Initialize(in int Length)
        {
            if (!terrainBatches.IsCreated)
            {
                terrainBatches = new NativeArray<TerrainElement>(Length, Allocator.TempJob);
            }
        }

        public void GetMeshBatch(in NativeArray<TerrainSection> terrainSections)
        {
            if (!terrainBatches.IsCreated)
            {
                return;
            }

            for (int i = 0; i < terrainSections.Length; ++i)
            {
                TerrainSection terrainSection = terrainSections[i];

                TerrainElement terrainBatch;
                terrainBatch.numQuad = terrainSection.numQuad;
                terrainBatch.lODIndex = terrainSection.lodIndex;
                terrainBatch.pivotPos = terrainSection.pivotPos;
                terrainBatch.boundBox = terrainSection.boundBox;
                terrainBatch.fractionLOD = terrainSection.fractionLOD;
                terrainBatches[i] = terrainBatch;
            }
        }

        public void Release()
        {
            if (terrainBatches.IsCreated)
            {
                terrainBatches.Dispose();
            }
        }
    }
}
