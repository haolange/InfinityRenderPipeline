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

namespace InfinityTech.Rendering.AnalyticShadow
{
    internal static class AnalyticShadowShaderIDs
    {
        public static readonly int AnalyticOccluderBuffer = Shader.PropertyToID("SRV_AnalyticOccluderBuffer");
        public static readonly int AnalyticIndirectClusterHeader = Shader.PropertyToID("SRV_AnalyticIndirectClusterHeader");
        public static readonly int AnalyticIndirectClusterIndices = Shader.PropertyToID("SRV_AnalyticIndirectClusterIndices");
        public static readonly int AnalyticDirectClusterHeader = Shader.PropertyToID("SRV_AnalyticDirectClusterHeader");
        public static readonly int AnalyticDirectClusterIndices = Shader.PropertyToID("SRV_AnalyticDirectClusterIndices");

        public static readonly RGResourceKey AnalyticOccluderRG = new RGResourceKey("_AnalyticOccluderBuffer");
        public static readonly RGResourceKey AnalyticIndirectHeaderRG = new RGResourceKey("_AnalyticIndirectClusterHeader");
        public static readonly RGResourceKey AnalyticIndirectIndexRG = new RGResourceKey("_AnalyticIndirectClusterIndices");
        public static readonly RGResourceKey AnalyticDirectHeaderRG = new RGResourceKey("_AnalyticDirectClusterHeader");
        public static readonly RGResourceKey AnalyticDirectIndexRG = new RGResourceKey("_AnalyticDirectClusterIndices");
    }

    internal struct AnalyticShadowLight
    {
        public bool isDirectional;
        public float3 positionWS;
        public float3 toLightDirWS;
        public float range;
        public float angularRadius;
    }

    internal struct AnalyticShadowStats
    {
        public int totalOccluders;
        public int visibleOccluders;
        public int indirectClusterEntries;
        public int directClusterEntries;
    }

    // One analytic occluder packed into world-to-local orthonormal basis + primitive params (112 bytes).
    // GPU-RECORD: AnalyticOccluderRecord Shaders/Library/AnalyticShadow.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct AnalyticOccluderRecord
    {
        public Vector4 worldToLocal0;
        public Vector4 worldToLocal1;
        public Vector4 worldToLocal2;
        public Vector4 shapeParams;
        public Vector4 centerAndRadius;
        public Vector4 attenuationParams;
        public Vector4 flagsAndMeta;
    }

    // Per-camera cluster grid parameters shared by Deferred and Forward analytic shadow evaluation (48 bytes).
    // GPU-RECORD: AnalyticShadowUniform Shaders/Library/AnalyticShadow.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct AnalyticShadowUniform
    {
        public Vector4 clusterParams;
        public Vector4 zSliceParams;
        public int occluderCount;
        public int indirectEnabled;
        public int directEnabled;
        public int padding0;
    }

    /// <summary>
    /// Per-camera analytic shadow and indirect occlusion view layer. Builds two separate 3D spatial cluster indices
    /// for Indirect Occlusion Influence Bounds and Light-Extruded Direct Shadow Influence Bounds (PRD §8.4).
    /// </summary>
    internal sealed class AnalyticShadowContext : IDisposable
    {
        public const int ClusterTileSize = 64;
        public const int ClusterZSlices = 16;

        static readonly int s_RecordStride = Marshal.SizeOf<AnalyticOccluderRecord>();

        readonly SpatialClusterIndex m_IndirectClusterIndex = new SpatialClusterIndex();
        readonly SpatialClusterIndex m_DirectClusterIndex = new SpatialClusterIndex();
        readonly List<AnalyticOccluderRecord> m_Records = new List<AnalyticOccluderRecord>(64);
        readonly List<GraphicsBuffer> m_RetiredBuffers = new List<GraphicsBuffer>();
        AnalyticOccluderRecord[] m_Upload = new AnalyticOccluderRecord[1];
        GraphicsBuffer m_RecordBuffer;
        int m_RecordCapacity;
        bool m_IndirectEnabled;
        bool m_DirectEnabled;
        AnalyticShadowStats m_Stats;

        internal AnalyticShadowContext()
        {
            m_RecordCapacity = 1;
            m_RecordBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_RecordCapacity, s_RecordStride);
        }

