#ifndef INFINITY_NATIVE_MOTION
#define INFINITY_NATIVE_MOTION
UNITY_INSTANCING_BUFFER_START(InfinityMotion)
    UNITY_DEFINE_INSTANCED_PROP(float4x4, InfinityPreviousObjectToWorld)
    UNITY_DEFINE_INSTANCED_PROP(float, InfinityPreviousVertexOffset)
UNITY_INSTANCING_BUFFER_END(InfinityMotion)
StructuredBuffer<float3> SRV_NativePreviousVertices;
float4x4 NativePreviousObjectToWorld()
{
    return UNITY_ACCESS_INSTANCED_PROP(InfinityMotion, InfinityPreviousObjectToWorld);
}
float4 NativePreviousPosition(float4 vertex, uint vertexId)
{
    int offset = (int)UNITY_ACCESS_INSTANCED_PROP(InfinityMotion, InfinityPreviousVertexOffset);
    if (offset >= 0) vertex = float4(SRV_NativePreviousVertices[offset + vertexId], 1);
    return mul(NativePreviousObjectToWorld(), vertex);
}
#endif
