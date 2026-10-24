#ifndef INFINITY_SCREEN_SPACE_TRACE_TILES
#define INFINITY_SCREEN_SPACE_TRACE_TILES
// Trace bookkeeping shared by the stochastic screen-space signals (SSR, SSGI; C# ScreenSpaceTraceUtility):
//   - 8x8 trace tiles, listed per class for indirect dispatch (one group per tile, packed x | y << 16);
//   - half-resolution tracing, where each trace pixel traces a different pixel of its 2x2 quad every frame;
//   - ray records (RGBA32F, trace resolution): hit = (uv, raw depth, +pdf); miss = (octahedral world direction, 0,
//     -pdf); no ray = 0. The pdf is the solid-angle pdf of the traced direction;
//   - ray reuse: the resolve gathers its own ray and neighbor rays on a rotated Vogel disk, and samples the color
//     pyramid at the mip that covers the solid angle each reused ray stands for (Krivanek and Colbert 2008).
#include "../../GBufferPack.hlsl"

#define SCREEN_SPACE_TRACE_TILE_SIZE 8

uint PackTraceTile(uint2 tile)
{
    return tile.x | (tile.y << 16);
}

uint2 UnpackTraceTile(uint packed)
{
    return uint2(packed & 0xFFFFu, packed >> 16);
}

uint2 TraceToFullPixel(uint2 tracePixel, uint traceScale, uint frameIndex, uint2 fullSize)
{
    uint2 pixel = tracePixel;
    if (traceScale > 1)
    {
        static const uint2 kQuadOrder[4] = { uint2(0, 0), uint2(1, 1), uint2(1, 0), uint2(0, 1) };
        pixel = min(tracePixel * 2 + kQuadOrder[frameIndex & 3], fullSize - 1);
    }
    return pixel;
}

uint2 FullToTracePixel(uint2 pixel, uint traceScale, uint2 traceSize)
{
    return min(pixel / max(traceScale, 1u), traceSize - 1);
}

float4 EncodeRayHit(float3 hitUVDepth, float pdf)
{
    return float4(hitUVDepth, max(pdf, 1e-5));
}

float4 EncodeRayMiss(float3 directionWS, float pdf)
{
    return float4(UnitVectorToOctahedron(directionWS), 0, -max(pdf, 1e-5));
}

// Offset of reuse sample i of count (sample 0 is the pixel's own ray) on a Vogel disk of the given radius.
int2 RayReuseOffset(uint i, uint count, float radius, float rotation)
{
    int2 offset = 0;
    if (i > 0)
    {
        float r = sqrt((i + 0.5) / count) * radius;
        float theta = i * 2.39996323 + rotation;
        offset = (int2)round(float2(cos(theta), sin(theta)) * r);
    }
    return offset;
}

// Pyramid mip whose texels cover the cone of a reused ray: solid angle 1 / (count * pdf) at hitDistance, seen at the
// hit's eye depth. projectionScale is the pixel count of one world unit at eye depth 1.
float RayConeMip(float pdf, uint count, float hitDistance, float projectionScale, float hitEyeDepth, uint mipCount)
{
    float solidAngle = rcp(count * pdf);
    float footprint = 2 * hitDistance * sqrt(solidAngle / 3.14159265) * projectionScale / max(hitEyeDepth, 1e-4);
    return clamp(log2(max(footprint, 1)), 0, (float)mipCount - 1);
}
#endif
