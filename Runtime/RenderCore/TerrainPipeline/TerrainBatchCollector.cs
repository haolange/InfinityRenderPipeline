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
        public void Initializ(in int Length)
        {
            if (TerrainBatchs.IsCreated == false)
            {
                TerrainBatchs = new NativeArray<FTerrainBatch>(Length, Allocator.TempJob);
            }
        }

        public void GetMeshBatch(in NativeArray<FTerrainSection> TerrainSections)
        {
            if (TerrainBatchs.IsCreated == false) { return; }

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
        }

        public void Release()
        {
            if (TerrainBatchs.IsCreated == true)
            {
                TerrainBatchs.Dispose();
            }
        }
    }
}
