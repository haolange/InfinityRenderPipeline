// Full-screen triangle

#ifndef INFINITY_FULL_SCREEN_TRIANGLE
#define INFINITY_FULL_SCREEN_TRIANGLE

// One procedural triangle covers the viewport: corners (-1,-1), (-1,3), (3,-1) from SV_VertexID 0, 1 and 2.
// Clip y is flipped and the uv keeps the unflipped corner, the convention every full-screen pass of the package shares.
float2 FullScreenTriangleCorner(uint vertexID)
{
    return float2(vertexID == 2u ? 3.0 : -1.0, vertexID == 1u ? 3.0 : -1.0);
}

float4 FullScreenTrianglePosition(uint vertexID)
{
    float2 corner = FullScreenTriangleCorner(vertexID);
    return float4(corner.x, -corner.y, 0.0, 1.0);
}

float2 FullScreenTriangleUV(uint vertexID)
{
    return (FullScreenTriangleCorner(vertexID) + 1.0) * 0.5;
}

#endif
