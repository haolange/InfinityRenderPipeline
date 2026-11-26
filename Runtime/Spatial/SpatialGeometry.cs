using System;
using Unity.Mathematics;

namespace InfinityTech.Rendering.Spatial
{
    public enum ESpatialVolumeKind : byte
    {
        Sphere = 0,
        Ellipsoid = 1,
        Capsule = 2,
        Box = 3,
        RoundedBox = 4,
    }

    /// <summary>
    /// Axis-aligned world-space bounding box used for conservative spatial culling and projection.
    /// </summary>
    public readonly struct SpatialBounds
    {
        public readonly float3 center;
        public readonly float3 halfExtents;

        public SpatialBounds(float3 center, float3 halfExtents)
        {
            this.center = center;
            this.halfExtents = math.max(halfExtents, float3.zero);
        }

        public float BoundingRadius() => math.length(halfExtents);

        public static SpatialBounds FromCenterExtents(float3 center, float3 halfExtents) => new SpatialBounds(center, halfExtents);

        public static SpatialBounds FromMinMax(float3 min, float3 max)
        {
            float3 safeMax = math.max(min, max);
            return new SpatialBounds((min + safeMax) * 0.5f, (safeMax - min) * 0.5f);
        }

        public bool Intersects(in SpatialBounds other)
        {
            return math.all(math.abs(center - other.center) <= (halfExtents + other.halfExtents));
        }

        public bool IntersectsSphere(float3 sphereCenter, float sphereRadius)
        {
            float3 delta = math.max(math.abs(sphereCenter - center) - halfExtents, float3.zero);
            return math.lengthsq(delta) <= sphereRadius * sphereRadius;
        }
    }

