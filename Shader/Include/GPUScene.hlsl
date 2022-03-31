#ifndef _GPUSceneInclude
#define _GPUSceneInclude

#include "Common.hlsl"
#include "Geometry.hlsl"

struct FMeshBatch
{
     float4x4 matrix_LocalToWorld;
};

uint meshBatchOffset;
StructuredBuffer<uint> meshBatchIndexs;
StructuredBuffer<FMeshBatch> meshBatchBuffer;

#endif