#ifndef INFINITY_SCREEN_SPACE_SURFACE_METADATA
#define INFINITY_SCREEN_SPACE_SURFACE_METADATA
#include "GBufferPack.hlsl"
#include "ScreenSpaceDepth.hlsl"

// R32G32_UInt: exact raw-depth bits; oct12x2 + age7 + valid1. Integer Load only.
uint2 EncodeSurfaceMetadata(float3 normalWS, float rawDepth, uint age, bool valid)
{
    uint2 oct = (uint2)round(saturate(UnitVectorToOctahedron(normalWS) * 0.5 + 0.5) * 4095.0);
    return uint2(asuint(rawDepth), oct.x | (oct.y << 12) | (min(age, 64u) << 24) | (valid ? 0x80000000u : 0u));
}
float4 DecodeSurfaceMetadata(uint2 packed)
{
    float2 oct = float2(packed.y & 4095u, (packed.y >> 12) & 4095u) * (2.0 / 4095.0) - 1.0;
    return float4(oct, asfloat(packed.x), (packed.y & 0x80000000u) != 0 ? (float)((packed.y >> 24) & 127u) : 0.0);
}
float4 LoadSurfaceMetadata(Texture2D<uint2> surfaceTexture, float4 textureView, float2 uv)
{
    uint width, height; surfaceTexture.GetDimensions(width, height);
    if (textureView.x > 0) { width = (uint)textureView.x; height = (uint)textureView.y; }
    return DecodeSurfaceMetadata(surfaceTexture.Load(int3(clamp((int2)(uv * uint2(width, height)), 0, int2(width, height) - 1), 0)));
}
float SurfaceHistoryDepthConfidence(Texture2D<uint2> surfaceTexture, float4 textureView, float2 uv, float expectedDepth)
{
    float confidence = 0;
    if (all(uv >= 0) && all(uv < 1))
    {
        uint width, height; surfaceTexture.GetDimensions(width, height);
        if (textureView.x > 0) { width = (uint)textureView.x; height = (uint)textureView.y; }
        int2 pixel = (int2)(uv * uint2(width, height));
        float low = ScreenSpaceDepthParams.y, high = ScreenSpaceDepthParams.x;
        [unroll] for (int y = -1; y <= 1; ++y)
        [unroll] for (int x = -1; x <= 1; ++x)
        {
            uint2 data = surfaceTexture.Load(int3(clamp(pixel + int2(x, y), 0, int2(width, height) - 1), 0));
            float depth = ScreenSpaceLinearEyeDepth(asfloat(data.x));
            low = min(low, depth); high = max(high, depth);
        }
        float expected = ScreenSpaceLinearEyeDepth(expectedDepth);
        float margin = 0.5 * (high - low) + max(0.01 * expected, 0.001);
        confidence = expected >= low - margin && expected <= high + margin ? 1 : 0;
    }
    return confidence;
}
#endif
