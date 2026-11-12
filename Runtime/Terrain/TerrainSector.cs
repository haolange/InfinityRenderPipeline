using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [Serializable]
    public class TerrainSector : IDisposable
    {
        public BoundingBox boundBox;
        public TerrainSection[] sections;
        internal NativeArray<TerrainSection> m_Sections;

        public TerrainSector(in int sectorSize, in int numSection, in int sectionSize, in float3 sectorPivotPos, in Bounds sectorBounds)
        {
            int sectorSize_Half = sectorSize / 2;
            int sectionSize_Half = sectionSize / 2;

            sections = new TerrainSection[numSection * numSection];
            boundBox = new BoundingBox(new float3(sectorPivotPos.x + sectorSize_Half, sectorPivotPos.y + sectorBounds.extents.y, sectorPivotPos.z + sectorSize_Half), sectorBounds.extents);

            for (int x = 0; x < numSection; ++x)
            {
                for (int y = 0; y < numSection; ++y)
                {
                    int sectionId = (x * numSection) + y;
                    float3 pivotPosition = sectorPivotPos + new float3(sectionSize * x, 0, sectionSize * y);
                    float3 centerPosition = pivotPosition + new float3(sectionSize_Half, 0, sectionSize_Half);

                    sections[sectionId] = new TerrainSection();
                    sections[sectionId].pivotPos = pivotPosition;
                    sections[sectionId].boundBox = new BoundingBox(centerPosition, new float3(sectionSize, 1, sectionSize) * 0.5f);
                }
            }
        }

        public void Initialize()
        {
            if (m_Sections.IsCreated)
            {
                return;
            }
            m_Sections = new NativeArray<TerrainSection>(sections.Length, Allocator.Persistent);
        }

        public void BuildLODData(in float lod0ScreenSize, in float lod0Distribution, in float lodDistribution)
        {
            int maxLOD = 7;
            for (int i = 0; i < sections.Length; ++i)
            {
                ref SectionLODData LODSetting = ref sections[i].lodSetting;

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

            m_Sections.CopyFrom(sections);
            //sections = null;
        }

        public void ProcessLOD(in int numQuad, in float3 viewOrigin, in float4x4 projectionMatrix)
        {
            if(m_Sections.IsCreated == false)
            {
                return;
            }

            TerrainProcessLODJob processLODJob = new TerrainProcessLODJob();
            {
                processLODJob.numQuad = numQuad;
                processLODJob.viewOrigin = viewOrigin;
                processLODJob.projectionMatrix = projectionMatrix;
                processLODJob.nativeSections = m_Sections;
            }
            processLODJob.Run();
        }

        public void Dispose()
        {
            if (m_Sections.IsCreated)
            {
                m_Sections.Dispose();
            }
        }

#if UNITY_EDITOR
        public void DrawBound(in bool useLODColor = false)
        {
            Geometry.DrawBound(boundBox, Color.white);

            for (int i = 0; i < m_Sections.Length; ++i)
            {
                TerrainSection section = m_Sections[i];

                if (!useLODColor)
                {
                    Geometry.DrawBound(section.boundBox, Color.yellow);
                } else {
                    Geometry.DrawBound(section.boundBox, TerrainUtility.LODColor[section.lodIndex]);
                }
            }
        }

        public void BuildBounds(in int sectorSize, in int sectionSize, in float scaleHeight, in float3 terrainPosition, Texture2D heightmap)
        {
            int sectorSize_Half = sectorSize / 2;

            for (int i = 0; i < sections.Length; ++i)
            {
                ref TerrainSection section = ref sections[i];

                float2 positionScale = new float2(terrainPosition.x, terrainPosition.z) + new float2(sectorSize_Half, sectorSize_Half);
                float2 rectUV = new float2((section.pivotPos.x - positionScale.x) + sectorSize_Half, (section.pivotPos.z - positionScale.y) + sectorSize_Half);

                int reverseScale = sectorSize - sectionSize;
                Color[] heightValues = heightmap.GetPixels(Mathf.FloorToInt(rectUV.x), reverseScale - Mathf.FloorToInt(rectUV.y), Mathf.FloorToInt(sectionSize), Mathf.FloorToInt(sectionSize), 0);

                float minHeight = heightValues[0].r;
                float maxHeight = heightValues[0].r;
                for (int j = 0; j < heightValues.Length; ++j)
                {
                    minHeight = math.min(minHeight, heightValues[j].r);
                    maxHeight = math.max(maxHeight, heightValues[j].r);
                }

                float centerHeight = section.boundBox.center.y + (minHeight + maxHeight) * 0.5f * scaleHeight;
                float heightRange = (maxHeight - minHeight) * scaleHeight;
                float3 center = new float3(section.boundBox.center.x, centerHeight, section.boundBox.center.z);
                section.boundBox = new BoundingBox(center, new float3(sectionSize, heightRange, sectionSize) * 0.5f);
            }
        }
#endif
    }
}
