using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.Spatial;

namespace InfinityTech.Rendering.Decal
{
    internal static class DecalShaderIDs
    {
        public static readonly int DecalRecords = Shader.PropertyToID("SRV_DecalRecords");
        public static readonly int DecalClusterHeaders = Shader.PropertyToID("SRV_DecalClusterHeaders");
        public static readonly int DecalClusterIndices = Shader.PropertyToID("SRV_DecalClusterIndices");

        public static readonly RGResourceKey DecalRecordBuffer = new RGResourceKey("_DecalRecordBuffer");
        public static readonly RGResourceKey DecalClusterHeaderBuffer = new RGResourceKey("_DecalClusterHeaderBuffer");
        public static readonly RGResourceKey DecalClusterIndexBuffer = new RGResourceKey("_DecalClusterIndexBuffer");
    }

    // One clustered decal record (drawOrder ascending priority).
    // GPU-RECORD: DecalRecord Shaders/Library/DecalCluster.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct DecalRecord
    {
        public Vector4 worldToBox0;
        public Vector4 worldToBox1;
        public Vector4 worldToBox2;
        public Vector4 albedoAlpha;
        public Vector4 materialParams;
        public Vector4 normalBlend;
        public Vector4 angleFadeParams;
        public Vector4 layerAndFlags;
    }

    // Camera-constant decal cluster parameters uploaded once before the camera graph.
    // GPU-RECORD: DecalUniform Shaders/Library/DecalCluster.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct DecalUniform
    {
        public Vector4 clusterParams;
        public Vector4 zSliceParams;
        public int decalCount;
        public int padding0;
        public int padding1;
        public int padding2;
    }

    /// <summary>
    /// Per-camera view layer across all decal paths (DBuffer, DirectGBuffer, Clustered): tallies raster decal counts and
    /// builds a drawOrder-preserving 3D cluster index for clustered decals with zero steady-state allocations.
    /// </summary>
    internal sealed class DecalContext : IDisposable
    {
        public const int ClusterTileSize = 64;
        public const int ClusterZSlices = 16;

        static readonly int s_RecordStride = Marshal.SizeOf<DecalRecord>();

        struct ClusteredCandidate
        {
            public DecalComponent component;
            public int drawOrder;
            public int4 tileRect;
            public float2 depthRange;
        }

        static readonly Comparison<ClusteredCandidate> s_ByDrawOrder = (a, b) => a.drawOrder.CompareTo(b.drawOrder);

        readonly SpatialClusterIndex m_ClusterIndex = new SpatialClusterIndex();
        readonly List<ClusteredCandidate> m_Candidates = new List<ClusteredCandidate>();
        readonly List<DecalRecord> m_Records = new List<DecalRecord>();
        readonly List<GraphicsBuffer> m_RetiredBuffers = new List<GraphicsBuffer>();
        DecalRecord[] m_Upload = new DecalRecord[1];
        GraphicsBuffer m_RecordBuffer;
        int m_RecordCapacity;
        int m_DBufferCount;
        int m_DirectCount;
        int m_DirectNormalCount;

        internal DecalContext()
        {
            m_RecordCapacity = 1;
            m_RecordBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_RecordCapacity, s_RecordStride);
        }

        internal int DBufferDecalCount => m_DBufferCount;
        internal int DirectDecalCount => m_DirectCount;
        internal int DirectNormalDecalCount => m_DirectNormalCount;
        internal int ClusteredDecalCount => m_Records.Count;
        internal int TotalDecalCount => m_DBufferCount + m_DirectCount + m_Records.Count;
        internal GraphicsBuffer RecordBuffer => m_RecordBuffer;
        internal GraphicsBuffer ClusterHeaderBuffer => m_ClusterIndex.HeaderBuffer;
        internal GraphicsBuffer ClusterIndexBuffer => m_ClusterIndex.IndexBuffer;

        internal void FlushRetiredBuffers()
        {
            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
            {
                m_RetiredBuffers[i].Release();
            }
            m_RetiredBuffers.Clear();
            m_ClusterIndex.FlushRetired();
        }

