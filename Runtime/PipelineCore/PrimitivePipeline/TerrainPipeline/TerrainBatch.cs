using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public struct FTerrainBatch : IComparable<FTerrainBatch>, IEquatable<FTerrainBatch>
    {
        public int numQuad;
        public int lODIndex;
        public float fractionLOD;
        public float3 pivotPos;
        public FBound boundBox;


        public bool Equals(FTerrainBatch Target)
        {
            return numQuad.Equals(Target.numQuad) && lODIndex.Equals(Target.lODIndex) && fractionLOD.Equals(Target.fractionLOD) && boundBox.Equals(Target.boundBox) && pivotPos.Equals(Target.pivotPos);
        }

        public override bool Equals(object target)
        {
            return Equals((FTerrainBatch)target);
        }

        public int CompareTo(FTerrainBatch target)
        {
            return lODIndex.CompareTo(target.lODIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchForDynamicInstance(ref FTerrainBatch target)
        {
            return target.lODIndex;
        }

        public override int GetHashCode()
        {
            int hashCode = lODIndex;

            return hashCode;
        }
    }

    public struct FViewTerrainBatch : IComparable<FViewTerrainBatch>, IEquatable<FViewTerrainBatch>
    {
        public int Index;


        public FViewTerrainBatch(in int InIndex)
        {
            Index = InIndex;
        }

        public int CompareTo(FViewTerrainBatch Target)
        {
            return Index.CompareTo(Target.Index);
        }

        public bool Equals(FViewTerrainBatch Target)
        {
            return Index.Equals(Target.Index);
        }

        public override bool Equals(object obj)
        {
            return Equals((FViewTerrainBatch)obj);
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        public static implicit operator Int32(FViewTerrainBatch ViewMeshBatch) { return ViewMeshBatch.Index; }
        public static implicit operator FViewTerrainBatch(int index) { return new FViewTerrainBatch(index); }
    }

    public struct FPassTerrainBatch : IComparable<FPassTerrainBatch>, IEquatable<FPassTerrainBatch>
    {
        public int Index;


        public FPassTerrainBatch(in int InIndex)
        {
            Index = InIndex;
        }

        public int CompareTo(FPassTerrainBatch Target)
        {
            return Index.CompareTo(Target.Index);
        }

        public bool Equals(FPassTerrainBatch Target)
        {
            return Index.Equals(Target.Index);
        }

        public override bool Equals(object obj)
        {
            return Equals((FPassTerrainBatch)obj);
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        public static implicit operator Int32(FPassTerrainBatch Target) { return Target.Index; }
        public static implicit operator FPassTerrainBatch(int index) { return new FPassTerrainBatch(index); }
    }
}
