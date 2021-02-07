using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [Serializable]
    public struct FSectionLODData
	{
        public int LastLODIndex;
        public float LOD0ScreenSizeSquared;
		public float LOD1ScreenSizeSquared;
		public float LODOnePlusDistributionScalarSquared;
		public float LastLODScreenSizeSquared;
	};

    [Serializable]
    public struct FSectionDescription
    {
        public FAABB BoundBox;
    }

    [Serializable]
    public struct FTerrainSection : IComparable<FTerrainSection>, IEquatable<FTerrainSection>
    {
        public int NumQuad;
        public int LODIndex;
        public float FractionLOD;

        public FBound BoundBox;
        public float3 PivotPosition;
        public float3 CenterPosition;

        public int TopSectionIndex;
        public int LeftSectionIndex;
        public int RightSectionIndex;
        public int ButtomSectionIndex;


        /*public FTerrainSection(in FSectionDescription SectionDescription)
        {
            BoundBox = SectionDescription.BoundBox;
        }*/

        public bool Equals(FTerrainSection Target)
        {
            return NumQuad.Equals(Target.NumQuad) && LODIndex.Equals(Target.LODIndex) && FractionLOD.Equals(Target.FractionLOD) && BoundBox.Equals(Target.BoundBox) && PivotPosition.Equals(Target.PivotPosition) && CenterPosition.Equals(Target.CenterPosition) && LeftSectionIndex.Equals(Target.LeftSectionIndex) && RightSectionIndex.Equals(Target.RightSectionIndex) && TopSectionIndex.Equals(Target.TopSectionIndex) && LODIndex.Equals(Target.LODIndex) && ButtomSectionIndex.Equals(Target.ButtomSectionIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTerrainSection)obj);
        }

        public int CompareTo(FTerrainSection Target)
        {
            return LODIndex.CompareTo(Target.LODIndex);
        }

        public override int GetHashCode()
        {
            int hashCode = NumQuad;
            hashCode += LODIndex.GetHashCode();
            hashCode += BoundBox.GetHashCode();
            hashCode += FractionLOD.GetHashCode();
            hashCode += PivotPosition.GetHashCode();
            hashCode += CenterPosition.GetHashCode();
            hashCode += TopSectionIndex.GetHashCode();
            hashCode += LeftSectionIndex.GetHashCode();
            hashCode += RightSectionIndex.GetHashCode();
            hashCode += ButtomSectionIndex.GetHashCode();
            return hashCode;
        }
    }
}
