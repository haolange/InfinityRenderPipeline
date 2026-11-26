#ifndef INFINITY_SPATIAL_CLUSTER_LIST
#define INFINITY_SPATIAL_CLUSTER_LIST

struct SpatialClusterLoopState
{
    uint cursor;
    uint end;
};

uint SpatialClusterLinearIndex(uint2 pixelCoord, float viewDepth, float4 clusterParams, float4 zSliceParams)
{
    uint tileSize = max((uint)zSliceParams.w, 1u);
    uint tilesX = max((uint)clusterParams.z, 1u);
    uint tilesY = max((uint)clusterParams.w, 1u);
    uint zSlices = max((uint)zSliceParams.x, 1u);

    uint2 tile = min(pixelCoord / tileSize, uint2(tilesX - 1u, tilesY - 1u));
    float sliceF = floor(log2(max(viewDepth, 1e-6)) * zSliceParams.y + zSliceParams.z);
    uint zSlice = (uint)clamp(sliceF, 0.0, (float)(zSlices - 1u));
    return (zSlice * tilesY + tile.y) * tilesX + tile.x;
}

// Initializes caller-owned cluster iteration state via out parameter for Metal cross-compiler compatibility.
void SpatialBeginClusterLoop(
    uint2 pixelCoord,
    float viewDepth,
    int itemCount,
    float4 clusterParams,
    float4 zSliceParams,
    StructuredBuffer<uint2> headers,
    out SpatialClusterLoopState state)
{
    state = (SpatialClusterLoopState)0;
    if (itemCount <= 0)
    {
        return;
    }
    uint cluster = SpatialClusterLinearIndex(pixelCoord, viewDepth, clusterParams, zSliceParams);
    uint2 header = headers[cluster];
    state.cursor = header.x;
    state.end = header.x + header.y;
}

bool SpatialNextClusterItem(inout SpatialClusterLoopState state, StructuredBuffer<uint> indices, out uint itemIndex)
{
    itemIndex = 0;
    bool found = state.cursor < state.end;
    if (found)
    {
        itemIndex = indices[state.cursor];
        state.cursor++;
    }
    return found;
}

#endif