        internal int OccluderCount => m_Records.Count;
        internal bool IndirectEnabled => m_IndirectEnabled;
        internal bool DirectEnabled => m_DirectEnabled;
        internal AnalyticShadowStats Stats => m_Stats;
        internal GraphicsBuffer RecordBuffer => m_RecordBuffer;
        internal GraphicsBuffer IndirectHeaderBuffer => m_IndirectClusterIndex.HeaderBuffer;
        internal GraphicsBuffer IndirectIndexBuffer => m_IndirectClusterIndex.IndexBuffer;
        internal GraphicsBuffer DirectHeaderBuffer => m_DirectClusterIndex.HeaderBuffer;
        internal GraphicsBuffer DirectIndexBuffer => m_DirectClusterIndex.IndexBuffer;
        internal Unity.Collections.NativeArray<uint2> IndirectHeaders => m_IndirectClusterIndex.Headers;
        internal Unity.Collections.NativeArray<uint> IndirectIndices => m_IndirectClusterIndex.Indices;
        internal Unity.Collections.NativeArray<uint2> DirectHeaders => m_DirectClusterIndex.Headers;
        internal Unity.Collections.NativeArray<uint> DirectIndices => m_DirectClusterIndex.Indices;

        internal void FlushRetiredBuffers()
        {
            for (int i = 0; i < m_RetiredBuffers.Count; ++i)
            {
                m_RetiredBuffers[i].Release();
            }
            m_RetiredBuffers.Clear();
            m_IndirectClusterIndex.FlushRetired();
            m_DirectClusterIndex.FlushRetired();
        }

