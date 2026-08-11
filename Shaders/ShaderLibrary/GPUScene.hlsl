#ifndef _GPUSceneInclude
#define _GPUSceneInclude

#include "Common.hlsl"
#include "Geometry.hlsl"

// Transform table uploaded by MeshSceneResidency (current local-to-world matrices).
struct FTransformData
{
     float4x4 matrix_LocalToWorld;
};

uint instanceIndexOffset;
StructuredBuffer<uint> instanceIndexBuffer;
StructuredBuffer<FTransformData> transformBuffer;
StructuredBuffer<FTransformData> previousTransformBuffer;

#endif
