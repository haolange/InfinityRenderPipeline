#ifndef INFINITY_REFLECTION_PROBES
#define INFINITY_REFLECTION_PROBES
// Reflection probes as a second index space of the clustered list (LightLoop.hlsl): probes are listed by priority
// (higher importance, then smaller volume; C# ReflectionProbePriority), get their own logarithmic z-bins and 16x16
// tile bitmasks, and are accumulated front to back until their weights reach 1; the remainder is the sky. Deferred
// and Forward share this evaluation.
// Probe faces live in the probe atlas (6 slices per probe, CubemapFaceDirection layout, GGX-prefiltered mips).
#include "../Common.hlsl"
#include "../Lighting.hlsl"
#include "../AtmosphereCommon.hlsl"
#include "LightLoop.hlsl"

// C# FReflectionProbeRecord (Runtime/RendererCore/LightPipeline/LightContext.cs, GPU-RECORD marker).
struct FReflectionProbeRecord
{
    // World to influence-box space (rows of a 3x4 affine; the box is centered at the origin, unscaled). Probes are
    // axis aligned (SupportedRenderingFeatures.reflectionProbeModes = None), so the rotation is identity today.
    float4 worldToBox0;
    float4 worldToBox1;
    float4 worldToBox2;
    // xyz = influence half extents (box, including the blend band) or radius in x (sphere); w = blend distance.
    float4 extents;
    // xyz = capture position in box space (box projection origin); w = 1 box projection, 0 infinite.
    float4 capture;
    // xyz = projection box half extents (the probe box without its blend band); w unused.
    float4 projection;
    // x = intensity (HDR decode included); y = first atlas slice; z = atlas mip count - 1; w = 1 sphere influence.
    float4 parameters;
};

int g_ReflectionProbeCount;
// x, y = screen size of the tile grid; z = tiles per row; w = mask words per tile.
float4 g_ProbeTileParams;
StructuredBuffer<FReflectionProbeRecord> SRV_ReflectionProbes;
StructuredBuffer<uint> SRV_ProbeZBins;
StructuredBuffer<uint> SRV_ProbeTileMasks;
Texture2DArray<float4> SRV_ReflectionProbeAtlas;

uint LoadProbeLoopWord(FLightLoop loop)
{
    uint mask = SRV_ProbeTileMasks[loop.maskBase + loop.word];
    uint lowBit = loop.word == (loop.minIndex >> 5) ? (loop.minIndex & 31u) : 0u;
    uint highBit = loop.word == (loop.maxIndex >> 5) ? (loop.maxIndex & 31u) : 31u;
    return mask & (0xFFFFFFFFu << lowBit) & (0xFFFFFFFFu >> (31u - highBit));
}

FLightLoop BeginProbeLoop(uint2 pixelCoord, float viewDepth)
{
    FLightLoop loop = (FLightLoop)0;
    loop.word = 1;
    loop.lastWord = 0;
    if (g_ReflectionProbeCount > 0)
    {
        uint packed = SRV_ProbeZBins[LightZBin(viewDepth)];
        loop.minIndex = packed & 0xFFFFu;
        loop.maxIndex = packed >> 16;
        if (loop.minIndex <= loop.maxIndex)
        {
            uint2 tile = min(pixelCoord / LIGHT_TILE_SIZE, (uint2)(ceil(g_ProbeTileParams.xy / LIGHT_TILE_SIZE)) - 1u);
            uint wordCount = (uint)g_ProbeTileParams.w;
            loop.maskBase = (tile.y * (uint)g_ProbeTileParams.z + tile.x) * wordCount;
            loop.word = loop.minIndex >> 5;
            loop.lastWord = loop.maxIndex >> 5;
            loop.mask = LoadProbeLoopWord(loop);
        }
    }
    return loop;
}

// Probes come out in list order (lowest index first), which is the blending priority.
bool NextProbe(inout FLightLoop loop, out uint probeIndex)
{
    probeIndex = 0;
    [loop]
    while (loop.mask == 0)
    {
        if (loop.word >= loop.lastWord) return false;
        loop.word++;
        loop.mask = LoadProbeLoopWord(loop);
    }
    uint bit = firstbitlow(loop.mask);
    loop.mask &= loop.mask - 1u;
    probeIndex = loop.word * 32u + bit;
    return true;
}

float3 ProbeBoxPosition(FReflectionProbeRecord probe, float3 positionWS)
{
    float4 p = float4(positionWS, 1.0);
    return float3(dot(probe.worldToBox0, p), dot(probe.worldToBox1, p), dot(probe.worldToBox2, p));
}

float3 ProbeBoxDirection(FReflectionProbeRecord probe, float3 directionWS)
{
    return float3(dot(probe.worldToBox0.xyz, directionWS), dot(probe.worldToBox1.xyz, directionWS), dot(probe.worldToBox2.xyz, directionWS));
}

// 1 inside the influence volume, fading to 0 over the blend distance at its border.
float ProbeInfluenceWeight(FReflectionProbeRecord probe, float3 boxPosition)
{
    float blend = max(probe.extents.w, 1e-4);
    float weight = 0;
    if (probe.parameters.w != 0)
    {
        weight = saturate((probe.extents.x - length(boxPosition)) / blend);
    }
    else
    {
        float3 distanceToFace = probe.extents.xyz - abs(boxPosition);
        weight = saturate(min(distanceToFace.x, min(distanceToFace.y, distanceToFace.z)) / blend);
    }
    return weight;
}

// Box projection (parallax correction): the reflected ray from the shaded point hits the projection box; the probe
// is sampled along the direction from its capture position to that hit.
float3 ProbeSampleDirection(FReflectionProbeRecord probe, float3 boxPosition, float3 boxDirection)
{
    float3 direction = boxDirection;
    if (probe.capture.w != 0)
    {
        float3 extents = probe.parameters.w != 0 ? probe.projection.xxx : probe.projection.xyz;
        float3 rcpDirection = rcp(boxDirection + (boxDirection >= 0 ? 1e-6 : -1e-6));
        float3 tMax = max((extents - boxPosition) * rcpDirection, (-extents - boxPosition) * rcpDirection);
        float t = min(tMax.x, min(tMax.y, tMax.z));
        direction = boxPosition + boxDirection * max(t, 0) - probe.capture.xyz;
    }
    return direction;
}

float3 SampleReflectionProbe(FReflectionProbeRecord probe, float3 directionBox, float roughness)
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
    FLightLoop loop = BeginProbeLoop(pixelCoord, viewDepth);
    uint probeIndex;
    [loop]
    while (weight < 0.999 && NextProbe(loop, probeIndex))
    {
        FReflectionProbeRecord probe = SRV_ReflectionProbes[probeIndex];
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

// Specular environment: probes in priority order, the sky (already sampled by the caller) for the remainder.
float3 BlendReflectionProbesWithSky(float3 positionWS, float3 reflectionWS, float roughness, uint2 pixelCoord, float viewDepth, float3 sky)
{
    float weight;
    float3 probes = EvaluateReflectionProbes(positionWS, reflectionWS, roughness, pixelCoord, viewDepth, weight);
    return probes + sky * (1.0 - weight);
}
#endif
