#ifndef INFINITY_SPATIAL_GEOMETRY
#define INFINITY_SPATIAL_GEOMETRY

float SpatialRayIntersectBox(float3 originLocal, float3 dirLocal, float3 halfExtents)
{
    float3 rcpDirection = rcp(dirLocal + (dirLocal >= 0 ? 1e-6 : -1e-6));
    float3 tMax = max((halfExtents - originLocal) * rcpDirection, (-halfExtents - originLocal) * rcpDirection);
    return max(min(tMax.x, min(tMax.y, tMax.z)), 0.0);
}

float SpatialRayIntersectSphere(float3 originLocal, float3 dirLocal, float radius)
{
    float a = max(dot(dirLocal, dirLocal), 1e-6);
    float b = dot(originLocal, dirLocal);
    float c = dot(originLocal, originLocal) - radius * radius;
    float disc = max(b * b - a * c, 0.0);
    return max((-b + sqrt(disc)) / a, 0.0);
}

float SpatialBoxInfluenceWeight(float3 localPos, float3 halfExtents, float blendDistance)
{
    float blend = max(blendDistance, 1e-4);
    float3 distanceToFace = halfExtents - abs(localPos);
    return saturate(min(distanceToFace.x, min(distanceToFace.y, distanceToFace.z)) / blend);
}

float SpatialSphereInfluenceWeight(float3 localPos, float radius, float blendDistance)
{
    float blend = max(blendDistance, 1e-4);
    return saturate((radius - length(localPos)) / blend);
}

float3 SpatialTransformToLocal(float3 pointWS, float4 row0, float4 row1, float4 row2)
{
    return float3(
        dot(row0.xyz, pointWS) + row0.w,
        dot(row1.xyz, pointWS) + row1.w,
        dot(row2.xyz, pointWS) + row2.w);
}

float3 SpatialRotateToLocal(float3 directionWS, float4 row0, float4 row1, float4 row2)
{
    return float3(
        dot(row0.xyz, directionWS),
        dot(row1.xyz, directionWS),
        dot(row2.xyz, directionWS));
}

// Analytical sphere hemisphere occlusion wrt surface normal (0 = fully occluded, 1 = visible).
float SpatialSphereHemisphereVisibility(float3 localPos, float3 localNormal, float3 sphereCenterLocal, float radius, float falloffDistance, float strength)
{
    float3 toCenter = sphereCenterLocal - localPos;
    float centerDist = length(toCenter);
    float r = max(radius, 1e-4);
    float surfaceDist = max(centerDist - r, 0.0);
    float rangeFade = 1.0 - smoothstep(0.0, max(falloffDistance, 1e-4), surfaceDist);
    float vis = 1.0;
    if (rangeFade > 1e-4)
    {
        float3 dir = centerDist > 1e-4 ? toCenter / centerDist : localNormal;
        float cosTheta = dot(localNormal, dir);
        float dSafe = max(centerDist, r);
        float sinAlphaSqr = saturate((r * r) / (dSafe * dSafe));
        float sinAlpha = sqrt(sinAlphaSqr);
        float horizonFactor = smoothstep(-sinAlpha, max(sinAlpha, 1e-4), cosTheta);
        float occ = saturate(saturate(cosTheta * 0.5 + 0.5) * sinAlphaSqr * horizonFactor * 1.25);
        vis = 1.0 - saturate(occ * strength * rangeFade);
    }
    return vis;
}

// Analytical sphere soft cone shadow visibility along local ray dir (0 = shadowed, 1 = unshadowed).
float SpatialConeVisibilitySphere(float3 localOrigin, float3 localDir, float radius, float sinAlpha, float maxDistance, float strength)
{
    float tProj = dot(-localOrigin, localDir);
    float vis = 1.0;
    if (tProj > -radius && tProj < maxDistance)
    {
        float clampedT = max(tProj, 0.0);
        float3 closest = localOrigin + localDir * clampedT;
        float gap = length(closest) - max(radius, 1e-4);
        float coneRadius = max(clampedT, 0.05) * clamp(sinAlpha, 0.01, 1.0);
        float occ = 1.0 - smoothstep(-coneRadius, coneRadius, gap);
        float distFade = 1.0 - smoothstep(maxDistance * 0.75, max(maxDistance, 1e-4), clampedT);
        vis = 1.0 - saturate(occ * strength * distFade);
    }
    return vis;
}

// Closest parameter y on Y-aligned segment [-halfLen, halfLen] to ray localOrigin + t * localDir (t >= 0).
float SpatialClosestSegmentYToRay(float3 localOrigin, float3 localDir, float halfLen)
{
    float dDotY = localDir.y;
    float denom = max(1.0 - dDotY * dDotY, 1e-5);
    float b = dot(localOrigin, localDir);
    float yCand = clamp((localOrigin.y - b * dDotY) / denom, -halfLen, halfLen);
    float tCand = max(-(b - yCand * dDotY), 0.0);
    return clamp(localOrigin.y + tCand * dDotY, -halfLen, halfLen);
}

// Analytical box soft cone shadow visibility along local ray dir (0 = shadowed, 1 = unshadowed).
float SpatialConeVisibilityBox(float3 localOrigin, float3 localDir, float3 halfExtents, float sinAlpha, float maxDistance, float strength)
{
    float3 safeDir = localDir + (localDir >= 0.0 ? 1e-6 : -1e-6);
    float3 invDir = rcp(safeDir);
    float3 t0 = (-halfExtents - localOrigin) * invDir;
    float3 t1 = (halfExtents - localOrigin) * invDir;
    float3 tSmall = min(t0, t1);
    float3 tLarge = max(t0, t1);
    float tEnter = max(tSmall.x, max(tSmall.y, tSmall.z));
    float tExit = min(tLarge.x, min(tLarge.y, tLarge.z));
    float vis = 1.0;

    float boxRadius = length(halfExtents);
    float tCenter = max(dot(-localOrigin, localDir), 0.0);
    if (tCenter < maxDistance + boxRadius)
    {
        float tSample = (tExit >= max(tEnter, 0.0)) ? max((max(tEnter, 0.0) + tExit) * 0.5, 0.0) : tCenter;
        float3 rayPoint = localOrigin + localDir * tSample;
        float3 clampedBox = clamp(rayPoint, -halfExtents, halfExtents);
        float gap = (tExit >= max(tEnter, 0.0)) ? -length(max(halfExtents - abs(rayPoint), 0.0)) : length(rayPoint - clampedBox);
        float coneRadius = max(tSample, 0.05) * clamp(sinAlpha, 0.01, 1.0);
        float occ = 1.0 - smoothstep(-coneRadius, coneRadius, gap);
        float distFade = 1.0 - smoothstep(maxDistance * 0.75, max(maxDistance, 1e-4), tSample);
        vis = 1.0 - saturate(occ * strength * distFade);
    }
    return vis;
}

#endif
