using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public struct TerrainElement : IComparable<TerrainElement>, IEquatable<TerrainElement>
    {
        public int numQuad;
        public int lODIndex;
        public float fractionLOD;
        public float3 pivotPos;
        public FBound boundBox;

        public bool Equals(TerrainElement Target)
        {
            return numQuad.Equals(Target.numQuad) && lODIndex.Equals(Target.lODIndex) && fractionLOD.Equals(Target.fractionLOD) && boundBox.Equals(Target.boundBox) && pivotPos.Equals(Target.pivotPos);
        }

        public override bool Equals(object target)
        {
            return Equals((TerrainElement)target);
        }

        public int CompareTo(TerrainElement target)
        {
            return lODIndex.CompareTo(target.lODIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchForDynamicInstance(ref TerrainElement target)
        {
            return target.lODIndex;
        }

        public override int GetHashCode()
        {
            int hashCode = lODIndex;

            return hashCode;
        }
    }

    public struct ViewTerrainElement : IComparable<ViewTerrainElement>, IEquatable<ViewTerrainElement>
    {
        public int Index;

        public ViewTerrainElement(in int InIndex)
        {
            Index = InIndex;
        }

        public int CompareTo(ViewTerrainElement Target)
        {
            return Index.CompareTo(Target.Index);
        }

        public bool Equals(ViewTerrainElement Target)
        {
            return Index.Equals(Target.Index);
        }

        public override bool Equals(object obj)
        {
            return Equals((ViewTerrainElement)obj);
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        public static implicit operator Int32(ViewTerrainElement ViewMeshBatch) { return ViewMeshBatch.Index; }
        public static implicit operator ViewTerrainElement(int index) { return new ViewTerrainElement(index); }
    }

    public struct PassTerrainElement : IComparable<PassTerrainElement>, IEquatable<PassTerrainElement>
    {
        public int Index;

        public PassTerrainElement(in int InIndex)
        {
            Index = InIndex;
        }

        public int CompareTo(PassTerrainElement Target)
        {
            return Index.CompareTo(Target.Index);
        }

        public bool Equals(PassTerrainElement Target)
        {
            return Index.Equals(Target.Index);
        }

        public override bool Equals(object obj)
        {
            return Equals((PassTerrainElement)obj);
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        public static implicit operator Int32(PassTerrainElement Target) { return Target.Index; }
        public static implicit operator PassTerrainElement(int index) { return new PassTerrainElement(index); }
    }
}
