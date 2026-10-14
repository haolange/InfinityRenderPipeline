using Unity.Mathematics;

namespace InfinityTech.Rendering.Pipeline
{
    // Trace resolution and tile grid of the stochastic screen-space signals (SSR, SSGI). The HLSL side is
    // Shaders/ShaderLibrary/RayTracing/ScreenSpaceRayTrace/ScreenSpaceTraceTiles.hlsl.
    internal static class ScreenSpaceTraceUtility
    {
        internal const int TileSize = 8;
        internal const int ResolveGroupSize = 8;

        internal static int2 TraceSize(int2 fullSize, bool halfResolution) =>
            halfResolution ? math.max(new int2(1, 1), (fullSize + 1) / 2) : fullSize;

        internal static int2 TileCount(int2 traceSize) => (traceSize + (TileSize - 1)) / TileSize;
    }
}
