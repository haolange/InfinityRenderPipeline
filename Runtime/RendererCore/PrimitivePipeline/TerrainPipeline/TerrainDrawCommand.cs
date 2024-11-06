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
        public int lod;
        public int index;
        public float2 scale;
        public FBound boundBox;
        public float3 pivotPosition;

        public bool Equals(TerrainDrawCommand target)
        {
            return lod.Equals(target.lod) && index.Equals(target.index) && scale.Equals(target.scale) && boundBox.Equals(target.boundBox) && pivotPosition.Equals(target.pivotPosition);
        }

        public override bool Equals(object target)
        {
            return Equals((TerrainDrawCommand)target);
        }

        public int CompareTo(TerrainDrawCommand target)
        {
            return lod.CompareTo(target.lod);
        }

        public override int GetHashCode()
        {
            return index.GetHashCode() + scale.GetHashCode() + boundBox.GetHashCode() + pivotPosition.GetHashCode();
        }
    }
}
