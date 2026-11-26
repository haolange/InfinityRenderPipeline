#ifndef INFINITY_ANALYTIC_SHADOW
#define INFINITY_ANALYTIC_SHADOW

#include "Spatial/SpatialGeometry.hlsl"
#include "Spatial/SpatialClusterList.hlsl"

#define ANALYTIC_SHAPE_SPHERE    0
#define ANALYTIC_SHAPE_ELLIPSOID 1
#define ANALYTIC_SHAPE_CAPSULE   2
#define ANALYTIC_SHAPE_BOX       3

#define ANALYTIC_FLAG_INDIRECT 1
#define ANALYTIC_FLAG_DIRECT   2

struct AnalyticOccluderRecord
{
    float4 worldToLocal0;
    float4 worldToLocal1;
    float4 worldToLocal2;
    float4 shapeParams;
    float4 centerAndRadius;
    float4 attenuationParams;
    float4 flagsAndMeta;
};

cbuffer AnalyticShadowUniform
{
    float4 _AnalyticClusterParams;
    float4 _AnalyticZSliceParams;
    int _AnalyticOccluderCount;
    int _AnalyticIndirectEnabled;
    int _AnalyticDirectEnabled;
    int _AnalyticPadding0;
};

StructuredBuffer<AnalyticOccluderRecord> SRV_AnalyticOccluderBuffer;
StructuredBuffer<uint2> SRV_AnalyticIndirectClusterHeader;
StructuredBuffer<uint> SRV_AnalyticIndirectClusterIndices;
StructuredBuffer<uint2> SRV_AnalyticDirectClusterHeader;
StructuredBuffer<uint> SRV_AnalyticDirectClusterIndices;

float EvaluateOccluderIndirectVisibility(AnalyticOccluderRecord rec, float3 positionWS, float3 normalWS)
{
    float vis = 1.0;
    float3 deltaCenter = rec.centerAndRadius.xyz - positionWS;
    float boundR = rec.centerAndRadius.w;
    float falloff = rec.attenuationParams.y;
    float maxReach = boundR + falloff;
    if (dot(deltaCenter, deltaCenter) <= maxReach * maxReach)
    {
        float3 localPos = SpatialTransformToLocal(positionWS, rec.worldToLocal0, rec.worldToLocal1, rec.worldToLocal2);
        float3 localNormal = normalize(SpatialRotateToLocal(normalWS, rec.worldToLocal0, rec.worldToLocal1, rec.worldToLocal2));
        uint shapeKind = (uint)(rec.flagsAndMeta.x + 0.5);
        float strength = rec.attenuationParams.x;

        if (shapeKind == ANALYTIC_SHAPE_SPHERE)
        {
            vis = SpatialSphereHemisphereVisibility(localPos, localNormal, 0.0, rec.shapeParams.w, falloff, strength);
        }
        else if (shapeKind == ANALYTIC_SHAPE_CAPSULE)
        {
            float halfLen = rec.shapeParams.y;
            float3 closestSeg = float3(0.0, clamp(localPos.y, -halfLen, halfLen), 0.0);
            vis = SpatialSphereHemisphereVisibility(localPos, localNormal, closestSeg, rec.shapeParams.w, falloff, strength);
        }
        else if (shapeKind == ANALYTIC_SHAPE_ELLIPSOID)
        {
            float3 radii = max(rec.shapeParams.xyz, 1e-3);
            float distToCenter = length(localPos);
            float3 dir = distToCenter > 1e-4 ? localPos / distToCenter : -localNormal;
            float3 invScaledDir = dir / radii;
            float localSurfaceR = rsqrt(max(dot(invScaledDir, invScaledDir), 1e-6));
            float orthoR = sqrt((radii.x * radii.y * radii.z) / max(localSurfaceR, 1e-3));
            float surfaceDist = max(distToCenter - localSurfaceR, 0.0);
            float rangeFade = 1.0 - smoothstep(0.0, max(falloff, 1e-4), surfaceDist);
            if (rangeFade > 1e-4)
            {
                float3 toCenter = -localPos;
                float3 toCenterDir = distToCenter > 1e-4 ? toCenter / distToCenter : localNormal;
                float cosTheta = dot(localNormal, toCenterDir);
                float dSafe = max(distToCenter, max(orthoR, localSurfaceR));
                float sinAlphaSqr = saturate((orthoR * orthoR) / (dSafe * dSafe));
                float sinAlpha = sqrt(sinAlphaSqr);
                float horizonFactor = smoothstep(-sinAlpha, max(sinAlpha, 1e-4), cosTheta);
                float occ = saturate(saturate(cosTheta * 0.5 + 0.5) * sinAlphaSqr * horizonFactor * 1.25);
                vis = 1.0 - saturate(occ * strength * rangeFade);
            }
        }
        else
        {
            float3 halfExtents = max(rec.shapeParams.xyz, 1e-3);
            float3 clampedSurface = clamp(localPos, -halfExtents, halfExtents);
            float3 boxDir = localPos - clampedSurface;
            float distSq = dot(boxDir, boxDir);
            float approxRadius = min(halfExtents.x, min(halfExtents.y, halfExtents.z)) * 0.85;
            float3 proxyCenter = clamp(localPos, -max(halfExtents - approxRadius, 0.0), max(halfExtents - approxRadius, 0.0));
            if (distSq <= falloff * falloff)
            {
                vis = SpatialSphereHemisphereVisibility(localPos, localNormal, proxyCenter, max(approxRadius, 0.1), falloff, strength);
            }
        }
    }
    return vis;
}

