using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.TerrainPipeline
{
    [Serializable]
    public struct SectionLODData
	{
        public int lastLODIndex;
        public float lod0ScreenSizeSquared;
		public float lod1ScreenSizeSquared;
		public float lastLODScreenSizeSquared;
		public float lodOnePlusDistributionScalarSquared;
	};

    [Serializable]
    public struct TerrainSection : IComparable<TerrainSection>, IEquatable<TerrainSection>
    {
        public int numQuad;
        public int lodIndex;
        public float fractionLOD;
        public float3 pivotPos;
        public FBound boundBox;
        public SectionLODData lodSetting;

        public bool Equals(TerrainSection target)
        {
            return numQuad.Equals(target.numQuad) && lodIndex.Equals(target.lodIndex) && fractionLOD.Equals(target.fractionLOD) && boundBox.Equals(target.boundBox) && pivotPos.Equals(target.pivotPos);
        }

        public int CompareTo(TerrainSection target)
        {
            return lodIndex.CompareTo(target.lodIndex);
        }

        public override bool Equals(object target)
        {
            return Equals((TerrainSection)target);
        }

        public override int GetHashCode()
        {
            int hashCode = numQuad;
            hashCode += lodIndex.GetHashCode();
            hashCode += boundBox.GetHashCode();
            hashCode += pivotPos.GetHashCode();
            hashCode += fractionLOD.GetHashCode();
            return hashCode;
        }
    }
}
