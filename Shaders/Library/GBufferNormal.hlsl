// GBuffer normal

#ifndef INFINITY_GBUFFER_NORMAL
#define INFINITY_GBUFFER_NORMAL

// The octahedral world normal of GBuffer B and of the screen-space surface metadata. It depends on no other library, so a
// pass with its own common header (GTAO) decodes the GBuffer normal with the same functions as every other reader.

float2 UnitVectorToOctahedron(float3 N)
{
    N.xy /= dot(1, abs(N));
    if (N.z <= 0)
    {
        N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? 1 : -1);
    }
    return N.xy;
}

float3 OctahedronToUnitVector(float2 Oct)
{
    float3 N = float3(Oct, 1 - dot(1, abs(Oct)));
    if (N.z < 0)
    {
        N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? float2(1, 1) : float2(-1, -1));
    }
    return normalize(N);
}

float2 EncodeOctahedralNormal(float3 normalWS)
{
    float lenSq = dot(normalWS, normalWS);
    if (lenSq <= 1e-8)
    {
        return float2(0.5, 0.5);
    }
    return UnitVectorToOctahedron(normalWS * rsqrt(lenSq)) * 0.5 + 0.5;
}

float3 DecodeOctahedralNormal(float2 encoded)
{
    return OctahedronToUnitVector(encoded * 2.0 - 1.0);
}

// GBuffer B xy (R8G8B8A8_UNorm): octahedral normal 8+8.
float3 DecodeGBufferNormal(float4 gBufferB)
{
    return DecodeOctahedralNormal(gBufferB.rg);
}

#endif