float EvaluateOccluderDirectVisibility(AnalyticOccluderRecord rec, float3 positionWS, float3 lightDirWS, float maxLightDist, float lightAngularRadius)
{
    float vis = 1.0;
    float3 localPos = SpatialTransformToLocal(positionWS, rec.worldToLocal0, rec.worldToLocal1, rec.worldToLocal2);
    float3 localDir = normalize(SpatialRotateToLocal(lightDirWS, rec.worldToLocal0, rec.worldToLocal1, rec.worldToLocal2));
    uint shapeKind = (uint)(rec.flagsAndMeta.x + 0.5);
    float strength = rec.attenuationParams.z;
    float sinAlpha = max(lightAngularRadius, rec.attenuationParams.w);
    float maxDist = min(maxLightDist, rec.flagsAndMeta.w);

    if (shapeKind == ANALYTIC_SHAPE_SPHERE)
    {
        vis = SpatialConeVisibilitySphere(localPos, localDir, rec.shapeParams.w, sinAlpha, maxDist, strength);
    }
    else if (shapeKind == ANALYTIC_SHAPE_CAPSULE)
    {
        float halfLen = rec.shapeParams.y;
        float segY = SpatialClosestSegmentYToRay(localPos, localDir, halfLen);
        vis = SpatialConeVisibilitySphere(localPos - float3(0.0, segY, 0.0), localDir, rec.shapeParams.w, sinAlpha, maxDist, strength);
    }
    else if (shapeKind == ANALYTIC_SHAPE_ELLIPSOID)
    {
        float3 radii = max(rec.shapeParams.xyz, 1e-3);
        float3 invRadii = rcp(radii);
        float3 scaledOrigin = localPos * invRadii;
        float3 unscaledDir = localDir * invRadii;
        float dirScale = max(length(unscaledDir), 1e-5);
        float3 scaledDir = unscaledDir / dirScale;
        vis = SpatialConeVisibilitySphere(scaledOrigin, scaledDir, 1.0, sinAlpha, maxDist * dirScale, strength);
    }
    else
    {
        vis = SpatialConeVisibilityBox(localPos, localDir, max(rec.shapeParams.xyz, 1e-3), sinAlpha, maxDist, strength);
    }
    return vis;
}

float EvaluateAnalyticIndirectOcclusion(uint2 pixelCoord, float linearDepth, float3 positionWS, float3 normalWS, uint renderingLayerMask)
{
    float visibility = 1.0;
    if (_AnalyticOccluderCount > 0 && _AnalyticIndirectEnabled != 0)
    {
        SpatialClusterLoopState state;
        SpatialBeginClusterLoop(pixelCoord, linearDepth, _AnalyticOccluderCount, _AnalyticClusterParams, _AnalyticZSliceParams, SRV_AnalyticIndirectClusterHeader, state);
        uint occluderIndex;
        [loop]
        while (SpatialNextClusterItem(state, SRV_AnalyticIndirectClusterIndices, occluderIndex))
        {
            if (occluderIndex >= (uint)_AnalyticOccluderCount)
            {
                continue;
            }
            AnalyticOccluderRecord rec = SRV_AnalyticOccluderBuffer[occluderIndex];
            if (((uint)rec.flagsAndMeta.z & ANALYTIC_FLAG_INDIRECT) == 0)
            {
                continue;
            }
            if (((uint)(rec.flagsAndMeta.y + 0.5) & renderingLayerMask) == 0)
            {
                continue;
            }
            visibility *= EvaluateOccluderIndirectVisibility(rec, positionWS, normalWS);
        }
    }
    return saturate(visibility);
}

float EvaluateAnalyticDirectShadow(uint2 pixelCoord, float linearDepth, float3 positionWS, float3 lightDirWS, float maxLightDist, float lightAngularRadius, uint renderingLayerMask)
{
    float visibility = 1.0;
    if (_AnalyticOccluderCount > 0 && _AnalyticDirectEnabled != 0)
    {
        SpatialClusterLoopState state;
        SpatialBeginClusterLoop(pixelCoord, linearDepth, _AnalyticOccluderCount, _AnalyticClusterParams, _AnalyticZSliceParams, SRV_AnalyticDirectClusterHeader, state);
        uint occluderIndex;
        [loop]
        while (SpatialNextClusterItem(state, SRV_AnalyticDirectClusterIndices, occluderIndex))
        {
            if (occluderIndex >= (uint)_AnalyticOccluderCount)
            {
                continue;
            }
            AnalyticOccluderRecord rec = SRV_AnalyticOccluderBuffer[occluderIndex];
            if (((uint)rec.flagsAndMeta.z & ANALYTIC_FLAG_DIRECT) == 0)
            {
                continue;
            }
            if (((uint)(rec.flagsAndMeta.y + 0.5) & renderingLayerMask) == 0)
            {
                continue;
            }
            visibility *= EvaluateOccluderDirectVisibility(rec, positionWS, lightDirWS, maxLightDist, lightAngularRadius);
        }
    }
    return saturate(visibility);
}

#endif
