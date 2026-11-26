#ifndef INFINITY_REFLECTION_PROBE
#define INFINITY_REFLECTION_PROBE
// Spatially indexed reflection probes built on SpatialClusterList + SpatialGeometry: probes are stored in priority order
// (higher importance, then smaller volume; C# ReflectionProbePriority) inside each 3D frustum cluster and blended front
// to back until weight reaches 1, leaving the remainder for the sky.
#include "../Common.hlsl"
#include "../Lighting.hlsl"
#include "../AtmosphereCommon.hlsl"
#include "../Spatial/SpatialGeometry.hlsl"
#include "../Spatial/SpatialClusterList.hlsl"

// C# ReflectionProbeRecord (Runtime/Reflection/ReflectionContext.cs, GPU-RECORD marker).
struct ReflectionProbeRecord
{
    // World to influence-box space (rows of a 3x4 affine; centered at origin).
    float4 worldToBox0;
    float4 worldToBox1;
    float4 worldToBox2;
    // xyz = influence half extents (box, including the blend band) or radius in x (sphere); w = blend distance.
    float4 extents;
    // xyz = capture position in box space (projection origin); w = 1 parallax projection, 0 infinite.
    float4 capture;
    // xyz = projection box half extents or sphere radius in x; w unused.
    float4 projection;
    // x = intensity (HDR decode included); y = first atlas slice; z = atlas mip count - 1; w = 1 sphere influence.
    float4 parameters;
};

// C# ReflectionUniform (Runtime/Reflection/ReflectionContext.cs, GPU-RECORD marker).
cbuffer ReflectionUniform
{
    float4 g_ReflectionClusterParams;
    float4 g_ReflectionZSliceParams;
    int g_ReflectionProbeCount;
    int g_ReflectionPadding0;
    int g_ReflectionPadding1;
    int g_ReflectionPadding2;
};

StructuredBuffer<ReflectionProbeRecord> SRV_ReflectionProbes;
StructuredBuffer<uint2> SRV_ProbeClusterHeaders;
StructuredBuffer<uint> SRV_ProbeClusterIndices;
#if defined(INFINITY_REFLECTION_PROBES)
Texture2DArray<float4> SRV_ReflectionProbeAtlas;
#endif

void BeginProbeLoop(uint2 pixelCoord, float viewDepth, out SpatialClusterLoopState loop)
{
    SpatialBeginClusterLoop(pixelCoord, viewDepth, g_ReflectionProbeCount,
        g_ReflectionClusterParams, g_ReflectionZSliceParams, SRV_ProbeClusterHeaders, loop);
}

// Probes come out in list order (lowest index first), which is the blending priority.
bool NextProbe(inout SpatialClusterLoopState loop, out uint probeIndex)
{
    return SpatialNextClusterItem(loop, SRV_ProbeClusterIndices, probeIndex);
}

float3 ProbeBoxPosition(ReflectionProbeRecord probe, float3 positionWS)
{
    float4 p = float4(positionWS, 1.0);
    return float3(dot(probe.worldToBox0, p), dot(probe.worldToBox1, p), dot(probe.worldToBox2, p));
}

float3 ProbeBoxDirection(ReflectionProbeRecord probe, float3 directionWS)
{
    return float3(dot(probe.worldToBox0.xyz, directionWS), dot(probe.worldToBox1.xyz, directionWS), dot(probe.worldToBox2.xyz, directionWS));
}

// 1 inside the influence volume, fading to 0 over the blend distance at its border.
float ProbeInfluenceWeight(ReflectionProbeRecord probe, float3 boxPosition)
{
    if (probe.parameters.w != 0)
    {
        return SpatialSphereInfluenceWeight(boxPosition, probe.extents.x, probe.extents.w);
    }
    return SpatialBoxInfluenceWeight(boxPosition, probe.extents.xyz, probe.extents.w);
}

// Parallax correction for independent box or sphere projection proxies (capture.w: 0 = none, 1 = box, 2 = sphere).
float3 ProbeSampleDirection(ReflectionProbeRecord probe, float3 boxPosition, float3 boxDirection)
{
    float3 direction = boxDirection;
    if (probe.capture.w > 1.5)
    {
        float t = SpatialRayIntersectSphere(boxPosition, boxDirection, probe.projection.x);
        direction = boxPosition + boxDirection * t - probe.capture.xyz;
    }
    else if (probe.capture.w > 0.5)
    {
        float t = SpatialRayIntersectBox(boxPosition, boxDirection, probe.projection.xyz);
        direction = boxPosition + boxDirection * t - probe.capture.xyz;
    }
    return direction;
}

#if defined(INFINITY_REFLECTION_PROBES)
float3 SampleReflectionProbe(ReflectionProbeRecord probe, float3 directionBox, float roughness)
{
    float3 directionWS = mul(directionBox, float3x3(probe.worldToBox0.xyz, probe.worldToBox1.xyz, probe.worldToBox2.xyz));
    return SampleCubemapArraySeamless(SRV_ReflectionProbeAtlas, (uint)probe.parameters.y, directionWS,
        saturate(roughness) * probe.parameters.z) * probe.parameters.x;
}

// Specular radiance from the probes reaching pixelCoord, and their total weight (0..1). The caller adds the sky times
// (1 - weight).
float3 EvaluateReflectionProbes(float3 positionWS, float3 reflectionWS, float roughness, uint2 pixelCoord, float viewDepth, out float weight)
{
    float3 radiance = 0;
    weight = 0;
    SpatialClusterLoopState loop;
    BeginProbeLoop(pixelCoord, viewDepth, loop);
    uint probeIndex;
    [loop]
    while (weight < 0.999 && NextProbe(loop, probeIndex))
    {
        ReflectionProbeRecord probe = SRV_ReflectionProbes[probeIndex];
        float3 boxPosition = ProbeBoxPosition(probe, positionWS);
        float influence = ProbeInfluenceWeight(probe, boxPosition);
        if (influence <= 0) continue;
        float3 direction = ProbeSampleDirection(probe, boxPosition, ProbeBoxDirection(probe, reflectionWS));
        float contribution = influence * (1.0 - weight);
        radiance += SampleReflectionProbe(probe, direction, roughness) * contribution;
        weight += contribution;
    }
    return radiance;
}
#endif

// Specular environment: probes in priority order, the sky (already sampled by the caller) for the remainder.
float3 BlendReflectionProbesWithSky(float3 positionWS, float3 reflectionWS, float roughness, uint2 pixelCoord, float viewDepth, float3 sky)
{
#if defined(INFINITY_REFLECTION_PROBES)
    float weight;
    float3 probes = EvaluateReflectionProbes(positionWS, reflectionWS, roughness, pixelCoord, viewDepth, weight);
    return probes + sky * (1.0 - weight);
#else
    return sky;
#endif
}

// Unified composition contract across Raster reflection (premultiplied radiance, coverage in alpha), Clustered probes,
// and Sky fallback.
float3 ComposeSpecularEnvironment(float4 rasterReflection, float3 positionWS, float3 reflectionWS, float roughness, uint2 pixelCoord, float viewDepth, float3 sky)
{
    float rasterWeight = saturate(rasterReflection.a);
    float3 probesAndSky = BlendReflectionProbesWithSky(positionWS, reflectionWS, roughness, pixelCoord, viewDepth, sky);
    return rasterReflection.rgb + probesAndSky * (1.0 - rasterWeight);
}
#endif
