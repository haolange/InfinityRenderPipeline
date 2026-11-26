#ifndef INFINITY_SCREEN_SPACE_SURFACE_METADATA
#define INFINITY_SCREEN_SPACE_SURFACE_METADATA
#include "GBufferPack.hlsl"

// R32_UInt: oct12x2 normal + age7 + valid1. Integer Load only. No depth crosses frames: a history sample is validated by
// reprojection, the normal and the neighborhood, never by a stored depth.
uint EncodeSurfaceMetadata(float3 normalWS, uint age, bool valid)
{
    uint2 oct = (uint2)round(saturate(UnitVectorToOctahedron(normalWS) * 0.5 + 0.5) * 4095.0);
    return oct.x | (oct.y << 12) | (min(age, 64u) << 24) | (valid ? 0x80000000u : 0u);
}
// xy: octahedral normal in [-1, 1]; z: sample age, 0 for an invalid sample.
float3 DecodeSurfaceMetadata(uint packed)
{
    float2 oct = float2(packed & 4095u, (packed >> 12) & 4095u) * (2.0 / 4095.0) - 1.0;
    return float3(oct, (packed & 0x80000000u) != 0 ? (float)((packed >> 24) & 127u) : 0.0);
}
float3 LoadSurfaceMetadata(Texture2D<uint> surfaceTexture, float4 textureView, float2 uv)
{
    uint width, height; surfaceTexture.GetDimensions(width, height);
    if (textureView.x > 0) { width = (uint)textureView.x; height = (uint)textureView.y; }
    return DecodeSurfaceMetadata(surfaceTexture.Load(int3(clamp((int2)(uv * uint2(width, height)), 0, int2(width, height) - 1), 0)));
}
#endif
