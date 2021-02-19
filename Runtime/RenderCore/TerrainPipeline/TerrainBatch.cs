using System;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public struct FTerrainBatch : IComparable<FTerrainBatch>, IEquatable<FTerrainBatch>
    {
        public int LODIndex;


        public bool Equals(FTerrainBatch Target)
        {
            return LODIndex.Equals(Target.LODIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTerrainBatch)obj);
        }

        public int CompareTo(FTerrainBatch MeshBatch)
        {
            return LODIndex.CompareTo(MeshBatch.LODIndex);
        }

        [BurstCompile]
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchForDynamicInstance(ref FTerrainBatch Target)
        {
            return Target.LODIndex;
        }

        [BurstCompile]
        public static int MatchForCacheMeshBatch(ref FTerrainBatch Target, in int InstanceID)
        {
            return InstanceID + Target.GetHashCode();
        }

        public override int GetHashCode()
        {
            int hashCode = LODIndex;

            return hashCode;
        }
    }

    public struct FViewTerrainBatch : IComparable<FViewTerrainBatch>
    {
        public int Flag;


        public FViewTerrainBatch(in int InFlag)
        {
            Flag = InFlag;
        }

        public int CompareTo(FViewTerrainBatch ViewMeshBatch)
        {
            return Flag.CompareTo(ViewMeshBatch.Flag);
        }

        public static implicit operator Int32(FViewTerrainBatch ViewMeshBatch) { return ViewMeshBatch.Flag; }
        public static implicit operator FViewTerrainBatch(int index) { return new FViewTerrainBatch(index); }
    }

    public struct FPassTerrainBatch : IComparable<FPassTerrainBatch>, IEquatable<FPassTerrainBatch>
    {
        public int MeshBatchIndex;


        public FPassTerrainBatch(in int InMeshBatchIndex)
        {
            MeshBatchIndex = InMeshBatchIndex;
        }

        public int CompareTo(FPassTerrainBatch Target)
        {
            return MeshBatchIndex.CompareTo(Target.MeshBatchIndex);
        }

        public bool Equals(FPassTerrainBatch Target)
        {
            return MeshBatchIndex.Equals(Target.MeshBatchIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FPassTerrainBatch)obj);
        }

        public override int GetHashCode()
        {
            return MeshBatchIndex.GetHashCode() + 5;
        }

        public static implicit operator Int32(FPassTerrainBatch MDCValue) { return MDCValue.MeshBatchIndex; }
        public static implicit operator FPassTerrainBatch(int index) { return new FPassTerrainBatch(index); }
    }
}
