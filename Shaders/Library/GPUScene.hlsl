#ifndef _GPUSceneInclude
#define _GPUSceneInclude

#include "Common.hlsl"

// Mesh entity interface. A draw list binds InstanceToEntity (its sorted or compacted entity slots) and the world's
// immutable transform version; a shader reads its entity once per vertex through MeshInstanceEntity.
struct TransformData
{
     float4x4 matrix_LocalToWorld;
};

uint InstanceOffset;
StructuredBuffer<uint> InstanceToEntity;
StructuredBuffer<TransformData> EntityTransforms;
StructuredBuffer<uint> EntityGenerations;
StructuredBuffer<uint> EntityRenderingLayers;
StructuredBuffer<TransformData> PreviousEntityTransforms;
StructuredBuffer<uint> PreviousEntityGenerations;
uint PreviousEntityCount, PreviousEntityValid;

uint MeshInstanceEntity(uint instanceId)
{
    return InstanceToEntity[instanceId + InstanceOffset];
}

// Signed LOD cross-fade factor of a fading draw (INFINITY_LOD_DITHER): +f for the outgoing level, -f for the incoming one,
// in the convention of Core RP LODDitheringTransition.
StructuredBuffer<float> InstanceFade;

float MeshInstanceFade(uint instanceId)
{
    return InstanceFade[instanceId + InstanceOffset];
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

// Inverse of an affine object-to-world matrix. Column 0..2 are the axes and column 3 is the translation.
float4x4 MeshWorldToObject(float4x4 objectToWorld)
{
    float3 axisX = float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20);
    float3 axisY = float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21);
    float3 axisZ = float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22);
    float3 translation = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
    float inverseDeterminant = rcp(dot(axisX, cross(axisY, axisZ)));
    float3 row0 = cross(axisY, axisZ) * inverseDeterminant;
    float3 row1 = cross(axisZ, axisX) * inverseDeterminant;
    float3 row2 = cross(axisX, axisY) * inverseDeterminant;
    float4x4 worldToObject;
    worldToObject._m00_m01_m02_m03 = float4(row0, -dot(row0, translation));
    worldToObject._m10_m11_m12_m13 = float4(row1, -dot(row1, translation));
    worldToObject._m20_m21_m22_m23 = float4(row2, -dot(row2, translation));
    worldToObject._m30_m31_m32_m33 = float4(0, 0, 0, 1);
    return worldToObject;
}

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
void SetupInfinityInstance()
{
    uint entity = MeshInstanceEntity(unity_InstanceID);
    float4x4 objectToWorld = EntityTransforms[entity].matrix_LocalToWorld;
    unity_ObjectToWorld = objectToWorld;
    unity_WorldToObject = MeshWorldToObject(objectToWorld);
    unity_WorldTransformParams = float4(0, 0, 0, MeshTransformSign(objectToWorld));
    unity_RenderingLayer = float4(asfloat(EntityRenderingLayers[entity]), 0, 0, 0);
    unity_MatrixPreviousM = MeshPreviousObjectToWorld(entity);
    unity_MatrixPreviousMI = (float4x4)0;
}

uint InfinityProceduralRenderingLayer()
{
    return EntityRenderingLayers[MeshInstanceEntity(unity_InstanceID)];
}
#endif

float3 MeshNormalToWorld(float4x4 objectToWorld, float3 normal)
{
    float3 x = float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20);
    float3 y = float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21);
    float3 z = float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22);
    float determinant = dot(x, cross(y, z));
    return normalize((cross(y, z) * normal.x + cross(z, x) * normal.y + cross(x, y) * normal.z) * (determinant < 0 ? -1 : 1));
}

#endif
