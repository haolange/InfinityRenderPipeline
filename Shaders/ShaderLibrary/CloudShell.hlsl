#ifndef INFINITY_CLOUD_SHELL_INCLUDED
#define INFINITY_CLOUD_SHELL_INCLUDED

// All positions are in world meters. The surface at the origin is the north pole.
float3 CloudPlanetCenter(float planetRadius)
{
    return float3(0, -planetRadius, 0);
}

bool CloudIsSkyDepth(float rawDepth, bool reversedZ)
{
    // Only the actual clear value is sky. Very distant opaque geometry can
    // produce arbitrarily small positive reversed-Z values.
    return reversedZ ? rawDepth == 0 : rawDepth == 1;
}

float CloudSceneRayLimit(float rawDepth, float2 uv, float4x4 inverseViewProjection,
    float3 origin, float3 direction, bool reversedZ)
{
    if (CloudIsSkyDepth(rawDepth, reversedZ))
        return 1e20;
    float4 position = mul(inverseViewProjection, float4(uv * 2 - 1, rawDepth, 1));
    return max(0, dot(position.xyz / position.w - origin, direction));
}

float CloudNormalizedHeight(float3 worldPosition, float planetRadius, float bottom, float thickness)
{
    return (length(worldPosition - CloudPlanetCenter(planetRadius)) - planetRadius - bottom) / thickness;
}

float2 CloudSphereIntersection(float3 relativeOrigin, float3 direction, float radius)
{
    float originRadius = length(relativeOrigin);
    float b = dot(relativeOrigin, direction);
    // Avoid subtracting two squared planet radii at cloud-scale altitudes.
    float c = (originRadius - radius) * (originRadius + radius);
    float discriminant = b * b - c;
    if (discriminant < 0)
        return float2(-1, -1);
    float root = sqrt(discriminant);
    float q = -b - (b >= 0 ? root : -root);
    if (q == 0)
        return float2(0, 0);
    float other = c / q;
    return float2(min(q, other), max(q, other));
}

// Outer sphere minus inner sphere, clipped to the visible half-ray. A grazing ray
// can cross two cloud segments without hitting the planet; keep both in order.
float4 CloudRaySegments(float3 worldOrigin, float3 direction, float planetRadius,
    float bottom, float thickness, float maxDistance)
{
    float3 origin = worldOrigin - CloudPlanetCenter(planetRadius);
    if (length(origin) < planetRadius)
        return 0;

    float2 outer = CloudSphereIntersection(origin, direction, planetRadius + bottom + thickness);
    float start = max(0, outer.x);
    float end = min(maxDistance, outer.y);
    float2 ground = CloudSphereIntersection(origin, direction, planetRadius);
    if (ground.y > 0 && ground.x >= 0)
        end = min(end, ground.x);
    if (end <= start)
        return 0;

    float2 inner = CloudSphereIntersection(origin, direction, planetRadius + bottom);
    if (inner.y <= start || inner.x >= end)
        return float4(start, end, 0, 0);

    float firstEnd = min(end, inner.x);
    float secondStart = max(start, inner.y);
    if (firstEnd > start)
        return float4(start, firstEnd, secondStart < end ? secondStart : 0, secondStart < end ? end : 0);
    return secondStart < end ? float4(secondStart, end, 0, 0) : 0;
}

float CloudRayDistance(float4 segments, float distanceInCloud)
{
    float firstLength = segments.y - segments.x;
    return distanceInCloud < firstLength ? segments.x + distanceInCloud
        : segments.z + distanceInCloud - firstLength;
}

#endif
