#ifndef INFINITY_LIGHT_LOOP
#define INFINITY_LIGHT_LOOP
// Shared clustered light loop for Deferred, Forward and the volumetrics built on SpatialZBinTile.
// Local index i is record g_DirectionalLightCount + i.

#define LIGHT_TILE_SIZE 16u

#include "LightLoopUniform.hlsl"
#include "../Spatial/SpatialZBinTile.hlsl"
StructuredBuffer<uint> SRV_LightZBins;
StructuredBuffer<uint> SRV_LightTileMasks;

#define LightLoopState SpatialZBinTileState

uint LightZBin(float viewDepth)
{
    return SpatialDepthToZBin(viewDepth, g_LightZBinParams);
}

void BeginLightLoop(uint2 pixelCoord, float viewDepth, out LightLoopState loop)
{
    SpatialBeginZBinTileLoop(pixelCoord, viewDepth, g_LocalLightCount, LIGHT_TILE_SIZE,
        g_LightTileParams, g_LightZBinParams, SRV_LightZBins, SRV_LightTileMasks, loop);
}

void BeginLightLoopUV(float2 screenUV, float viewDepth, out LightLoopState loop)
{
    BeginLightLoop((uint2)(saturate(screenUV) * g_LightTileParams.xy), viewDepth, loop);
}

bool NextLight(inout LightLoopState loop, out uint recordIndex)
{
    recordIndex = 0;
    uint localIndex = 0;
    bool found = SpatialNextZBinTileItem(loop, SRV_LightTileMasks, localIndex);
    if (found)
    {
        recordIndex = (uint)g_DirectionalLightCount + localIndex;
    }
    return found;
}
#endif
