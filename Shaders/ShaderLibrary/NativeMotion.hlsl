#ifndef INFINITY_NATIVE_MOTION
#define INFINITY_NATIVE_MOTION
float4x4 NativePreviousObjectToWorld()
{
    return unity_MatrixPreviousM;
}

float4 NativePreviousPosition(float4 vertex, uint vertexId)
{
    return mul(unity_MatrixPreviousM, vertex);
}
#endif
