#ifndef _GPUSceneInclude
#define _GPUSceneInclude

#include "Common.hlsl"
#include "Geometry.hlsl"

struct FMeshBatch
{
     int sectionIndex;
     int meshIndex;
     int materialIndex;
     int visible;
     int priority;
     int castShadow;
     int motionType;
     int renderLayer;
     int pending1;
     int pending2;
     FBound boundBox;
     float4x4 matrix_LocalToWorld;
};

uint meshBatchOffset;
Buffer<uint> meshBatchIndexs;
StructuredBuffer<FMeshBatch> meshBatchBuffer;

#endif