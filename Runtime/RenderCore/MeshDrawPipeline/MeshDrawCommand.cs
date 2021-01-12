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
    public struct FMeshDrawCommand
    {
        internal int SubmeshIndex;
        internal SharedRef<Mesh> DrawMesh;
        internal SharedRef<Material> DrawMaterial;
        internal NativeList<int> MeshBatchIndexs;
        internal int InstanceCount { get { return MeshBatchIndexs.Length; } }

        public FMeshDrawCommand(in FMeshBatch MeshBatch, ref NativeList<int> InMeshBatchIndexs)
        {
            DrawMesh = MeshBatch.Mesh;
            DrawMaterial = MeshBatch.Material;
            SubmeshIndex = MeshBatch.SubmeshIndex;
            MeshBatchIndexs = InMeshBatchIndexs;
        }

        public void Release()
        {
            MeshBatchIndexs.Dispose();
        }
    }
}
