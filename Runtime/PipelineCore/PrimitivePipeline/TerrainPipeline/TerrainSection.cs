using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [Serializable]
    public struct FSectionLODData
	{
        public int lastLODIndex;
        public float lod0ScreenSizeSquared;
		public float lod1ScreenSizeSquared;
		public float lodOnePlusDistributionScalarSquared;
		public float lastLODScreenSizeSquared;
	};

    [Serializable]
    public struct FTerrainSection : IComparable<FTerrainSection>, IEquatable<FTerrainSection>
    {
        public int numQuad;
        public int lodIndex;
        public float fractionLOD;

        public FBound boundBox;
        public float3 pivotPos;
        public float3 centerPos;
        public FSectionLODData lodSetting;


        public bool Equals(FTerrainSection target)
        {
            return numQuad.Equals(target.numQuad) && lodIndex.Equals(target.lodIndex) && fractionLOD.Equals(target.fractionLOD) && boundBox.Equals(target.boundBox) && pivotPos.Equals(target.pivotPos) && centerPos.Equals(target.centerPos);
        }

        public int CompareTo(FTerrainSection target)
        {
            return lodIndex.CompareTo(target.lodIndex);
        }

        public override bool Equals(object target)
        {
            return Equals((FTerrainSection)target);
        }

        public override int GetHashCode()
        {
            int hashCode = numQuad;
            hashCode += lodIndex.GetHashCode();
            hashCode += boundBox.GetHashCode();
            hashCode += pivotPos.GetHashCode();
            hashCode += centerPos.GetHashCode();
            hashCode += fractionLOD.GetHashCode();
            return hashCode;
        }
    }
}
