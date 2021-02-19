using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.TerrainPipeline
{
    //[Serializable]
    public class FTerrainBatchCollector
    {
        public NativeArray<FTerrainBatch> TerrainBatchs;

        public FTerrainBatchCollector() 
        { 

        }

        public void GetMeshBatch(in NativeArray<FTerrainSection> TerrainSections)
        {
            TerrainBatchs = new NativeArray<FTerrainBatch>(TerrainSections.Length, Allocator.TempJob);

            for (int i = 0; i < TerrainSections.Length; ++i)
            {
                FTerrainSection TerrainSection = TerrainSections[i];

                FTerrainBatch TerrainBatch;
                TerrainBatch.NumQuad = TerrainSection.NumQuad;
                TerrainBatch.LODIndex = TerrainSection.LODIndex;
                TerrainBatch.FractionLOD = TerrainSection.FractionLOD;
                TerrainBatch.BoundingBox = TerrainSection.BoundingBox;
                TerrainBatch.PivotPosition = TerrainSection.PivotPosition;
                TerrainBatch.NeighborFractionLOD = new float4(1, 1, 1, 1);

                TerrainBatchs[i] = TerrainBatch;
            }

            TerrainBatchs.Dispose();
        }
    }
}
