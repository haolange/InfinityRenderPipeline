#ifndef INFINITY_LIGHT_LOOP
#define INFINITY_LIGHT_LOOP
// Shared clustered light loop for Deferred, Forward and the volumetrics. Local lights are sorted by view depth on the
// CPU (LightBinning.cs). Each logarithmic depth bin holds the [min, max] range of local indices whose depth range
// overlaps it (16 bits each); each 16x16 tile holds a bitmask of the lights whose screen rectangle covers it. A pixel
// visits the set bits of its tile mask inside its bin's index range. Local index i is record g_DirectionalLightCount + i.
//
//   FLightLoop loop = BeginLightLoop(pixelCoord, viewDepth);
//   uint recordIndex;
//   while (NextLight(loop, recordIndex)) { ... g_LightRecordBuffer[recordIndex] ... }

#define LIGHT_TILE_SIZE 16u

// x, y = screen size of the tile grid, z = tiles per row, w = mask words per tile.
float4 g_LightTileParams;
// x = bin count, y = scale, z = bias: bin = floor(log2(depth) * scale + bias).
float4 g_LightZBinParams;
StructuredBuffer<uint> SRV_LightZBins;
StructuredBuffer<uint> SRV_LightTileMasks;

struct FLightLoop
{
    uint maskBase;
    uint word;
    uint lastWord;
    uint mask;
    uint minIndex;
    uint maxIndex;
};

uint LightZBin(float viewDepth)
{
    float bin = floor(log2(max(viewDepth, 1e-6)) * g_LightZBinParams.y + g_LightZBinParams.z);
    return (uint)clamp(bin, 0.0, g_LightZBinParams.x - 1.0);
}

uint LoadLightLoopWord(FLightLoop loop)
{
    uint mask = SRV_LightTileMasks[loop.maskBase + loop.word];
    uint lowBit = loop.word == (loop.minIndex >> 5) ? (loop.minIndex & 31u) : 0u;
    uint highBit = loop.word == (loop.maxIndex >> 5) ? (loop.maxIndex & 31u) : 31u;
    return mask & (0xFFFFFFFFu << lowBit) & (0xFFFFFFFFu >> (31u - highBit));
}

FLightLoop BeginLightLoop(uint2 pixelCoord, float viewDepth)
{
    FLightLoop loop = (FLightLoop)0;
    loop.mask = 0;
    loop.word = 1;
    loop.lastWord = 0;
    if (g_LocalLightCount <= 0)
    {
        return loop;
    }

    uint packed = SRV_LightZBins[LightZBin(viewDepth)];
    loop.minIndex = packed & 0xFFFFu;
    loop.maxIndex = packed >> 16;
    if (loop.minIndex > loop.maxIndex)
    {
        return loop;
    }

    uint2 tile = min(pixelCoord / LIGHT_TILE_SIZE, (uint2)(ceil(g_LightTileParams.xy / LIGHT_TILE_SIZE)) - 1u);
    uint wordCount = (uint)g_LightTileParams.w;
    loop.maskBase = (tile.y * (uint)g_LightTileParams.z + tile.x) * wordCount;
    loop.word = loop.minIndex >> 5;
    loop.lastWord = loop.maxIndex >> 5;
    loop.mask = LoadLightLoopWord(loop);
    return loop;
}

FLightLoop BeginLightLoopUV(float2 screenUV, float viewDepth)
{
    return BeginLightLoop((uint2)(saturate(screenUV) * g_LightTileParams.xy), viewDepth);
}

bool NextLight(inout FLightLoop loop, out uint recordIndex)
{
    recordIndex = 0;
    [loop]
    while (loop.mask == 0)
    {
        if (loop.word >= loop.lastWord)
        {
            return false;
        }
        loop.word++;
        loop.mask = LoadLightLoopWord(loop);
    }
    uint bit = firstbitlow(loop.mask);
    loop.mask &= loop.mask - 1u;
    recordIndex = (uint)g_DirectionalLightCount + loop.word * 32u + bit;
    return true;
}
#endif
