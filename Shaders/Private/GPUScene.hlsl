#ifndef _GPUScene_
#define _GPUScene_

#include "Common.hlsl"
#include "Geometry.hlsl"

struct FMeshbatch
{
     int SubmeshIndex;
     int MeshIndex;
     int MaterialIndex;
     int CastShadow;
     int MotionType;
     int Visible;
     int Priority;
     int RenderLayer;
     FBound BoundBox;
     float4x4 Matrix_Model;
};

uint _Offset;
Buffer<uint> _Indexs;
StructuredBuffer<FMeshbatch> _Primitives;


#endif