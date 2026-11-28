#ifndef INFINITY_STP_REDUCTION
#define INFINITY_STP_REDUCTION

groupshared float4 gs_StpScratch[128];
groupshared uint gs_StpSourceLane[64];

void StpSetReductionSource(uint lane, uint2 localPosition, uint2 validSize)
{
    uint2 p = min(localPosition, validSize - 1u);
    gs_StpSourceLane[lane] = (p.x & 1u) | ((p.y & 3u) << 1u) | ((p.x & 6u) << 2u) | ((p.y & 4u) << 3u);
}

void StpReduceMax(uint lane, inout float4 a, inout float4 b)
{
    gs_StpScratch[lane] = a;
    gs_StpScratch[lane + 64] = b;
    GroupMemoryBarrierWithGroupSync();
    uint source = gs_StpSourceLane[lane];
    float4 edgeA = gs_StpScratch[source], edgeB = gs_StpScratch[source + 64];
    GroupMemoryBarrierWithGroupSync();
    a = edgeA;
    b = edgeB;
    gs_StpScratch[lane] = a;
    gs_StpScratch[lane + 64] = b;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint stage = 0; stage < 2; ++stage)
    {
        uint step = stage == 0 ? 1 : 4;
        uint mask = stage == 0 ? 3 : 15;
        uint offset = lane & ~mask;
        uint i0 = offset + ((lane + step) & mask);
        uint i1 = offset + ((lane + 2 * step) & mask);
        uint i2 = offset + ((lane + 3 * step) & mask);
        float4 x0 = gs_StpScratch[i0], x1 = gs_StpScratch[i1], x2 = gs_StpScratch[i2];
        float4 y0 = gs_StpScratch[i0 + 64], y1 = gs_StpScratch[i1 + 64], y2 = gs_StpScratch[i2 + 64];
        GroupMemoryBarrierWithGroupSync();
        a = max(max(max(a, x0), x1), x2);
        b = max(max(max(b, y0), y1), y2);
        if (stage == 0)
        {
            gs_StpScratch[lane] = a;
            gs_StpScratch[lane + 64] = b;
            GroupMemoryBarrierWithGroupSync();
        }
    }
}

void StpReduceSum(uint lane, inout float4 a)
{
    gs_StpScratch[lane] = a;
    GroupMemoryBarrierWithGroupSync();
    float4 edge = gs_StpScratch[gs_StpSourceLane[lane]];
    GroupMemoryBarrierWithGroupSync();
    a = edge;
    gs_StpScratch[lane] = a;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint stage = 0; stage < 2; ++stage)
    {
        uint step = stage == 0 ? 1 : 4;
        uint mask = stage == 0 ? 3 : 15;
        uint offset = lane & ~mask;
        float4 x0 = gs_StpScratch[offset + ((lane + step) & mask)];
        float4 x1 = gs_StpScratch[offset + ((lane + 2 * step) & mask)];
        float4 x2 = gs_StpScratch[offset + ((lane + 3 * step) & mask)];
        GroupMemoryBarrierWithGroupSync();
        a += x0 + x1 + x2;
        if (stage == 0)
        {
            gs_StpScratch[lane] = a;
            GroupMemoryBarrierWithGroupSync();
        }
    }
}

#endif
