#ifndef _GPUScene_
#define _GPUScene_

#include "Common.hlsl"
#include "Geometry.hlsl"

struct FMeshBatch
{
     int submeshIndex;
     int meshIndex;
     int materialIndex;
     int visible;
     int priority;
     int castShadow;
     int motionType;
     int renderLayer;
     FBound boundBox;
     float4x4 matrix_LocalToWorld;
};

uint meshBatchOffset;
Buffer<uint> meshBatchIndexs;
StructuredBuffer<FMeshBatch> meshBatchBuffer;


#endif