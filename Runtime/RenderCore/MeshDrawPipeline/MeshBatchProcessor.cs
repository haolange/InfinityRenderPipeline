using System;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Runtime.Core;
using InfinityTech.Runtime.Core.Geometry;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    interface ParallelPassContexBase : IJobParallelFor
    {
        void AddMeshBatchRef(in FVisibleMeshBatch VisibleMeshBatch);
    }

    [BurstCompile]
    internal struct ParallelPassContexFilter : ParallelPassContexBase
    {
        [ReadOnly]
        public NativeArray<FMeshBatch> MeshBatchArray;

        [ReadOnly]
        public NativeArray<FVisibleMeshBatch> VisibleMeshBatchArray;

        [NativeDisableParallelForRestriction]
        public NativeHashMap<int, FMeshDrawCommand> PassDrawContextList;

        public void AddMeshBatchRef(in FVisibleMeshBatch VisibleMeshBatch)
        {
            FMeshBatch MeshBatch = MeshBatchArray[VisibleMeshBatch.index];
        }

        public void Execute(int index)
        {
            FVisibleMeshBatch VisibleMeshBatch = VisibleMeshBatchArray[index];
            AddMeshBatchRef(VisibleMeshBatch);
        }
    }

    internal class FMeshBatchProcessor<T> where T : struct
    {
        protected NativeArray<FMeshBatch> MeshBatchArray;
        protected NativeHashMap<int, FMeshDrawCommand> PassDrawContextList;

        internal FMeshBatchProcessor()
        {
            PassDrawContextList = new NativeHashMap<int, FMeshDrawCommand>(1024, Allocator.Persistent);
        }

        internal void Reset()
        {
            PassDrawContextList.Clear();
        }

        internal void BuildMesh()
        {

        }

        internal void DispatchMesh()
        {

        }

        internal void Release()
        {
            PassDrawContextList.Dispose();
        }
    }
}
