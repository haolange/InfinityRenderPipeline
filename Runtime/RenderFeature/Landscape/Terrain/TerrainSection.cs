using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Runtime.Core.Geometry;

namespace InfinityTech.Runtime.Rendering.Feature
{
    public struct SectionLODData
	{
        public int LastLODIndex;
        public float LOD0ScreenSizeSquared;
		public float LOD1ScreenSizeSquared;
		public float LODOnePlusDistributionScalarSquared;
		public float LastLODScreenSizeSquared;
	};

    public struct FTerrainSectionDescription
    {
        public FAABB BoundBox;
    }

    [Serializable]
    public struct FTerrainSection
    {
        public int NumQuad;
        public int LODIndex;
        public float FractionLOD;

        public int MaxLOD;	
        public int FirstLOD;
        public int LastLOD;

        public int SectionIndex;
        public SectionLODData LODSettings;

        public Rect DrawRect;
        public FAABB BoundBox;
        public float3 PivotPosition;
        public float3 CenterPosition;
        
        public int LeftSectionIndex;
        public int RightSectionIndex;
        public int TopSectionIndex;
        public int ButtomSectionIndex;


        /*public FTerrainSection(in FTerrainSectionDescription TerrainSectionDescription)
        {
            BoundBox = TerrainSectionDescription.BoundBox;
        }*/
    }
}
