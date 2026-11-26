using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.Spatial;

namespace InfinityTech.Rendering.Reflection
{
    internal static class ReflectionShaderIDs
    {
        public static readonly int ReflectionProbes = Shader.PropertyToID("SRV_ReflectionProbes");
        public static readonly int ProbeClusterHeaders = Shader.PropertyToID("SRV_ProbeClusterHeaders");
        public static readonly int ProbeClusterIndices = Shader.PropertyToID("SRV_ProbeClusterIndices");
        public static readonly int ReflectionProbeAtlas = Shader.PropertyToID("SRV_ReflectionProbeAtlas");
        public static readonly GlobalKeyword ReflectionProbesKeyword = GlobalKeyword.Create("INFINITY_REFLECTION_PROBES");

        public static readonly RGResourceKey ReflectionProbeBuffer = new RGResourceKey("_ReflectionProbeBuffer");
        public static readonly RGResourceKey ProbeClusterHeaderBuffer = new RGResourceKey("_ProbeClusterHeaderBuffer");
        public static readonly RGResourceKey ProbeClusterIndexBuffer = new RGResourceKey("_ProbeClusterIndexBuffer");
    }

    // One reflection probe of the camera's priority-ordered list (higher importance, then smaller volume).
    // GPU-RECORD: ReflectionProbeRecord Shaders/Library/Lighting/ReflectionProbes.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct ReflectionProbeRecord
    {
        // World to influence-box space (rows of a 3x4 affine).
        public Vector4 worldToBox0;
        public Vector4 worldToBox1;
        public Vector4 worldToBox2;
        // xyz = influence half extents including the blend band (sphere: radius in x); w = blend distance.
        public Vector4 extents;
        // xyz = capture position in box space; w = 1 with parallax projection.
        public Vector4 capture;
        // xyz = projection half extents (sphere: radius in x).
        public Vector4 projection;
        // x = intensity (HDR decode multiplier); y = first atlas slice; z = atlas mip count - 1; w = 1 sphere influence.
        public Vector4 parameters;
    }

    // Camera-constant reflection cluster parameters uploaded once before the camera graph and bound globally.
    // GPU-RECORD: ReflectionUniform Shaders/Library/Lighting/ReflectionProbes.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct ReflectionUniform
    {
        public Vector4 clusterParams;
        public Vector4 zSliceParams;
        public int probeCount;
        public int padding0;
        public int padding1;
        public int padding2;
    }

    /// <summary>
    /// Per-camera view layer for reflection probes, built on the shared SpatialClusterIndex to preserve priority order.
    /// </summary>
    internal sealed class ReflectionContext : IDisposable
    {
        public const int ClusterTileSize = 64;
        public const int ClusterZSlices = 16;

        static readonly int s_ProbeStride = Marshal.SizeOf<ReflectionProbeRecord>();

        readonly SpatialClusterIndex m_ClusterIndex = new SpatialClusterIndex();
        readonly List<ReflectionProbeRecord> m_Probes = new List<ReflectionProbeRecord>();
        readonly List<int> m_ProbeOrder = new List<int>();
        readonly List<GraphicsBuffer> m_RetiredBuffers = new List<GraphicsBuffer>();
        ReflectionProbeRecord[] m_ProbeUpload = new ReflectionProbeRecord[1];
        GraphicsBuffer m_ProbeBuffer;
        int m_ProbeCapacity;
        int m_RasterReflectionCount;

        NativeArray<VisibleReflectionProbe> m_ProbeSortInput;
        Comparison<int> m_ProbeComparison;

        internal ReflectionContext()
        {
            m_ProbeCapacity = 1;
            m_ProbeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_ProbeCapacity, s_ProbeStride);
        }

        internal int ProbeCount => m_Probes.Count;
        internal int RasterReflectionCount => m_RasterReflectionCount;
        internal int ClusterCount => m_ClusterIndex.ClusterCount;
        internal int ClusterIndexCount => m_ClusterIndex.IndexCount;
        internal GraphicsBuffer ProbeBuffer => m_ProbeBuffer;
        internal GraphicsBuffer ClusterHeaderBuffer => m_ClusterIndex.HeaderBuffer;
        internal GraphicsBuffer ClusterIndexBuffer => m_ClusterIndex.IndexBuffer;
        internal int FirstSlice(int index) => (int)m_Probes[index].parameters.y;

        internal void FlushRetiredBuffers()
        {
            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
            {
                m_RetiredBuffers[i].Release();
            }
            m_RetiredBuffers.Clear();
            m_ClusterIndex.FlushRetired();
        }

