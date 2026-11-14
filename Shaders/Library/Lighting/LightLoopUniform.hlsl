#ifndef INFINITY_LIGHT_LOOP_UNIFORM
#define INFINITY_LIGHT_LOOP_UNIFORM

// The camera's light-loop constants (C# LightLoopUniform), written once per camera and bound globally for raster and
// compute passes.
cbuffer LightLoopUniform
{
    // x, y = screen size of the tile grid, z = tiles per row, w = mask words per tile.
    float4 g_LightTileParams;
    // x = bin count, y = scale, z = bias: bin = floor(log2(depth) * scale + bias).
    float4 g_LightZBinParams;
    // x, y = screen size of the probe tile grid; z = tiles per row; w = mask words per tile.
    float4 g_ProbeTileParams;
    // The first g_DirectionalLightCount records are directional; the remaining g_LocalLightCount are local, sorted by
    // view depth. Light-list indices are absolute record indices.
    int g_DirectionalLightCount;
    int g_LocalLightCount;
    int g_SunRecordIndex;
    int g_ReflectionProbeCount;
};

#endif