        internal void Build(SceneTable<DecalComponent> decalTable, Camera camera, int2 screenSize)
        {
            m_Candidates.Clear();
            m_Records.Clear();
            m_DBufferCount = 0;
            m_DirectCount = 0;
            m_DirectNormalCount = 0;

            int2 safeScreen = math.max(screenSize, new int2(1, 1));
            float near = camera != null ? camera.nearClipPlane : 0.1f;
            float far = camera != null ? camera.farClipPlane : 1000.0f;
            Matrix4x4 worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 gpuProjection = camera != null ? GraphicsConventions.GetComputeGpuProjection(camera) : Matrix4x4.identity;
            bool orthographic = camera != null && camera.orthographic;

            m_ClusterIndex.BeginBuild(safeScreen, near, far, ClusterTileSize, ClusterZSlices);

            foreach (DecalComponent decal in decalTable.components)
            {
                if (decal == null || !decal.isActiveAndEnabled)
                {
                    continue;
                }

                if (decal.executionMode != EDecalExecutionMode.Clustered)
                {
                    MeshRenderer renderer = decal.GetComponent<MeshRenderer>();
                    EDecalTarget target = DecalTargetUtility.Read(renderer.sharedMaterial);
                    if (target == EDecalTarget.DBuffer)
                    {
                        m_DBufferCount++;
                    }
                    else
                    {
                        m_DirectCount++;
                        if (renderer.sharedMaterial.HasProperty("_DecalReplaceNormal") && renderer.sharedMaterial.GetFloat("_DecalReplaceNormal") != 0.0f)
                        {
                            m_DirectNormalCount++;
                        }
                    }
                    continue;
                }

                Transform t = decal.transform;
                Vector3 halfExtents = new Vector3(
                    math.max(math.abs(t.lossyScale.x) * 0.5f, 1e-4f),
                    math.max(math.abs(t.lossyScale.y) * 0.5f, 1e-4f),
                    math.max(math.abs(t.lossyScale.z) * 0.5f, 1e-4f));
                SpatialBounds bounds = SpatialVolume.CreateBox(t.position, t.rotation, halfExtents).ComputeWorldBounds();
                if (!SpatialProjection.TryProjectBoundsToTileRect(in bounds, in worldToView, in gpuProjection,
                    orthographic, near, safeScreen, ClusterTileSize, out int4 tileRect, out float2 depthRange))
                {
                    continue;
                }

                m_Candidates.Add(new ClusteredCandidate
                {
                    component = decal,
                    drawOrder = decal.drawOrder,
                    tileRect = tileRect,
                    depthRange = depthRange
                });
            }

            if (m_Candidates.Count > 1)
            {
                m_Candidates.Sort(s_ByDrawOrder);
            }

            int count = math.min(m_Candidates.Count, SpatialProjection.MaxIndexedItems);
            for (int i = 0; i < count; ++i)
            {
                ClusteredCandidate candidate = m_Candidates[i];
                DecalComponent decal = candidate.component;
                Transform t = decal.transform;
                Matrix4x4 worldToLocal = t.worldToLocalMatrix;

                uint affectMask = (decal.clusteredAffectAlbedo ? 1u : 0u)
                    | (decal.clusteredAffectNormal ? 2u : 0u)
                    | (decal.clusteredAffectMaterial ? 4u : 0u);
                Vector3 up = t.up.normalized;
                Color linearColor = decal.clusteredColor.linear;

                m_Records.Add(new DecalRecord
                {
                    worldToBox0 = worldToLocal.GetRow(0),
                    worldToBox1 = worldToLocal.GetRow(1),
                    worldToBox2 = worldToLocal.GetRow(2),
                    albedoAlpha = new Vector4(linearColor.r, linearColor.g, linearColor.b, linearColor.a),
                    materialParams = new Vector4(decal.clusteredRoughness, decal.clusteredMetallic, decal.clusteredSpecular, affectMask),
                    normalBlend = new Vector4(up.x, up.y, up.z, 1.0f),
                    angleFadeParams = DecalComponent.AngleFadeParameters(decal.startAngleFade, decal.endAngleFade),
                    layerAndFlags = new Vector4(RenderingLayerUtility.Validate((uint)decal.renderingLayerMask), 0.0f, 0.0f, 0.0f)
                });
                m_ClusterIndex.AddItem(candidate.tileRect, candidate.depthRange);
            }

            m_ClusterIndex.FinalizeBuild();

            if (m_RecordCapacity < m_Records.Count)
            {
                int nextCapacity = math.max(m_Records.Count, m_RecordCapacity * 2);
                var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nextCapacity, s_RecordStride);
                if (m_RecordBuffer != null)
                {
                    m_RetiredBuffers.Add(m_RecordBuffer);
                }
                m_RecordBuffer = replacement;
                m_RecordCapacity = nextCapacity;
            }
        }

        internal DecalUniform ResolveUniform(bool enabled)
        {
            SpatialClusterGridDesc grid = m_ClusterIndex.Grid;
            return new DecalUniform
            {
                clusterParams = grid.clusterParams,
                zSliceParams = grid.zSliceParams,
                decalCount = enabled ? m_Records.Count : 0
            };
        }

        internal void Upload(in RGTransferEncoder commands)
        {
            commands.SetBufferData(m_ClusterIndex.HeaderBuffer, m_ClusterIndex.Headers);
            commands.SetBufferData(m_ClusterIndex.IndexBuffer, m_ClusterIndex.Indices);
            if (m_Records.Count > 0)
            {
                if (m_Upload.Length < m_Records.Count)
                {
                    m_Upload = new DecalRecord[m_Records.Count];
                }
                m_Records.CopyTo(m_Upload);
                commands.SetBufferData(m_RecordBuffer, m_Upload, 0, 0, m_Records.Count);
            }
        }

        public void Dispose()
        {
            FlushRetiredBuffers();
            m_RecordBuffer?.Release();
            m_RecordBuffer = null;
            m_ClusterIndex.Dispose();
        }
    }
}
