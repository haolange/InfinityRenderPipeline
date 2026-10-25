using System.Collections.Generic;
using InfinityTech.Component;
using InfinityTech.Core.Geometry;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class RenderContext
    {
        private static readonly List<MeshComponent> s_TransformUpdates = new List<MeshComponent>(256);
        private TransformAccessArray m_DynamicTransforms;
        private NativeArray<float4x4> m_DynamicMatrices;
        private NativeArray<byte> m_DynamicChanged;
        private NativeArray<Bounds> m_LocalBounds;
        private NativeArray<FAABB> m_WorldBounds;

        [BurstCompile]
        private struct MeshTransformReadJob : IJobParallelForTransform
        {
            public NativeArray<float4x4> matrices;
            public NativeArray<byte> changed;
            [ReadOnly] public NativeArray<Bounds> localBounds;
            public NativeArray<FAABB> worldBounds;
            public void Execute(int index, TransformAccess transform)
            {
                float4x4 matrix = transform.localToWorldMatrix;
                changed[index] = (byte)(matrix.Equals(matrices[index]) ? 0 : 1);
                matrices[index] = matrix;
                Bounds bounds = localBounds[index];
                float3 extent = bounds.extents;
                worldBounds[index] = new FAABB(math.transform(matrix, bounds.center),
                    2 * (math.abs(matrix.c0.xyz) * extent.x + math.abs(matrix.c1.xyz) * extent.y + math.abs(matrix.c2.xyz) * extent.z));
            }
        }

        internal static void EnqueueMeshTransform(MeshComponent mesh) => s_TransformUpdates.Add(mesh);

        void AddDynamicTransform(MeshComponent mesh)
        {
            if (!m_DynamicTransforms.isCreated) m_DynamicTransforms = new TransformAccessArray(256);
            int required = m_DynamicMeshList.Count;
            if (!m_DynamicMatrices.IsCreated || required > m_DynamicMatrices.Length)
            {
                int capacity = math.ceilpow2(math.max(256, required));
                var matrices = new NativeArray<float4x4>(capacity, Allocator.Persistent);
                if (m_DynamicMatrices.IsCreated)
                {
                    NativeArray<float4x4>.Copy(m_DynamicMatrices, matrices, m_DynamicMatrices.Length);
                    m_DynamicMatrices.Dispose(); m_DynamicChanged.Dispose();
                    m_LocalBounds.Dispose(); m_WorldBounds.Dispose();
                }
                m_DynamicMatrices = matrices;
                m_DynamicChanged = new NativeArray<byte>(capacity, Allocator.Persistent);
                m_LocalBounds = new NativeArray<Bounds>(capacity, Allocator.Persistent);
                m_WorldBounds = new NativeArray<FAABB>(capacity, Allocator.Persistent);
                for (int i = 0; i < required; ++i) m_LocalBounds[i] = m_DynamicMeshList[i].localBounds;
            }
            m_DynamicTransforms.Add(mesh.transform);
            m_DynamicMatrices[required - 1] = mesh.transform.localToWorldMatrix;
            m_LocalBounds[required - 1] = mesh.localBounds;
        }

        void RemoveDynamicTransform(int index, int last)
        {
            m_DynamicTransforms.RemoveAtSwapBack(index);
            m_DynamicMatrices[index] = m_DynamicMatrices[last];
            m_LocalBounds[index] = m_LocalBounds[last]; m_WorldBounds[index] = m_WorldBounds[last];
        }

        void UpdateDynamicTransforms()
        {
            using var measurement = new PipelineFrameMeasurement.Section(0);
            if (!m_DynamicTransforms.isCreated || m_DynamicTransforms.length == 0) return;
            foreach (var mesh in s_DirtyMeshList)
                if (mesh && mesh.dynamicIndex >= 0) m_LocalBounds[mesh.dynamicIndex] = mesh.localBounds;
            new MeshTransformReadJob { matrices = m_DynamicMatrices, changed = m_DynamicChanged, localBounds = m_LocalBounds, worldBounds = m_WorldBounds }
                .ScheduleReadOnly(m_DynamicTransforms, 64).Complete();
            for (int i = 0; i < m_DynamicMeshList.Count; ++i)
                if (m_DynamicChanged[i] != 0)
                {
                    var mesh = m_DynamicMeshList[i];
                    mesh.MarkTransformDirty(m_DynamicMatrices[i].c3.xyz);
                }
        }

        internal void ApplyMeshTransforms()
        {
            using var measurement = new PipelineFrameMeasurement.Section(2);
            if (s_TransformUpdates.Count == 0) return;
            using (var update = m_MeshScene.BeginUpdate())
            {
                foreach (var mesh in s_TransformUpdates)
                {
                    if (!mesh) continue;
                    if (!mesh.isActiveAndEnabled) continue;
                    var matrix = mesh.dynamicIndex >= 0 ? m_DynamicMatrices[mesh.dynamicIndex] : (float4x4)mesh.transform.localToWorldMatrix;
                    FAABB bounds = mesh.dynamicIndex >= 0 ? m_WorldBounds[mesh.dynamicIndex] : (FAABB)Geometry.CaculateWorldBound(mesh.localBounds, matrix);
                    mesh.ApplyTransform(update, matrix, bounds);
                }
                update.Commit();
            }
            foreach (var mesh in s_TransformUpdates) if (mesh) mesh.AcceptTransform();
            s_TransformUpdates.Clear();
        }

        void DisposeTransformBatch()
        {
            if (m_DynamicTransforms.isCreated) m_DynamicTransforms.Dispose();
            if (m_DynamicMatrices.IsCreated) { m_DynamicMatrices.Dispose(); m_DynamicChanged.Dispose(); m_LocalBounds.Dispose(); m_WorldBounds.Dispose(); }
            foreach (var mesh in s_TransformUpdates) if (mesh) mesh.transformPending = false;
            s_TransformUpdates.Clear();
        }
    }
}
