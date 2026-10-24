#ifndef _GPUSceneInclude
#define _GPUSceneInclude

#include "Common.hlsl"
#include "Geometry.hlsl"

// Immutable scene-owned transform version (current local-to-world matrices).
struct FTransformData
{
     float4x4 matrix_LocalToWorld;
};

uint instanceIndexOffset;
StructuredBuffer<uint> instanceIndexBuffer;
StructuredBuffer<FTransformData> transformBuffer;
StructuredBuffer<FTransformData> previousTransformBuffer;
StructuredBuffer<uint> transformGenerationBuffer;
StructuredBuffer<uint> previousTransformGenerationBuffer;
uint meshPreviousTransformCount, meshPreviousTransformValid;

float4x4 MeshPreviousObjectToWorld(uint transformIndex)
{
    float4x4 previous = (float4x4)0;
    if (meshPreviousTransformValid != 0 && transformIndex < meshPreviousTransformCount)
    {
        uint generation = transformGenerationBuffer[transformIndex];
        if (generation != 0 && generation == previousTransformGenerationBuffer[transformIndex])
            previous = previousTransformBuffer[transformIndex].matrix_LocalToWorld;
    }
    return previous;
}
StructuredBuffer<uint> renderingLayerBuffer;

float MeshTransformSign(float4x4 objectToWorld)
{
    return determinant((float3x3)objectToWorld) < 0 ? -1 : 1;
}

float3 MeshNormalToWorld(float4x4 objectToWorld, float3 normal)
{
    float3 x = float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20);
    float3 y = float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21);
    float3 z = float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22);
    float determinant = dot(x, cross(y, z));
    return normalize((cross(y, z) * normal.x + cross(z, x) * normal.y + cross(x, y) * normal.z) * (determinant < 0 ? -1 : 1));
}

#endif
