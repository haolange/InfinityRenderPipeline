using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public struct FTerrainBatch : IComparable<FTerrainBatch>, IEquatable<FTerrainBatch>
    {
        public int NumQuad;
        public int LODIndex;
        public float FractionLOD;
        public FBound BoundingBox;
        public float3 PivotPosition;
        public float4 NeighborFractionLOD;


        public bool Equals(FTerrainBatch Target)
        {
            return NumQuad.Equals(Target.NumQuad) && LODIndex.Equals(Target.LODIndex) && FractionLOD.Equals(Target.FractionLOD) && BoundingBox.Equals(Target.BoundingBox) && PivotPosition.Equals(Target.PivotPosition) && NeighborFractionLOD.Equals(Target.NeighborFractionLOD);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTerrainBatch)obj);
        }

        public int CompareTo(FTerrainBatch MeshBatch)
        {
            return LODIndex.CompareTo(MeshBatch.LODIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchForDynamicInstance(ref FTerrainBatch Target)
        {
            return Target.LODIndex;
        }

        public override int GetHashCode()
        {
            int hashCode = LODIndex;

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