        int CompareProbes(int a, int b)
            => ReflectionProbePriority.Compare(
                m_ProbeSortInput[a].importance, ReflectionProbePriority.Volume(m_ProbeSortInput[a].bounds),
                m_ProbeSortInput[b].importance, ReflectionProbePriority.Volume(m_ProbeSortInput[b].bounds));

        internal void Build(
            ReflectionProbeAtlas atlas,
            SceneTable<InfinityAdditionalReflectionProbeData> probeTable,
            int rasterReflectionCount,
            in CullingResults cullingResults,
            Camera camera,
            int2 screenSize,
            int frame)
        {
            m_Probes.Clear();
            m_ProbeOrder.Clear();
            m_RasterReflectionCount = math.max(rasterReflectionCount, 0);

            int2 safeScreen = math.max(screenSize, new int2(1, 1));
            float near = camera != null ? camera.nearClipPlane : 0.1f;
            float far = camera != null ? camera.farClipPlane : 1000.0f;
            Matrix4x4 worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 gpuProjection = camera != null ? GraphicsConventions.GetComputeGpuProjection(camera) : Matrix4x4.identity;
            bool orthographic = camera != null && camera.orthographic;

            m_ClusterIndex.BeginBuild(safeScreen, near, far, ClusterTileSize, ClusterZSlices);

            NativeArray<VisibleReflectionProbe> visible = cullingResults.visibleReflectionProbes;
            for (int i = 0; i < visible.Length; ++i)
            {
                m_ProbeOrder.Add(i);
            }
            m_ProbeSortInput = visible;
            if (m_ProbeComparison == null)
            {
                m_ProbeComparison = CompareProbes;
            }
            m_ProbeOrder.Sort(m_ProbeComparison);
            m_ProbeSortInput = default;

            for (int order = 0; atlas != null && atlas.Enabled && order < m_ProbeOrder.Count && m_Probes.Count < SpatialProjection.MaxIndexedItems; ++order)
            {
                VisibleReflectionProbe probe = visible[m_ProbeOrder[order]];
                ReflectionProbe component = probe.reflectionProbe;
                Texture texture = probe.texture;
                if (component == null || texture == null || texture.dimension != TextureDimension.Cube)
                {
                    continue;
                }

                Bounds bounds = probe.bounds;
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                float blend = math.max(probe.blendDistance, 0.0f);
                probeTable.TryGet(UnityEntityId.ToUInt64(component), out InfinityAdditionalReflectionProbeData ext);
                EReflectionProbeInfluence influence = ext != null ? ext.influence : EReflectionProbeInfluence.Box;
                bool sphere = influence == EReflectionProbeInfluence.Sphere;
                bool orientedBox = influence == EReflectionProbeInfluence.OrientedBox;
                float radius = sphere ? (ext.sphereRadius > 0 ? ext.sphereRadius : math.cmax(new float3(extents.x, extents.y, extents.z))) : 0.0f;

                Quaternion rotation = orientedBox ? component.transform.rotation : Quaternion.identity;
                SpatialBounds influenceBounds = sphere
                    ? SpatialVolume.CreateSphere(center, radius + blend).ComputeWorldBounds()
                    : SpatialVolume.CreateBox(center, rotation, extents + Vector3.one * blend).ComputeWorldBounds();
                if (!SpatialProjection.TryProjectBoundsToTileRect(in influenceBounds, in worldToView, in gpuProjection,
                    orthographic, near, safeScreen, ClusterTileSize, out int4 rect, out float2 depthRange))
                {
                    continue;
                }

                bool realtime = component.mode == ReflectionProbeMode.Realtime;
                var key = new ProbeCubeKey(UnityEntityId.ToUInt64(component), UnityEntityId.ToUInt64(texture));
                if (!atlas.Acquire(key, texture, texture.updateCount, realtime, frame, out int slot))
                {
                    continue;
                }

                // Orthonormal world-to-box 3x4 affine rows (identity when axis-aligned, R^T when OrientedBox).
                Matrix4x4 rotMatrix = Matrix4x4.Rotate(Quaternion.Inverse(rotation));
                Vector3 row0 = new Vector3(rotMatrix.m00, rotMatrix.m01, rotMatrix.m02);
                Vector3 row1 = new Vector3(rotMatrix.m10, rotMatrix.m11, rotMatrix.m12);
                Vector3 row2 = new Vector3(rotMatrix.m20, rotMatrix.m21, rotMatrix.m22);
                Vector4 worldToBox0 = new Vector4(row0.x, row0.y, row0.z, -Vector3.Dot(row0, center));
                Vector4 worldToBox1 = new Vector4(row1.x, row1.y, row1.z, -Vector3.Dot(row1, center));
                Vector4 worldToBox2 = new Vector4(row2.x, row2.y, row2.z, -Vector3.Dot(row2, center));

                // Projection proxy is independent of influence shape when explicitly configured.
                float projectionMode;
                Vector4 projectionVec;
                EReflectionProjectionProxy proxyMode = ext != null ? ext.projectionProxy : EReflectionProjectionProxy.Auto;
                if (proxyMode == EReflectionProjectionProxy.None)
                {
                    projectionMode = 0.0f;
                    projectionVec = Vector4.zero;
                }
                else if (proxyMode == EReflectionProjectionProxy.Box)
                {
                    projectionMode = 1.0f;
                    Vector3 pe = Vector3.Max(ext.projectionBoxExtents, new Vector3(1e-3f, 1e-3f, 1e-3f));
                    projectionVec = new Vector4(pe.x, pe.y, pe.z, 0.0f);
                }
                else if (proxyMode == EReflectionProjectionProxy.Sphere)
                {
                    projectionMode = 2.0f;
                    float pr = ext.projectionSphereRadius > 0.0f ? ext.projectionSphereRadius : (sphere ? radius : math.cmax(new float3(extents.x, extents.y, extents.z)));
                    projectionVec = new Vector4(pr, 0.0f, 0.0f, 0.0f);
                }
                else
                {
                    projectionMode = probe.isBoxProjection ? (sphere ? 2.0f : 1.0f) : 0.0f;
                    projectionVec = sphere ? new Vector4(radius, 0.0f, 0.0f, 0.0f) : new Vector4(extents.x, extents.y, extents.z, 0.0f);
                }

                Vector3 captureWS = ext != null && ext.useCustomCaptureOrigin
                    ? center + rotation * ext.captureOriginOffset
                    : component.transform.position;
                Vector4 capturePos4 = new Vector4(captureWS.x, captureWS.y, captureWS.z, 1.0f);
                Vector3 captureLocal = new Vector3(
                    Vector4.Dot(worldToBox0, capturePos4),
                    Vector4.Dot(worldToBox1, capturePos4),
                    Vector4.Dot(worldToBox2, capturePos4));

                var record = new ReflectionProbeRecord
                {
                    worldToBox0 = worldToBox0,
                    worldToBox1 = worldToBox1,
                    worldToBox2 = worldToBox2,
                    extents = sphere ? new Vector4(radius + blend, 0, 0, blend) : new Vector4(extents.x + blend, extents.y + blend, extents.z + blend, blend),
                    capture = new Vector4(captureLocal.x, captureLocal.y, captureLocal.z, projectionMode),
                    projection = projectionVec,
                    parameters = new Vector4(probe.hdrData.x, slot * 6, atlas.MipCount - 1, sphere ? 1 : 0),
                };

                m_Probes.Add(record);
                m_ClusterIndex.AddItem(rect, depthRange);
            }

            m_ClusterIndex.FinalizeBuild();

            if (m_ProbeCapacity < m_Probes.Count)
            {
                int nextCapacity = math.max(m_Probes.Count, m_ProbeCapacity * 2);
                var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nextCapacity, s_ProbeStride);
                if (m_ProbeBuffer != null)
                {
                    m_RetiredBuffers.Add(m_ProbeBuffer);
                }
                m_ProbeBuffer = replacement;
                m_ProbeCapacity = nextCapacity;
            }
        }

        internal ReflectionUniform ResolveUniform(bool enabled)
        {
            SpatialClusterGridDesc grid = m_ClusterIndex.Grid;
            return new ReflectionUniform
            {
                clusterParams = grid.clusterParams,
                zSliceParams = grid.zSliceParams,
                probeCount = enabled ? m_Probes.Count : 0
            };
        }

        internal void Upload(in RGTransferEncoder commands)
        {
            commands.SetBufferData(m_ClusterIndex.HeaderBuffer, m_ClusterIndex.Headers);
            commands.SetBufferData(m_ClusterIndex.IndexBuffer, m_ClusterIndex.Indices);
            if (m_Probes.Count > 0)
            {
                if (m_ProbeUpload.Length < m_Probes.Count)
                {
                    m_ProbeUpload = new ReflectionProbeRecord[m_Probes.Count];
                }
                m_Probes.CopyTo(m_ProbeUpload);
                commands.SetBufferData(m_ProbeBuffer, m_ProbeUpload, 0, 0, m_Probes.Count);
            }
        }

        public void Dispose()
        {
            FlushRetiredBuffers();
            m_ProbeBuffer?.Release();
            m_ProbeBuffer = null;
            m_ClusterIndex.Dispose();
        }
    }
}
