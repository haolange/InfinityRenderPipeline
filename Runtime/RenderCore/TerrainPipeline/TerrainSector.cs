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

        public FTerrainSector(in int SectorSize, in int NumSection, in int NumQuad, in float3 SectorPivotPosition, FAABB SectorBound)
        {
            int SectorSize_Half = SectorSize / 2;
            int SectionSize_Half = NumQuad / 2;

            MaxLODs = new int[NumSection * NumSection];
            Sections = new FTerrainSection[NumSection * NumSection];
            BoundBox = new FBound(new float3(SectorPivotPosition.x + SectorSize_Half, SectorPivotPosition.y + (SectorBound.size.y / 2), SectorPivotPosition.z + SectorSize_Half), SectorBound.size * 0.5f);

            for (int SectorSizeX = 0; SectorSizeX <= NumSection - 1; SectorSizeX++)
            {
                for (int SectorSizeY = 0; SectorSizeY <= NumSection - 1; SectorSizeY++)
                {
                    int SectionIndex = (SectorSizeX * NumSection) + SectorSizeY;
                    float3 SectionPivotPosition = SectorPivotPosition + new float3(NumQuad * SectorSizeX, 0, NumQuad * SectorSizeY);
                    float3 SectionCenterPosition = SectionPivotPosition + new float3(SectionSize_Half, 0, SectionSize_Half);

                    Sections[SectionIndex] = new FTerrainSection();
                    Sections[SectionIndex].PivotPosition = SectionPivotPosition;
                    Sections[SectionIndex].CenterPosition = SectionCenterPosition;
                    Sections[SectionIndex].BoundBox = new FAABB(SectionCenterPosition, new float3(NumQuad, 1, NumQuad));
                }
            }

            InitializLOD(7);
        }

        public void Initializ()
        {
            if (NativeSections.IsCreated == false)
            {
                NativeSections = new NativeArray<FTerrainSection>(Sections.Length, Allocator.Persistent);
            }
        }

        public void Release()
        {
            if (NativeSections.IsCreated == true)
            {
                NativeSections.Dispose();
            }
        }

        public void UpdateToNativeCollection()
        {
            if(NativeSections.IsCreated == true)
            {
                for (int i = 0; i < Sections.Length; i++)
                {
                    NativeSections[i] = Sections[i];
                }
            }
        }

        private void InitializLOD(in int MaxLOD)
        {
            for (int i = 0; i < MaxLODs.Length; i++)
            {
                MaxLODs[i] = MaxLOD;
            }
        }

        public void GenerateLODData(in float LOD0ScreenSize, in float LOD0Distribution, in float LODDistribution)
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
                for (int LOD_Index = 1; LOD_Index <= MaxLOD - 1; ++LOD_Index) // This should ALWAYS be calculated from the component size, not user MaxLOD override
                {
                    LODScreenRatioSquared[LOD_Index] = CurrentScreenSizeRatio * CurrentScreenSizeRatio;
                    CurrentScreenSizeRatio /= ScreenSizeRatioDivider;
                }

                // Clamp ForcedLOD to the valid range and then apply
                LODSetting.LastLODIndex = MaxLOD;
                LODSetting.LastLODScreenSizeSquared = LODScreenRatioSquared[MaxLOD - 1];
            }
        }

        public void UpdateLODData(in int NumQuad, in float3 ViewOringin, in float4x4 Matrix_Proj)
        {
            /*for (int i = 0; i < NativeSections.Length; ++i)
            {
                FTerrainSection Section = NativeSections[i];
                float ScreenSize = TerrainUtility.ComputeBoundsScreenRadiusSquared(TerrainUtility.GetBoundRadius(Section.BoundBox), Section.BoundBox.center, ViewOringin, Matrix_Proj);
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
                    Geometry.DrawBound(Section.BoundBox, Color.yellow);
                } else {
                    Geometry.DrawBound(Section.BoundBox, TerrainUtility.LODColor[Section.LODIndex]);
                }
            }
        }

        public void UpdateBounds(int NumQuad, int TerrainSize, float ScaleY, float3 TerrianPosition, Texture2D Heightmap)
        {
            int TerrainSize_Half = TerrainSize / 2;

            for (int i = 0; i < Sections.Length; i++)
            {
                ref FTerrainSection Section = ref Sections[i];

                float2 PositionScale = new float2(TerrianPosition.x, TerrianPosition.z) + new float2(TerrainSize_Half, TerrainSize_Half);
                float2 RectUV = new float2((Section.PivotPosition.x - PositionScale.x) + TerrainSize_Half, (Section.PivotPosition.z - PositionScale.y) + TerrainSize_Half);

                int ReverseScale = TerrainSize - NumQuad;
                Color[] HeightValues = Heightmap.GetPixels(Mathf.FloorToInt(RectUV.x), ReverseScale - Mathf.FloorToInt(RectUV.y), Mathf.FloorToInt(NumQuad), Mathf.FloorToInt(NumQuad), 0);

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
                Section.BoundBox = new FAABB(NewBoundCenter, new float3(NumQuad, SizeY, NumQuad));
            }
        }
#endif
    }
}
