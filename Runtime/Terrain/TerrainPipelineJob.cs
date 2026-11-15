using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [BurstCompile]
    public struct TerrainProcessLODJob : IJob
    {
        [ReadOnly]
        public int numQuad;

        [ReadOnly]
        public float3 viewOrigin;

        [ReadOnly]
        public float4x4 projectionMatrix;

        public NativeArray<TerrainSection> nativeSections;

        public void Execute()
        {
            for (int i = 0; i < nativeSections.Length; ++i)
            {
                TerrainSection section = nativeSections[i];
                float screenSize = TerrainUtility.ComputeBoundsScreenRadiusSquared(TerrainUtility.GetBoundRadius(section.boundBox), section.boundBox.center, viewOrigin, projectionMatrix);
                section.lodIndex = math.min(6, TerrainUtility.GetLODFromScreenSize(section.lodSetting, screenSize, 1, out section.fractionLOD));
                section.fractionLOD = math.min(5, section.fractionLOD);
                section.numQuad = math.clamp(numQuad >> section.lodIndex, 1, numQuad);

                nativeSections[i] = section;
            }
        }
    }
}
