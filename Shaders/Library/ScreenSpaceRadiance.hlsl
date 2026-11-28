#ifndef INFINITY_SCREEN_SPACE_RADIANCE
#define INFINITY_SCREEN_SPACE_RADIANCE

float4 LoadScreenSpaceRadiance(Texture2DArray<float2> radianceTexture, int2 pixel, int mip)
{
    return float4(radianceTexture.Load(int4(pixel, 0, mip)), radianceTexture.Load(int4(pixel, 1, mip)));
}

float4 SampleScreenSpaceRadiance(Texture2DArray<float2> radianceTexture, SamplerState samplerState, float2 uv, int mip)
{
    return float4(radianceTexture.SampleLevel(samplerState, float3(uv, 0), mip),
                  radianceTexture.SampleLevel(samplerState, float3(uv, 1), mip));
}

void StoreScreenSpaceRadiance(RWTexture2DArray<float2> radianceTexture, uint2 pixel, float4 colorAndConfidence)
{
    radianceTexture[uint3(pixel, 0)] = colorAndConfidence.rg;
    radianceTexture[uint3(pixel, 1)] = colorAndConfidence.ba;
}

#endif
