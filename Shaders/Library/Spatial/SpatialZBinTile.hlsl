#ifndef INFINITY_SPATIAL_ZBIN_TILE
#define INFINITY_SPATIAL_ZBIN_TILE

struct SpatialZBinTileState
{
    uint maskBase;
    uint word;
    uint lastWord;
    uint mask;
    uint minIndex;
    uint maxIndex;
};

uint SpatialDepthToZBin(float viewDepth, float4 zBinParams)
{
    float bin = floor(log2(max(viewDepth, 1e-6)) * zBinParams.y + zBinParams.z);
    return (uint)clamp(bin, 0.0, max(zBinParams.x - 1.0, 0.0));
}

uint SpatialLoadZBinTileWord(SpatialZBinTileState state, StructuredBuffer<uint> tileMasks)
{
    uint mask = tileMasks[state.maskBase + state.word];
    uint lowBit = state.word == (state.minIndex >> 5) ? (state.minIndex & 31u) : 0u;
    uint highBit = state.word == (state.maxIndex >> 5) ? (state.maxIndex & 31u) : 31u;
    return mask & (0xFFFFFFFFu << lowBit) & (0xFFFFFFFFu >> (31u - highBit));
}

// Initializes caller-owned integer iteration state via out parameter for Metal cross-compiler compatibility.
void SpatialBeginZBinTileLoop(
    uint2 pixelCoord,
    float viewDepth,
    int itemCount,
    uint tileSize,
    float4 tileParams,
    float4 zBinParams,
    StructuredBuffer<uint> zBins,
    StructuredBuffer<uint> tileMasks,
    out SpatialZBinTileState state)
{
    state = (SpatialZBinTileState)0;
    state.mask = 0;
    state.word = 1;
    state.lastWord = 0;
    if (itemCount <= 0)
    {
        return;
    }

    uint packed = zBins[SpatialDepthToZBin(viewDepth, zBinParams)];
    state.minIndex = packed & 0xFFFFu;
    state.maxIndex = packed >> 16;
    if (state.minIndex > state.maxIndex)
    {
        return;
    }

    uint2 tile = min(pixelCoord / tileSize, (uint2)(ceil(tileParams.xy / tileSize)) - 1u);
    uint wordCount = (uint)tileParams.w;
    state.maskBase = (tile.y * (uint)tileParams.z + tile.x) * wordCount;
    state.word = state.minIndex >> 5;
    state.lastWord = state.maxIndex >> 5;
    state.mask = SpatialLoadZBinTileWord(state, tileMasks);
}

bool SpatialNextZBinTileItem(inout SpatialZBinTileState state, StructuredBuffer<uint> tileMasks, out uint localIndex)
{
    localIndex = 0;
    [loop]
    while (state.mask == 0 && state.word < state.lastWord)
    {
        state.word++;
        state.mask = SpatialLoadZBinTileWord(state, tileMasks);
    }
    bool found = state.mask != 0;
    if (found)
    {
        uint bit = firstbitlow(state.mask);
        state.mask &= state.mask - 1u;
        localIndex = state.word * 32u + bit;
    }
    return found;
}

#endif
