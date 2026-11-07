#ifndef _GPUSceneInclude
#define _GPUSceneInclude

#include "Common.hlsl"
#include "Geometry.hlsl"

// Mesh entity interface. A draw list binds InstanceToEntity (its sorted or compacted entity slots) and the world's
// immutable transform version; a shader reads its entity once per vertex through MeshInstanceEntity.
struct FTransformData
{
     float4x4 matrix_LocalToWorld;
};

uint InstanceOffset;
StructuredBuffer<uint> InstanceToEntity;
StructuredBuffer<FTransformData> EntityTransforms;
StructuredBuffer<uint> EntityGenerations;
StructuredBuffer<uint> EntityRenderingLayers;
StructuredBuffer<FTransformData> PreviousEntityTransforms;
StructuredBuffer<uint> PreviousEntityGenerations;
uint PreviousEntityCount, PreviousEntityValid;

uint MeshInstanceEntity(uint instanceId)
{
    return InstanceToEntity[instanceId + InstanceOffset];
}

// Zero when the view has no accepted previous version or the slot was reused since then.
float4x4 MeshPreviousObjectToWorld(uint entity)
{
    float4x4 previous = (float4x4)0;
    if (PreviousEntityValid != 0 && entity < PreviousEntityCount)
    {
        uint generation = EntityGenerations[entity];
        if (generation != 0 && generation == PreviousEntityGenerations[entity])
            previous = PreviousEntityTransforms[entity].matrix_LocalToWorld;
    }
    return previous;
}

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
