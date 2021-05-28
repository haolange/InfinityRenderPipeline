using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [BurstCompile]
    public struct FSectionLODDataUpdateJob : IJob
    {
        [ReadOnly]
        public int numQuad;

        [ReadOnly]
        public float3 viewOringin;

        [ReadOnly]
        public float4x4 matrix_Proj;

        public NativeArray<FTerrainSection> nativeSections;

        public void Execute()
        {
            for (int i = 0; i < nativeSections.Length; ++i)
            {
                FTerrainSection section = nativeSections[i];
                float screenSize = TerrainUtility.ComputeBoundsScreenRadiusSquared(TerrainUtility.GetBoundRadius(section.boundBox), section.boundBox.center, viewOringin, matrix_Proj);
                section.lodIndex = math.min(6, TerrainUtility.GetLODFromScreenSize(section.lodSetting, screenSize, 1, out section.fractionLOD));
                section.fractionLOD = math.min(5, section.fractionLOD);
                section.numQuad = math.clamp(numQuad >> section.lodIndex, 1, numQuad);

                nativeSections[i] = section;
            }
        }
    }

    [BurstCompile]
    public struct FSectionLODDataParallelUpdateJob : IJobParallelFor
    {
        [ReadOnly]
        public int numQuad;

        [ReadOnly]
        public float3 viewOringin;

        [ReadOnly]
        public float4x4 matrix_Proj;

        public NativeArray<FTerrainSection> nativeSections;

        public void Execute(int index)
        {
            FTerrainSection section = nativeSections[index];
            float screenSize = TerrainUtility.ComputeBoundsScreenRadiusSquared(TerrainUtility.GetBoundRadius(section.boundBox), section.boundBox.center, viewOringin, matrix_Proj);
            section.lodIndex = math.min(6, TerrainUtility.GetLODFromScreenSize(section.lodSetting, screenSize, 1, out section.fractionLOD));
            section.fractionLOD = math.min(5, section.fractionLOD);
            section.numQuad = math.clamp(numQuad >> section.lodIndex, 1, numQuad);

            nativeSections[index] = section;
        }
    }
}
