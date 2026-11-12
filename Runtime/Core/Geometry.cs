using System;
using UnityEngine;
using Unity.Mathematics;

namespace InfinityTech.Core.Geometry
{
    [Serializable]
    public struct BoundingBox : IEquatable<BoundingBox>
    {
        public float3 center;
        public float3 extents;


        public BoundingBox(float3 center, float3 extents)
        {
            this.center = center;
            this.extents = extents;
        }

        public override bool Equals(object other)
        {
            if (!(other is BoundingBox))
            {
                return false;
            }

            return Equals((BoundingBox)other);
        }

        public bool Equals(BoundingBox other)
        {
            return center.Equals(other.center) && extents.Equals(other.extents);
        }

        public override int GetHashCode()
        {
            return center.GetHashCode() ^ (extents.GetHashCode() << 2);
        }

        /// <summary>The world-space box that encloses this local box under an affine transform.</summary>
        public static BoundingBox Transform(in BoundingBox local, in float4x4 localToWorld)
        {
            return new BoundingBox(math.transform(localToWorld, local.center),
                math.abs(localToWorld.c0.xyz) * local.extents.x + math.abs(localToWorld.c1.xyz) * local.extents.y + math.abs(localToWorld.c2.xyz) * local.extents.z);
        }

        public static implicit operator BoundingBox(Bounds bounds)
        {
            return new BoundingBox(bounds.center, bounds.extents);
        }

        public static implicit operator Bounds(BoundingBox box)
        {
            return new Bounds(box.center, box.extents * 2);
        }
    }


    public static class Geometry
    {
        public static void DrawBound(Bounds bounds, Color color)
        {
            var p1 = new Vector3(bounds.min.x, bounds.min.y, bounds.min.z);
            var p2 = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
            var p3 = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
            var p4 = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
            var p5 = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
            var p6 = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);
            var p7 = new Vector3(bounds.max.x, bounds.max.y, bounds.max.z);
            var p8 = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);

            // Bottom

            Debug.DrawLine(p1, p2, color);
            Debug.DrawLine(p2, p3, color);
            Debug.DrawLine(p3, p4, color);
            Debug.DrawLine(p4, p1, color);

            // Top

            Debug.DrawLine(p5, p6, color);
            Debug.DrawLine(p6, p7, color);
            Debug.DrawLine(p7, p8, color);
            Debug.DrawLine(p8, p5, color);

            // Sides

            Debug.DrawLine(p1, p5, color);
            Debug.DrawLine(p2, p6, color);
            Debug.DrawLine(p3, p7, color);
            Debug.DrawLine(p4, p8, color);
        }
    }
}
