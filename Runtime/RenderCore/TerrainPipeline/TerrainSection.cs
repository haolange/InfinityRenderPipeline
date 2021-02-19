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
    public struct FTerrainSection : IComparable<FTerrainSection>, IEquatable<FTerrainSection>
    {
        public int NumQuad;
        public int LODIndex;
        public float FractionLOD;

        public FBound BoundingBox;
        public float3 PivotPosition;
        public float3 CenterPosition;
        public FSectionLODData LODSetting;


        public bool Equals(FTerrainSection Target)
        {
            return NumQuad.Equals(Target.NumQuad) && LODIndex.Equals(Target.LODIndex) && FractionLOD.Equals(Target.FractionLOD) && BoundingBox.Equals(Target.BoundingBox) && PivotPosition.Equals(Target.PivotPosition) && CenterPosition.Equals(Target.CenterPosition);
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
            hashCode += BoundingBox.GetHashCode();
            hashCode += FractionLOD.GetHashCode();
            hashCode += PivotPosition.GetHashCode();
            hashCode += CenterPosition.GetHashCode();
            return hashCode;
        }
    }
}
