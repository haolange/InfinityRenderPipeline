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
        //public bool nativeCreated { get { return m_Sections.IsCreated; } }

        public int[] maxLODs;
        public FBound boundBox;
        public FTerrainSection[] sections;
        internal NativeArray<FTerrainSection> m_Sections;

        public FTerrainSector(in int sectorSize, in int numSection, in int sectionSize, in float3 sectorPivotPos, in FAABB sectorBound)
        {
            int sectorSize_Half = sectorSize / 2;
            int sectionSize_Half = sectionSize / 2;

            maxLODs = new int[numSection * numSection];
            sections = new FTerrainSection[numSection * numSection];
            boundBox = new FBound(new float3(sectorPivotPos.x + sectorSize_Half, sectorPivotPos.y + (sectorBound.size.y / 2), sectorPivotPos.z + sectorSize_Half), sectorBound.size * 0.5f);

            for (int x = 0; x < numSection; ++x)
            {
                for (int y = 0; y < numSection; ++y)
                {
                    int sectionId = (x * numSection) + y;
                    float3 SectionPivotPosition = sectorPivotPos + new float3(sectionSize * x, 0, sectionSize * y);
                    float3 SectionCenterPosition = SectionPivotPosition + new float3(sectionSize_Half, 0, sectionSize_Half);

                    sections[sectionId] = new FTerrainSection();
                    sections[sectionId].pivotPos = SectionPivotPosition;
                    sections[sectionId].centerPos = SectionCenterPosition;
                    sections[sectionId].boundBox = new FAABB(SectionCenterPosition, new float3(sectionSize, 1, sectionSize));
                }
            }

            InitializLOD(7);
        }

        public void BuildNativeCollection()
        {
            m_Sections = new NativeArray<FTerrainSection>(sections.Length, Allocator.Persistent);

            for (int i = 0; i < sections.Length; ++i)
            {
                m_Sections[i] = sections[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            m_Sections.Dispose();
        }

        private void InitializLOD(in int maxLOD)
        {
            for (int i = 0; i < maxLODs.Length; ++i)
            {
                maxLODs[i] = maxLOD;
            }
        }

        public void BuildLODData(in float lod0ScreenSize, in float lod0Distribution, in float lodDistribution)
        {
            for (int i = 0; i < sections.Length; ++i)
            {
                ref int maxLOD = ref maxLODs[i];
                ref FSectionLODData LODSetting = ref sections[i].lodSetting;

                float CurrentScreenSizeRatio = lod0ScreenSize;
                float[] LODScreenRatioSquared = new float[maxLOD];
                float ScreenSizeRatioDivider = math.max(lod0Distribution, 1.01f);
                LODScreenRatioSquared[0] = CurrentScreenSizeRatio * CurrentScreenSizeRatio;

                // LOD 0 handling
                LODSetting.lod0ScreenSizeSquared = CurrentScreenSizeRatio * CurrentScreenSizeRatio;
                CurrentScreenSizeRatio /= ScreenSizeRatioDivider;
                LODSetting.lod1ScreenSizeSquared = CurrentScreenSizeRatio * CurrentScreenSizeRatio;
                ScreenSizeRatioDivider = math.max(lodDistribution, 1.01f);
                LODSetting.lodOnePlusDistributionScalarSquared = ScreenSizeRatioDivider * ScreenSizeRatioDivider;

                // Other LODs
                for (int j = 1; j < maxLOD; ++j) // This should ALWAYS be calculated from the section size, not user MaxLOD override
                {
                    LODScreenRatioSquared[j] = CurrentScreenSizeRatio * CurrentScreenSizeRatio;
                    CurrentScreenSizeRatio /= ScreenSizeRatioDivider;
                }

                // Clamp ForcedLOD to the valid range and then apply
                LODSetting.lastLODIndex = maxLOD;
                LODSetting.lastLODScreenSizeSquared = LODScreenRatioSquared[maxLOD - 1];
            }
        }

        public void UpdateLODData(in int numQuad, in float3 viewOringin, in float4x4 matrix_Proj)
        {
            if(m_Sections.IsCreated == false) { return; }

            FSectionLODDataUpdateJob sectionLODDataUpdateJob = new FSectionLODDataUpdateJob();
            {
                sectionLODDataUpdateJob.numQuad = numQuad;
                sectionLODDataUpdateJob.viewOringin = viewOringin;
                sectionLODDataUpdateJob.matrix_Proj = matrix_Proj;
                sectionLODDataUpdateJob.nativeSections = m_Sections;
            }
            sectionLODDataUpdateJob.Run();

            /*FSectionLODDataParallelUpdateJob sectionLODDataParallelUpdateJob = new FSectionLODDataParallelUpdateJob();
            {
                sectionLODDataParallelUpdateJob.numQuad = numQuad;
                sectionLODDataParallelUpdateJob.viewOringin = viewOringin;
                sectionLODDataParallelUpdateJob.matrix_Proj = matrix_Proj;
                sectionLODDataParallelUpdateJob.nativeSections = m_NativeSections;
            }
            sectionLODDataParallelUpdateJob.Schedule(m_NativeSections.Length, 32).Complete();*/
        }

#if UNITY_EDITOR
        public void DrawBound(in bool useLODColor = false)
        {
            Geometry.DrawBound(boundBox, Color.white);

            for (int i = 0; i < m_Sections.Length; ++i)
            {
                FTerrainSection section = m_Sections[i];

                if (!useLODColor)
                {
                    Geometry.DrawBound(section.boundBox, Color.yellow);
                } else {
                    Geometry.DrawBound(section.boundBox, TerrainUtility.LODColor[section.lodIndex]);
                }
            }
        }

        public void BuildBounds(int sectorSize, int sectionSize, float scaleHeight, float3 terrianPosition, Texture2D heightmap)
        {
            int sectorSize_Half = sectorSize / 2;

            for (int i = 0; i < sections.Length; ++i)
            {
                ref FTerrainSection section = ref sections[i];

                float2 positionScale = new float2(terrianPosition.x, terrianPosition.z) + new float2(sectorSize_Half, sectorSize_Half);
                float2 rectUV = new float2((section.pivotPos.x - positionScale.x) + sectorSize_Half, (section.pivotPos.z - positionScale.y) + sectorSize_Half);

                int reverseScale = sectorSize - sectionSize;
                Color[] heightValues = heightmap.GetPixels(Mathf.FloorToInt(rectUV.x), reverseScale - Mathf.FloorToInt(rectUV.y), Mathf.FloorToInt(sectionSize), Mathf.FloorToInt(sectionSize), 0);

                float minHeight = heightValues[0].r;
                float maxHeight = heightValues[0].r;
                for (int j = 0; j < heightValues.Length; ++j)
                {
                    if (minHeight < heightValues[j].r)
                    {
                        minHeight = heightValues[j].r;
                    }

                    if (maxHeight > heightValues[j].r)
                    {
                        maxHeight = heightValues[j].r;
                    }
                }

                float posHeight = ((section.centerPos.y + minHeight * scaleHeight) + (section.centerPos.y + maxHeight * scaleHeight)) * 0.5f;
                float sizeHeight = ((section.centerPos.y + minHeight * scaleHeight) - (section.centerPos.y + maxHeight * scaleHeight));
                float3 newBoundCenter = new float3(section.centerPos.x, posHeight, section.centerPos.z);
                section.boundBox = new FAABB(newBoundCenter, new float3(sectionSize, sizeHeight, sectionSize));
            }
        }
#endif
    }
}
