using System;
using UnityEngine;
using Unity.Mathematics;

namespace InfinityTech.Core.Geometry
{
    [Serializable]
    public struct FPlane : IEquatable<FPlane>
    {
        private float m_Distance;
        private float3 m_Normal;

        public float distance { get { return m_Distance; } set { m_Distance = value; } }
        public float3 normal { get { return m_Normal; } set { m_Normal = value; } }


        public FPlane(float3 inNormal, float3 inPoint)
        {
            m_Normal = math.normalize(inNormal);
            m_Distance = -math.dot(m_Normal, inPoint);
        }

        public FPlane(float3 inNormal, float d)
        {
            m_Normal = math.normalize(inNormal);
            m_Distance = d;
        }

        public FPlane(float3 a, float3 b, float3 c)
        {
            m_Normal = math.normalize(math.cross(b - a, c - a));
            m_Distance = -math.dot(m_Normal, a);
        }

        public override bool Equals(object other)
        {
            if (!(other is FPlane)) return false;

            return Equals((FPlane)other);
        }

        public bool Equals(FPlane other)
        {
            return distance.Equals(other.distance) && m_Normal.Equals(other.m_Normal);
        }

        public override int GetHashCode()
        {
            return distance.GetHashCode() ^ (m_Normal.GetHashCode() << 2);
        }

        public static implicit operator Plane(FPlane plane) { return new Plane(plane.normal, plane.distance); }

        public static implicit operator FPlane(Plane plane) { return new FPlane(plane.normal, plane.distance); }
    }

    [Serializable]
    public struct FAABB : IEquatable<FAABB>
    {
        private float3 m_Center;
        private float3 m_Extents;

        public float3 center { get { return m_Center; } set { m_Center = value; } }
        public float3 size { get { return m_Extents * 2.0F; } set { m_Extents = value * 0.5F; } }
        public float3 extents { get { return m_Extents; } set { m_Extents = value; } }
        public float3 min { get { return center - extents; } set { SetMinMax(value, max); } }
        public float3 max { get { return center + extents; } set { SetMinMax(min, value); } }


        public FAABB(float3 center, float3 size)
        {
            m_Center = center;
            m_Extents = size * 0.5F;
        }

        public override bool Equals(object other)
        {
            if (!(other is FAABB)) return false;

            return Equals((FAABB)other);
        }

        public bool Equals(FAABB other)
        {
            return center.Equals(other.center) && extents.Equals(other.extents);
        }

        public override int GetHashCode()
        {
            return center.GetHashCode() ^ (extents.GetHashCode() << 2);
        }

        public void SetMinMax(float3 min, float3 max)
        {
            extents = (max - min) * 0.5F;
            center = min + extents;
        }

        public static implicit operator Bounds(FAABB AABB) { return new Bounds(AABB.center, AABB.size); }

        public static implicit operator FAABB(Bounds Bound) { return new FAABB(Bound.center, Bound.size); }
    }

    [Serializable]
    public struct FBound : IEquatable<FBound>
    {
        public float3 center;
        public float3 extents;


        public FBound(float3 Center, float3 Extents)
        {
            center = Center;
            extents = Extents;
        }

        public override bool Equals(object other)
        {
            if (!(other is FBound)) return false;

            return Equals((FBound)other);
        }

        public bool Equals(FBound other)
        {
            return center.Equals(other.center) && extents.Equals(other.extents);
        }

        public override int GetHashCode()
        {
            return center.GetHashCode() ^ (extents.GetHashCode() << 2);
        }

        public static implicit operator FBound(FAABB Bound) { return new FBound(Bound.center, Bound.extents); }
        public static implicit operator FBound(Bounds Bound) { return new FBound(Bound.center, Bound.extents); }
        public static implicit operator FAABB(FBound Bound) { return new FAABB(Bound.center, Bound.extents * 2); }
        public static implicit operator Bounds(FBound Bound) { return new Bounds(Bound.center, Bound.extents * 2); }
    }

    [Serializable]
    public struct FSphere : IEquatable<FSphere>
    {
        private float m_Radius;
        private float3 m_Center;

        public float radius { get { return m_Radius; } set { m_Radius = value; } }
        public float3 center { get { return m_Center; } set { m_Center = value; } }


        public FSphere(float radius, float3 center)
        {
            m_Radius = radius;
            m_Center = center;
        }

        public override bool Equals(object other)
        {
            if (!(other is FSphere)) return false;

            return Equals((FSphere)other);
        }

