using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [BurstCompile]
    public struct FSectionLODDataUpdateJob : IJob
    {
        [ReadOnly]
        public int NumQuad;

        [ReadOnly]
        public float3 ViewOringin;

        [ReadOnly]
        public float4x4 Matrix_Proj;

        public NativeArray<FTerrainSection> NativeSections;


        public void Execute()
        {
            for (int i = 0; i < NativeSections.Length; ++i)
            {
                FTerrainSection Section = NativeSections[i];
                float ScreenSize = TerrainUtility.ComputeBoundsScreenRadiusSquared(TerrainUtility.GetBoundRadius(Section.BoundBox), Section.BoundBox.center, ViewOringin, Matrix_Proj);
                Section.LODIndex = math.min(6, TerrainUtility.GetLODFromScreenSize(Section.LODSetting, ScreenSize, 1, out Section.FractionLOD));
                Section.FractionLOD = math.min(5, Section.FractionLOD);
                Section.NumQuad = math.clamp(NumQuad >> Section.LODIndex, 1, NumQuad);

                NativeSections[i] = Section;
            }
        }
    }

    [BurstCompile]
    public struct FSectionLODDataParallelUpdateJob : IJobParallelFor
    {
        [ReadOnly]
        public int NumQuad;

        [ReadOnly]
        public float3 ViewOringin;

        [ReadOnly]
        public float4x4 Matrix_Proj;

        public NativeArray<FTerrainSection> NativeSections;


        public void Execute(int i)
        {
            FTerrainSection Section = NativeSections[i];
            float ScreenSize = TerrainUtility.ComputeBoundsScreenRadiusSquared(TerrainUtility.GetBoundRadius(Section.BoundBox), Section.BoundBox.center, ViewOringin, Matrix_Proj);
            Section.LODIndex = math.min(6, TerrainUtility.GetLODFromScreenSize(Section.LODSetting, ScreenSize, 1, out Section.FractionLOD));
            Section.FractionLOD = math.min(5, Section.FractionLOD);
            Section.NumQuad = math.clamp(NumQuad >> Section.LODIndex, 1, NumQuad);

            NativeSections[i] = Section;
        }
    }
}
