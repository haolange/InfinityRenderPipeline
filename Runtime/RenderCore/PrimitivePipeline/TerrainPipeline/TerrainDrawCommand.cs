using System;
using UnityEngine;
using Unity.Burst;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public struct TerrainDrawCommand : IComparable<TerrainDrawCommand>, IEquatable<TerrainDrawCommand>
    {
        public int LOD;
        public int Index;
        public float2 Scale;
        public FBound BoundingBox;
        public float3 PivotPosition;

        public bool Equals(TerrainDrawCommand Target)
        {
            return LOD.Equals(Target.LOD) && Index.Equals(Target.Index) && Scale.Equals(Target.Scale) && BoundingBox.Equals(Target.BoundingBox) && PivotPosition.Equals(Target.PivotPosition);
        }

        public override bool Equals(object obj)
        {
            return Equals((TerrainDrawCommand)obj);
        }

        public int CompareTo(TerrainDrawCommand Target)
        {
            return LOD.CompareTo(Target.LOD);
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode() + Scale.GetHashCode() + BoundingBox.GetHashCode() + PivotPosition.GetHashCode();
        }
    }
}