        public bool Equals(FSphere other)
        {
            return radius.Equals(other.radius) && center.Equals(other.center);
        }

        public override int GetHashCode()
        {
            return radius.GetHashCode() ^ (center.GetHashCode() << 2);
        }
    }

    public static class Geometry
    {
        public static float CaculateBoundRadius(Bounds BoundBox)
        {
            Vector3 Extents = BoundBox.extents;
            return Mathf.Max(Mathf.Max(Mathf.Abs(Extents.x), Mathf.Abs(Extents.y)), Mathf.Abs(Extents.z));
        }

        public static Bounds CaculateWorldBound(Bounds LocalBound, Matrix4x4 Matrix)
        {
            float4 Center = Matrix * new float4(LocalBound.center.x, LocalBound.center.y, LocalBound.center.z, 1);
            float4 Extents = math.abs(Matrix.GetColumn(0) * LocalBound.extents.x) + math.abs(Matrix.GetColumn(1) * LocalBound.extents.y) + math.abs(Matrix.GetColumn(2) * LocalBound.extents.z);

            Bounds WorldBound = LocalBound;
            WorldBound.center = Center.xyz;
            WorldBound.extents = Extents.xyz;

            return WorldBound;
        }

        public static bool IntersectAABBFrustum(FAABB bound, FPlane[] plane)
        {
            for (int i = 0; i < 6; ++i)
            {
                float3 normal = plane[i].normal;
                float distance = plane[i].distance;

                float dist = math.dot(normal, bound.center) + distance;
                float radius = math.dot(bound.extents, math.abs(normal));

                if (dist + radius< 0) {
                    return false;
                }
            }

            return true;
        }

        public static void DrawBound(Bounds b)
        {
            // bottom
            var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
            var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
            var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
            var p4 = new Vector3(b.min.x, b.min.y, b.max.z);

            Debug.DrawLine(p1, p2, Color.blue);
            Debug.DrawLine(p2, p3, Color.red);
            Debug.DrawLine(p3, p4, Color.yellow);
            Debug.DrawLine(p4, p1, Color.magenta);

            // top
            var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
            var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
            var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
            var p8 = new Vector3(b.min.x, b.max.y, b.max.z);

            Debug.DrawLine(p5, p6, Color.blue);
            Debug.DrawLine(p6, p7, Color.red);
            Debug.DrawLine(p7, p8, Color.yellow);
            Debug.DrawLine(p8, p5, Color.magenta);

            // sides
            Debug.DrawLine(p1, p5, Color.white);
            Debug.DrawLine(p2, p6, Color.gray);
            Debug.DrawLine(p3, p7, Color.green);
            Debug.DrawLine(p4, p8, Color.cyan);
        }

        public static void DrawBound(Bounds b, Color DebugColor)
        {
            // bottom
            var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
            var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
            var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
            var p4 = new Vector3(b.min.x, b.min.y, b.max.z);

            Debug.DrawLine(p1, p2, DebugColor);
            Debug.DrawLine(p2, p3, DebugColor);
            Debug.DrawLine(p3, p4, DebugColor);
            Debug.DrawLine(p4, p1, DebugColor);

            // top
            var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
            var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
            var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
            var p8 = new Vector3(b.min.x, b.max.y, b.max.z);

            Debug.DrawLine(p5, p6, DebugColor);
            Debug.DrawLine(p6, p7, DebugColor);
            Debug.DrawLine(p7, p8, DebugColor);
            Debug.DrawLine(p8, p5, DebugColor);

            // sides
            Debug.DrawLine(p1, p5, DebugColor);
            Debug.DrawLine(p2, p6, DebugColor);
            Debug.DrawLine(p3, p7, DebugColor);
            Debug.DrawLine(p4, p8, DebugColor);
        }

        public static void DrawRect(Rect rect, Color color)
        {

            Vector3[] line = new Vector3[5];

            line[0] = new Vector3(rect.x, rect.y, 0);

            line[1] = new Vector3(rect.x + rect.width, rect.y, 0);

            line[2] = new Vector3(rect.x + rect.width, rect.y + rect.height, 0);

            line[3] = new Vector3(rect.x, rect.y + rect.height, 0);

            line[4] = new Vector3(rect.x, rect.y, 0);

            for (int i = 0; i < line.Length - 1; ++i)
            {
                Debug.DrawLine(line[i], line[i + 1], color);
            }
        }
    }
}
