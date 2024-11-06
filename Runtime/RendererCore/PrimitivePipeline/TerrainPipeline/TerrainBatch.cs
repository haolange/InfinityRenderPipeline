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
        public int index;

        public ViewTerrainElement(in int index)
        {
            this.index = index;
        }

        public int CompareTo(ViewTerrainElement target)
        {
            return index.CompareTo(target.index);
        }

        public bool Equals(ViewTerrainElement target)
        {
            return index.Equals(target.index);
        }

        public override bool Equals(object target)
        {
            return Equals((ViewTerrainElement)target);
        }

        public override int GetHashCode()
        {
            return index.GetHashCode();
        }

        public static implicit operator Int32(ViewTerrainElement element) { return element.index; }
        public static implicit operator ViewTerrainElement(int index) { return new ViewTerrainElement(index); }
    }

    public struct PassTerrainElement : IComparable<PassTerrainElement>, IEquatable<PassTerrainElement>
    {
        public int index;

        public PassTerrainElement(in int index)
        {
            this.index = index;
        }

        public int CompareTo(PassTerrainElement target)
        {
            return index.CompareTo(target.index);
        }

        public bool Equals(PassTerrainElement target)
        {
            return index.Equals(target.index);
        }

        public override bool Equals(object target)
        {
            return Equals((PassTerrainElement)target);
        }

        public override int GetHashCode()
        {
            return index.GetHashCode();
        }

        public static implicit operator Int32(PassTerrainElement element) { return element.index; }
        public static implicit operator PassTerrainElement(int index) { return new PassTerrainElement(index); }
    }
}
