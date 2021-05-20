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

        public void GetMeshBatch(in NativeArray<FTerrainSection> TerrainSections)
        {
            if (!terrainBatchs.IsCreated) { return; }

            for (int i = 0; i < TerrainSections.Length; ++i)
            {
                FTerrainSection TerrainSection = TerrainSections[i];

                FTerrainBatch TerrainBatch;
                TerrainBatch.NumQuad = TerrainSection.numQuad;
                TerrainBatch.LODIndex = TerrainSection.lodIndex;
                TerrainBatch.BoundingBox = TerrainSection.boundBox;
                TerrainBatch.PivotPosition = TerrainSection.pivotPos;
                TerrainBatch.FractionLOD = TerrainSection.fractionLOD;
                TerrainBatch.NeighborFractionLOD = new float4(1, 1, 1, 1);

                terrainBatchs[i] = TerrainBatch;
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
