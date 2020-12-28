using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Runtime.Core;
using InfinityTech.Runtime.Core.Geometry;
using InfinityTech.Runtime.Rendering.Core;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    internal struct FMeshDrawCommand
    {
        internal NativeList<int> MeshBatchIndexBuffer;

        public void Init()
        {
            MeshBatchIndexBuffer = new NativeList<int>(8192, Allocator.Persistent);
        }

        public void Reset()
        {
            MeshBatchIndexBuffer.Clear();
        }

        public void DrawMesh(FRenderWorld World, CommandBuffer CmdBuffer, FMeshBatch MeshBatch, in int PassIndex)
        {
            Mesh DrawMesh = World.WorldMeshList.Get(MeshBatch.Mesh);
            Material DrawMaterial = World.WorldMaterialList.Get(MeshBatch.Material);
            CmdBuffer.DrawMeshInstancedProcedural(DrawMesh, MeshBatch.SubmeshIndex, DrawMaterial, PassIndex, MeshBatchIndexBuffer.Length);
        }

        public void Release()
        {
            MeshBatchIndexBuffer.Dispose();
        }
    }
}