        internal void Build(SceneTable<InfinityAnalyticOccluder> occluderTable, Camera camera, int2 screenSize,
            List<AnalyticShadowLight> activeLights, bool allowIndirect, bool allowDirect)
        {
            m_Records.Clear();
            m_IndirectEnabled = allowIndirect;
            m_DirectEnabled = allowDirect && activeLights != null && activeLights.Count > 0;
            m_Stats = default;

            int2 safeScreen = math.max(screenSize, new int2(1, 1));
            float near = camera != null ? camera.nearClipPlane : 0.1f;
            float far = camera != null ? camera.farClipPlane : 1000.0f;
            Matrix4x4 worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 gpuProjection = camera != null ? GraphicsConventions.GetComputeGpuProjection(camera) : Matrix4x4.identity;
            bool orthographic = camera != null && camera.orthographic;

            m_IndirectClusterIndex.BeginBuild(safeScreen, near, far, ClusterTileSize, ClusterZSlices);
            m_DirectClusterIndex.BeginBuild(safeScreen, near, far, ClusterTileSize, ClusterZSlices);

            if (occluderTable != null && (allowIndirect || allowDirect))
            {
                foreach (InfinityAnalyticOccluder occluder in occluderTable.components)
                {
                    if (occluder == null || !occluder.isActiveAndEnabled)
                    {
                        continue;
                    }
                    m_Stats.totalOccluders++;
                    if (m_Records.Count >= SpatialProjection.MaxIndexedItems)
                    {
                        break;
                    }

                    bool wantsIndirect = allowIndirect && occluder.castIndirectOcclusion && occluder.indirectStrength > 0.001f;
                    bool wantsDirect = m_DirectEnabled && occluder.castDirectShadow && occluder.directStrength > 0.001f;
                    if (!wantsIndirect && !wantsDirect)
                    {
                        continue;
                    }

                    SpatialVolume vol = occluder.ComputeSpatialVolume();
                    SpatialBounds geomBounds = vol.ComputeWorldBounds();

                    bool visibleIndirect = false;
                    int4 indirectRect = default;
                    float2 indirectDepth = default;
                    if (wantsIndirect)
                    {
                        SpatialBounds indirectBounds = InfinityAnalyticOccluder.ExpandForIndirectOcclusion(in geomBounds, occluder.indirectFalloffDistance);
                        visibleIndirect = SpatialProjection.TryProjectBoundsToTileRect(in indirectBounds, in worldToView, in gpuProjection,
                            orthographic, near, safeScreen, ClusterTileSize, out indirectRect, out indirectDepth);
                    }

                    bool visibleDirect = false;
                    int4 directRect = default;
                    float2 directDepth = default;
                    if (wantsDirect && TryComputeDirectShadowUnionBounds(in geomBounds, occluder.directSoftness, occluder.directMaxDistance,
                        activeLights, out SpatialBounds directUnionBounds))
                    {
                        visibleDirect = SpatialProjection.TryProjectBoundsToTileRect(in directUnionBounds, in worldToView, in gpuProjection,
                            orthographic, near, safeScreen, ClusterTileSize, out directRect, out directDepth);
                    }

                    if (!visibleIndirect && !visibleDirect)
                    {
                        continue;
                    }

                    m_Records.Add(PackRecord(in vol, occluder, visibleIndirect, visibleDirect));
                    m_IndirectClusterIndex.AddItem(visibleIndirect ? indirectRect : new int4(1, 1, 0, 0), indirectDepth);
                    m_DirectClusterIndex.AddItem(visibleDirect ? directRect : new int4(1, 1, 0, 0), directDepth);
                }
            }

            m_IndirectClusterIndex.FinalizeBuild();
            m_DirectClusterIndex.FinalizeBuild();

            m_Stats.visibleOccluders = m_Records.Count;
            m_Stats.indirectClusterEntries = m_IndirectClusterIndex.IndexCount;
            m_Stats.directClusterEntries = m_DirectClusterIndex.IndexCount;

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

        internal static bool TryComputeDirectShadowUnionBounds(in SpatialBounds geomBounds, float softness, float maxDistance,
            List<AnalyticShadowLight> activeLights, out SpatialBounds unionBounds)
        {
            bool any = false;
            float3 minWS = new float3(float.PositiveInfinity);
            float3 maxWS = new float3(float.NegativeInfinity);

            for (int i = 0; i < activeLights.Count; ++i)
            {
                AnalyticShadowLight light = activeLights[i];
                SpatialBounds lightBounds;
                if (light.isDirectional)
                {
                    lightBounds = InfinityAnalyticOccluder.ComputeDirectShadowBounds(in geomBounds, light.toLightDirWS, softness, maxDistance);
                }
                else
                {
                    lightBounds = InfinityAnalyticOccluder.ComputeDirectShadowBoundsForPointLight(
                        in geomBounds, light.positionWS, light.range, softness, maxDistance, out bool reaches);
                    if (!reaches)
                    {
                        continue;
                    }
                }
                minWS = math.min(minWS, lightBounds.center - lightBounds.halfExtents);
                maxWS = math.max(maxWS, lightBounds.center + lightBounds.halfExtents);
                any = true;
            }

            unionBounds = any ? SpatialBounds.FromMinMax(minWS, maxWS) : geomBounds;
            return any;
        }

        internal static AnalyticOccluderRecord PackRecord(in SpatialVolume vol, InfinityAnalyticOccluder occluder, bool indirectActive, bool directActive)
        {
            var basis = new float3x3(vol.orientation);
            float3 r0 = basis.c0;
            float3 r1 = basis.c1;
            float3 r2 = basis.c2;
            uint flags = (indirectActive ? 1u : 0u) | (directActive ? 2u : 0u);
            return new AnalyticOccluderRecord
            {
                worldToLocal0 = new Vector4(r0.x, r0.y, r0.z, -math.dot(r0, vol.center)),
                worldToLocal1 = new Vector4(r1.x, r1.y, r1.z, -math.dot(r1, vol.center)),
                worldToLocal2 = new Vector4(r2.x, r2.y, r2.z, -math.dot(r2, vol.center)),
                shapeParams = new Vector4(vol.halfExtents.x, vol.halfExtents.y, vol.halfExtents.z, vol.radius),
                centerAndRadius = new Vector4(vol.center.x, vol.center.y, vol.center.z, vol.BoundingRadius()),
                attenuationParams = new Vector4(occluder.indirectStrength, occluder.indirectFalloffDistance, occluder.directStrength, occluder.directSoftness),
                flagsAndMeta = new Vector4((uint)vol.kind, (byte)occluder.renderingLayerMask, flags, occluder.directMaxDistance)
            };
        }

        internal AnalyticShadowUniform ResolveUniform(bool enabled)
        {
            SpatialClusterGridDesc grid = m_IndirectClusterIndex.Grid;
            return new AnalyticShadowUniform
            {
                clusterParams = grid.clusterParams,
                zSliceParams = grid.zSliceParams,
                occluderCount = enabled ? m_Records.Count : 0,
                indirectEnabled = enabled && m_IndirectEnabled ? 1 : 0,
                directEnabled = enabled && m_DirectEnabled ? 1 : 0
            };
        }

        internal AnalyticOccluderRecord[] PrepareRecordUploadArray()
        {
            if (m_Upload.Length < m_Records.Count)
            {
                m_Upload = new AnalyticOccluderRecord[m_Records.Count];
            }
            if (m_Records.Count > 0)
            {
                m_Records.CopyTo(m_Upload);
            }
            return m_Upload;
        }

        internal void Upload(in RGTransferEncoder commands)
        {
            commands.SetBufferData(m_IndirectClusterIndex.HeaderBuffer, m_IndirectClusterIndex.Headers);
            commands.SetBufferData(m_IndirectClusterIndex.IndexBuffer, m_IndirectClusterIndex.Indices);
            commands.SetBufferData(m_DirectClusterIndex.HeaderBuffer, m_DirectClusterIndex.Headers);
            commands.SetBufferData(m_DirectClusterIndex.IndexBuffer, m_DirectClusterIndex.Indices);
            if (m_Records.Count > 0)
            {
                commands.SetBufferData(m_RecordBuffer, PrepareRecordUploadArray(), 0, 0, m_Records.Count);
            }
        }

        public void Dispose()
        {
            FlushRetiredBuffers();
            m_RecordBuffer?.Release();
            m_RecordBuffer = null;
            m_IndirectClusterIndex.Dispose();
            m_DirectClusterIndex.Dispose();
        }
    }
}
