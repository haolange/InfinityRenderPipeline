using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [Serializable]
    public class FTerrainSector
    {
        public int[] MaxLODs;
        public FBound BoundBox;
        public FTerrainSection[] Sections;
        public NativeArray<FTerrainSection> NativeSections;


        public FTerrainSector(in int SectorSize, in int NumSection, in int SectionSize, in float3 SectorPivotPosition, FAABB SectorBound)
        {
            int SectorSize_Half = SectorSize / 2;
            int SectionSize_Half = SectionSize / 2;

            MaxLODs = new int[NumSection * NumSection];
            Sections = new FTerrainSection[NumSection * NumSection];
            BoundBox = new FBound(new float3(SectorPivotPosition.x + SectorSize_Half, SectorPivotPosition.y + (SectorBound.size.y / 2), SectorPivotPosition.z + SectorSize_Half), SectorBound.size * 0.5f);

            for (int SectorSizeX = 0; SectorSizeX <= NumSection - 1; SectorSizeX++)
            {
                for (int SectorSizeY = 0; SectorSizeY <= NumSection - 1; SectorSizeY++)
                {
                    int SectionIndex = (SectorSizeX * NumSection) + SectorSizeY;
                    float3 SectionPivotPosition = SectorPivotPosition + new float3(SectionSize * SectorSizeX, 0, SectionSize * SectorSizeY);
                    float3 SectionCenterPosition = SectionPivotPosition + new float3(SectionSize_Half, 0, SectionSize_Half);

                    Sections[SectionIndex] = new FTerrainSection();
                    Sections[SectionIndex].PivotPosition = SectionPivotPosition;
                    Sections[SectionIndex].CenterPosition = SectionCenterPosition;
                    Sections[SectionIndex].BoundingBox = new FAABB(SectionCenterPosition, new float3(SectionSize, 1, SectionSize));
                }
            }

            InitializLOD(7);
        }

        public void BuildNativeCollection()
        {
            NativeSections = new NativeArray<FTerrainSection>(Sections.Length, Allocator.Persistent);

            for (int i = 0; i < Sections.Length; i++)
            {
                NativeSections[i] = Sections[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            NativeSections.Dispose();
        }

        private void InitializLOD(in int MaxLOD)
        {
            for (int i = 0; i < MaxLODs.Length; i++)
            {
                MaxLODs[i] = MaxLOD;
            }
        }

        public void BuildLODData(in float LOD0ScreenSize, in float LOD0Distribution, in float LODDistribution)
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                ref int MaxLOD = ref MaxLODs[i];
                ref FSectionLODData LODSetting = ref Sections[i].LODSetting;

                float CurrentScreenSizeRatio = LOD0ScreenSize;
                float[] LODScreenRatioSquared = new float[MaxLOD];
                float ScreenSizeRatioDivider = math.max(LOD0Distribution, 1.01f);
                LODScreenRatioSquared[0] = CurrentScreenSizeRatio * CurrentScreenSizeRatio;

                // LOD 0 handling
                LODSetting.LOD0ScreenSizeSquared = CurrentScreenSizeRatio * CurrentScreenSizeRatio;
                CurrentScreenSizeRatio /= ScreenSizeRatioDivider;
                LODSetting.LOD1ScreenSizeSquared = CurrentScreenSizeRatio * CurrentScreenSizeRatio;
                ScreenSizeRatioDivider = math.max(LODDistribution, 1.01f);
                LODSetting.LODOnePlusDistributionScalarSquared = ScreenSizeRatioDivider * ScreenSizeRatioDivider;

                // Other LODs
                for (int j = 1; j < MaxLOD; ++j) // This should ALWAYS be calculated from the section size, not user MaxLOD override
                {
                    LODScreenRatioSquared[j] = CurrentScreenSizeRatio * CurrentScreenSizeRatio;
                    CurrentScreenSizeRatio /= ScreenSizeRatioDivider;
                }

                // Clamp ForcedLOD to the valid range and then apply
                LODSetting.LastLODIndex = MaxLOD;
                LODSetting.LastLODScreenSizeSquared = LODScreenRatioSquared[MaxLOD - 1];
            }
        }

        public void UpdateLODData(in int NumQuad, in float3 ViewOringin, in float4x4 Matrix_Proj)
        {
            if(NativeSections.IsCreated == false) { return; }

            /*for (int i = 0; i < NativeSections.Length; ++i)
            {
                FTerrainSection Section = NativeSections[i];
                float ScreenSize = TerrainUtility.ComputeBoundsScreenRadiusSquared(TerrainUtility.GetBoundRadius(Section.BoundingBox), Section.BoundingBox.center, ViewOringin, Matrix_Proj);
                Section.LODIndex = math.min(6, TerrainUtility.GetLODFromScreenSize(Section.LODSetting, ScreenSize, 1, out Section.FractionLOD));
                Section.FractionLOD = math.min(5, Section.FractionLOD);
                Section.NumQuad = math.clamp(NumQuad >> Section.LODIndex, 1, NumQuad);

                NativeSections[i] = Section;
            }*/

            FSectionLODDataUpdateJob SectionLODDataUpdateJob = new FSectionLODDataUpdateJob();
            {
                SectionLODDataUpdateJob.NumQuad = NumQuad;
                SectionLODDataUpdateJob.ViewOringin = ViewOringin;
                SectionLODDataUpdateJob.Matrix_Proj = Matrix_Proj;
                SectionLODDataUpdateJob.NativeSections = NativeSections;
            }
            SectionLODDataUpdateJob.Run();

            /*FSectionLODDataParallelUpdateJob SectionLODDataParallelUpdateJob = new FSectionLODDataParallelUpdateJob();
            {
                SectionLODDataParallelUpdateJob.NumQuad = NumQuad;
                SectionLODDataParallelUpdateJob.ViewOringin = ViewOringin;
                SectionLODDataParallelUpdateJob.Matrix_Proj = Matrix_Proj;
                SectionLODDataParallelUpdateJob.NativeSections = NativeSections;
            }
            SectionLODDataParallelUpdateJob.Schedule(NativeSections.Length, 32).Complete();*/
        }

#if UNITY_EDITOR
        public void DrawBound(in bool LODColor = false)
        {
            Geometry.DrawBound(BoundBox, Color.white);

            for (int i = 0; i < NativeSections.Length; i++)
            {
                FTerrainSection Section = NativeSections[i];

                if (!LODColor)
                {
                    Geometry.DrawBound(Section.BoundingBox, Color.yellow);
                } else {
                    Geometry.DrawBound(Section.BoundingBox, TerrainUtility.LODColor[Section.LODIndex]);
                }
            }
        }

        public void BuildBounds(int SectorSize, int SectionSize, float ScaleY, float3 TerrianPosition, Texture2D Heightmap)
        {
            int SectorSize_Half = SectorSize / 2;

            for (int i = 0; i < Sections.Length; i++)
            {
                ref FTerrainSection Section = ref Sections[i];

                float2 PositionScale = new float2(TerrianPosition.x, TerrianPosition.z) + new float2(SectorSize_Half, SectorSize_Half);
                float2 RectUV = new float2((Section.PivotPosition.x - PositionScale.x) + SectorSize_Half, (Section.PivotPosition.z - PositionScale.y) + SectorSize_Half);

                int ReverseScale = SectorSize - SectionSize;
                Color[] HeightValues = Heightmap.GetPixels(Mathf.FloorToInt(RectUV.x), ReverseScale - Mathf.FloorToInt(RectUV.y), Mathf.FloorToInt(SectionSize), Mathf.FloorToInt(SectionSize), 0);

                float MinHeight = HeightValues[0].r;
                float MaxHeight = HeightValues[0].r;
                for (int j = 0; j < HeightValues.Length; j++)
                {
                    if (MinHeight < HeightValues[j].r)
                    {
                        MinHeight = HeightValues[j].r;
                    }

                    if (MaxHeight > HeightValues[j].r)
                    {
                        MaxHeight = HeightValues[j].r;
                    }
                }

                float PosY = ((Section.CenterPosition.y + MinHeight * ScaleY) + (Section.CenterPosition.y + MaxHeight * ScaleY)) * 0.5f;
                float SizeY = ((Section.CenterPosition.y + MinHeight * ScaleY) - (Section.CenterPosition.y + MaxHeight * ScaleY));
                float3 NewBoundCenter = new float3(Section.CenterPosition.x, PosY, Section.CenterPosition.z);
                Section.BoundingBox = new FAABB(NewBoundCenter, new float3(SectionSize, SizeY, SectionSize));
            }
        }
#endif
    }
}