    /// <summary>
    /// Authoring-to-runtime primitive volume parameterized by kind, pose, extent and radius.
    /// </summary>
    public readonly struct SpatialVolume
    {
        public readonly ESpatialVolumeKind kind;
        public readonly float3 center;
        public readonly quaternion orientation;
        // Box / RoundedBox: box half-extents. Ellipsoid: radii. Capsule: half segment length in Y. Sphere: unused.
        public readonly float3 halfExtents;
        // Sphere / Capsule / RoundedBox radius.
        public readonly float radius;

        SpatialVolume(ESpatialVolumeKind kind, float3 center, quaternion orientation, float3 halfExtents, float radius)
        {
            this.kind = kind;
            this.center = center;
            this.orientation = math.normalizesafe(orientation, quaternion.identity);
            this.halfExtents = math.max(halfExtents, float3.zero);
            this.radius = math.max(radius, 0.0f);
        }

        public static SpatialVolume CreateSphere(float3 center, float radius)
            => new SpatialVolume(ESpatialVolumeKind.Sphere, center, quaternion.identity, float3.zero, radius);

        public static SpatialVolume CreateEllipsoid(float3 center, quaternion orientation, float3 radii)
            => new SpatialVolume(ESpatialVolumeKind.Ellipsoid, center, orientation, math.max(radii, new float3(1e-4f)), 0.0f);

        public static SpatialVolume CreateCapsule(float3 center, quaternion orientation, float halfLength, float radius)
            => new SpatialVolume(ESpatialVolumeKind.Capsule, center, orientation, new float3(0.0f, math.max(halfLength, 0.0f), 0.0f), radius);

        public static SpatialVolume CreateBox(float3 center, quaternion orientation, float3 halfExtents)
            => new SpatialVolume(ESpatialVolumeKind.Box, center, orientation, halfExtents, 0.0f);

        public static SpatialVolume CreateRoundedBox(float3 center, quaternion orientation, float3 halfExtents, float radius)
            => new SpatialVolume(ESpatialVolumeKind.RoundedBox, center, orientation, halfExtents, radius);

        public float3 ToLocalPoint(float3 pointWS)
            => math.mul(math.conjugate(orientation), pointWS - center);

        public float3 ToLocalDirection(float3 directionWS)
            => math.mul(math.conjugate(orientation), directionWS);

        public float BoundingRadius()
        {
            switch (kind)
            {
                case ESpatialVolumeKind.Sphere:
                    return radius;
                case ESpatialVolumeKind.Ellipsoid:
                    return math.cmax(halfExtents);
                case ESpatialVolumeKind.Capsule:
                    return halfExtents.y + radius;
                case ESpatialVolumeKind.Box:
                    return math.length(halfExtents);
                case ESpatialVolumeKind.RoundedBox:
                    return math.length(math.max(halfExtents - radius, float3.zero)) + radius;
                default:
                    return 0.0f;
            }
        }

        public SpatialBounds ComputeWorldBounds()
        {
            if (kind == ESpatialVolumeKind.Sphere)
            {
                return new SpatialBounds(center, new float3(radius));
            }
            var basis = new float3x3(orientation);
            if (kind == ESpatialVolumeKind.Capsule)
            {
                float3 axisExtent = math.abs(basis.c1) * halfExtents.y + new float3(radius);
                return new SpatialBounds(center, axisExtent);
            }
            float3 rotatedExtents = math.abs(basis.c0) * halfExtents.x
                + math.abs(basis.c1) * halfExtents.y
                + math.abs(basis.c2) * halfExtents.z;
            return new SpatialBounds(center, rotatedExtents);
        }

        public float EvaluateSignedDistance(float3 pointWS)
        {
            float3 local = ToLocalPoint(pointWS);
            switch (kind)
            {
                case ESpatialVolumeKind.Sphere:
                    return math.length(local) - radius;
                case ESpatialVolumeKind.Ellipsoid:
                {
                    float3 safeRadii = math.max(halfExtents, new float3(1e-4f));
                    float k0 = math.length(local / safeRadii);
                    float k1 = math.length(local / (safeRadii * safeRadii));
                    return k1 > 1e-6f ? k0 * (k0 - 1.0f) / k1 : -math.cmin(safeRadii);
                }
                case ESpatialVolumeKind.Capsule:
                {
                    local.y -= math.clamp(local.y, -halfExtents.y, halfExtents.y);
                    return math.length(local) - radius;
                }
                case ESpatialVolumeKind.Box:
                {
                    float3 q = math.abs(local) - halfExtents;
                    return math.length(math.max(q, 0.0f)) + math.min(math.cmax(q), 0.0f);
                }
                case ESpatialVolumeKind.RoundedBox:
                {
                    float r = math.min(radius, math.cmin(halfExtents));
                    float3 inner = math.max(halfExtents - r, 0.0f);
                    float3 q = math.abs(local) - inner;
                    return math.length(math.max(q, 0.0f)) + math.min(math.cmax(q), 0.0f) - r;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported spatial volume kind.");
            }
        }

        public float EvaluateInfluenceWeight(float3 pointWS, float blendDistance)
        {
            float sdf = EvaluateSignedDistance(pointWS);
            return math.saturate(-sdf / math.max(blendDistance, 1e-4f));
        }

        public static bool TryRayIntersectBox(float3 originLocal, float3 dirLocal, float3 halfExtents, out float tExit)
        {
            if (math.lengthsq(dirLocal) < 1e-12f)
            {
                tExit = 0.0f;
                return false;
            }
            float3 rcpDir = math.rcp(dirLocal + math.select(new float3(-1e-6f), new float3(1e-6f), dirLocal >= 0.0f));
            float3 tMax = math.max((halfExtents - originLocal) * rcpDir, (-halfExtents - originLocal) * rcpDir);
            tExit = math.max(math.cmin(tMax), 0.0f);
            return true;
        }

        public static bool TryRayIntersectSphere(float3 originLocal, float3 dirLocal, float radius, out float tExit)
        {
            float a = math.dot(dirLocal, dirLocal);
            float safeRadius = math.max(radius, 0.0f);
            float b = math.dot(originLocal, dirLocal);
            float c = math.dot(originLocal, originLocal) - safeRadius * safeRadius;
            float discriminant = b * b - a * c;
            if (a < 1e-12f || discriminant < 0.0f)
            {
                tExit = 0.0f;
                return false;
            }
            tExit = math.max((-b + math.sqrt(discriminant)) / a, 0.0f);
            return true;
        }
    }
}
