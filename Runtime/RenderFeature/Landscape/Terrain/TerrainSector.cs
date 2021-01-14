using System;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.Feature
{
    [Serializable]
    public class FTerrainSector
    {
        public FAABB BoundBox;
        public NativeArray<FTerrainSection> TerrainSection;

        public FTerrainSector(in int SectorSize, in int NumSection, in int NumQuad, in float3 SectorPivotPosition, FAABB SectorBound)
        {
            int SectorSize_Half = SectorSize / 2;
            int SectionSize_Half = NumQuad / 2;
            BoundBox = new Bounds(new float3(SectorPivotPosition.x + SectorSize_Half, SectorPivotPosition.y + (SectorBound.size.y / 2), SectorPivotPosition.z + SectorSize_Half), SectorBound.size);

            TerrainSection = new NativeArray<FTerrainSection>(NumSection * NumSection, Allocator.Persistent);

            for (int SectorSizeX = 0; SectorSizeX <= NumSection - 1; SectorSizeX++)
            {
                for (int SectorSizeY = 0; SectorSizeY <= NumSection - 1; SectorSizeY++)
                {
                    int ArrayIndex = (SectorSizeX * NumSection) + SectorSizeY;
                    float3 SectionPivotPosition = SectorPivotPosition + new float3(NumQuad * SectorSizeX, 0, NumQuad * SectorSizeY);
                    float3 SectionCenterPosition = SectionPivotPosition + new float3(SectionSize_Half, 0, SectionSize_Half);

                    TerrainSection[ArrayIndex] = new FTerrainSection();
                    /*TerrainSection[ArrayIndex].NeighborSection = new TerrainSection[4];

                    TerrainSection[ArrayIndex].Name = "X_" + SectorSizeX.ToString() + "Y_" + SectorSizeY;
                    TerrainSection[ArrayIndex].SectionIndex = ArrayIndex;
                    TerrainSection[ArrayIndex].Position = SectionPosition;
                    TerrainSection[ArrayIndex].CenterPosition = SectionCenterPosition;
                    TerrainSection[ArrayIndex].BoundinBox = new Bounds(SectionCenterPosition, new Vector3(InSectionSize, 1, InSectionSize));*/
                }
            }
        }

        public void Release()
        {
            TerrainSection.Dispose();
        }
    }
}
