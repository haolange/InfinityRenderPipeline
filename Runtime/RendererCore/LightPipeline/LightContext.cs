using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.LightPipeline
{
    internal static class LightShaderIDs
    {
        public static int DirectionalLightCount = Shader.PropertyToID("g_DirectionalLightCount");
        public static int LocalLightCount = Shader.PropertyToID("g_LocalLightCount");
        public static int SunRecordIndex = Shader.PropertyToID("g_SunRecordIndex");
        public static int LightRecordBuffer = Shader.PropertyToID("g_LightRecordBuffer");
        public static int LocalShadowMatrixBuffer = Shader.PropertyToID("SRV_LocalShadowMatrices");
        public static int LocalShadowRectBuffer = Shader.PropertyToID("SRV_LocalShadowRects");
        public static int LocalShadowSliceCount = Shader.PropertyToID("g_LocalShadowSliceCount");
        // Clustered light list (LightLoop.hlsl).
        public static readonly int LightZBins = Shader.PropertyToID("SRV_LightZBins");
        public static readonly int LightTileMasks = Shader.PropertyToID("SRV_LightTileMasks");
        public static readonly int LightTileRects = Shader.PropertyToID("SRV_LightTileRects");
        public static readonly int LightTileParams = Shader.PropertyToID("g_LightTileParams");
        public static readonly int LightZBinParams = Shader.PropertyToID("g_LightZBinParams");
        // Graph keys of the CPU-uploaded light-list buffers.
        public static readonly int LightTileRectBuffer = Shader.PropertyToID("_LightTileRectBuffer");
        public static readonly int LightZBinBuffer = Shader.PropertyToID("_LightZBinBuffer");
        public static readonly int EmptyLightTileMaskBuffer = Shader.PropertyToID("_EmptyLightTileMaskBuffer");
    }

    internal class LightContext : IDisposable
    {
        struct LocalCandidate
        {
            public FLightRecord record;
            public float depth;
            public int4 tileRect;
        }

        int m_RecordCapacity;
        int m_RectCapacity;
        int m_LocalMatrixCapacity;
        int m_RecordStride;
        int m_DirectionalCount;
        int m_LocalCount;
        int m_UnsupportedShapeCount;
        int m_DistanceCulledCount;
        int m_OffscreenCount;
        int m_SunRecordIndex;
        int2 m_ScreenSize;
        Vector4 m_ZBinParams;
        GraphicsBuffer m_LightRecordBuffer;
        GraphicsBuffer m_TileRectBuffer;
        GraphicsBuffer m_ZBinBuffer;
        GraphicsBuffer m_EmptyTileMaskBuffer;
        GraphicsBuffer m_LocalShadowMatrixBuffer;
        GraphicsBuffer m_LocalShadowRectBuffer;
        NativeList<FLightRecord> m_Records;
        NativeList<int4> m_TileRects;
        NativeArray<uint> m_ZBins;
        readonly List<LocalCandidate> m_LocalCandidates = new List<LocalCandidate>();
        readonly ShadowAllocator m_ShadowAllocator;
        readonly List<GraphicsBuffer> m_RetiredBuffers = new List<GraphicsBuffer>();
        static readonly uint[] s_EmptyMask = new uint[1];
        static readonly int LocalShadowSliceStride = Marshal.SizeOf<FLocalShadowSliceGPU>();
        static readonly Comparison<LocalCandidate> s_ByDepth = (a, b) =>
        {
            int cmp = a.depth.CompareTo(b.depth);
            return cmp != 0 ? cmp : a.record.visibleLightIndex.CompareTo(b.record.visibleLightIndex);
        };
        internal int RetiredBufferCount => m_RetiredBuffers.Count;

        internal void FlushRetiredBuffers()
        {
            foreach (GraphicsBuffer buffer in m_RetiredBuffers) buffer.Release();
            m_RetiredBuffers.Clear();
        }

        void ReplaceBuffer(ref GraphicsBuffer buffer, int count, int stride)
        {
            var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
            if (buffer != null) m_RetiredBuffers.Add(buffer);
            buffer = replacement;
        }

        internal LightContext()
        {
            m_RecordStride = Marshal.SizeOf<FLightRecord>();
            m_RecordCapacity = 1;
            m_RectCapacity = 1;
            m_LocalMatrixCapacity = 1;
            m_LightRecordBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_RecordCapacity, m_RecordStride);
            m_TileRectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_RectCapacity, 16);
            m_ZBinBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, LightBinning.ZBinCount, sizeof(uint));
            m_EmptyTileMaskBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            m_LocalShadowMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LocalMatrixCapacity, 64);
            m_LocalShadowRectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LocalMatrixCapacity, LocalShadowSliceStride);
            m_Records = new NativeList<FLightRecord>(8, Allocator.Persistent);
            m_TileRects = new NativeList<int4>(8, Allocator.Persistent);
            m_ZBins = new NativeArray<uint>(LightBinning.ZBinCount, Allocator.Persistent);
            m_ShadowAllocator = new ShadowAllocator();
            m_SunRecordIndex = -1;
        }

        internal int DirectionalLightCount => m_DirectionalCount;
        internal int LocalLightCount => m_LocalCount;
        // Visible lights skipped because their shape is invalid (for example a zero-area rect) or their native type is
        // unknown; they are counted, never substituted.
        internal int UnsupportedShapeCount => m_UnsupportedShapeCount;
        // Local lights culled this camera by their Maximum Distance.
        internal int DistanceCulledCount => m_DistanceCulledCount;
        // Local lights Unity reported visible whose bounds project outside the screen.
        internal int OffscreenCount => m_OffscreenCount;
        internal int SunRecordIndex => m_SunRecordIndex;
        internal int RecordCount => m_Records.Length;
        internal int2 TileCount => LightBinning.TileCount(m_ScreenSize);
        internal int TileWordCount => LightBinning.WordCount(m_LocalCount);
        // x, y = screen size the tiles cover; z = tiles per row; w = mask words per tile.
        internal Vector4 TileParams => new Vector4(m_ScreenSize.x, m_ScreenSize.y, TileCount.x, TileWordCount);
        internal Vector4 ZBinParams => m_ZBinParams;
        internal GraphicsBuffer LightRecordBuffer => m_LightRecordBuffer;
        internal GraphicsBuffer TileRectBuffer => m_TileRectBuffer;
        internal GraphicsBuffer ZBinBuffer => m_ZBinBuffer;
        internal GraphicsBuffer EmptyTileMaskBuffer => m_EmptyTileMaskBuffer;
        internal GraphicsBuffer LocalShadowMatrixBuffer => m_LocalShadowMatrixBuffer;
        internal GraphicsBuffer LocalShadowRectBuffer => m_LocalShadowRectBuffer;
        internal ShadowAllocator ShadowAllocator => m_ShadowAllocator;
        internal NativeArray<uint> ZBins => m_ZBins;

        internal static bool HasLocalLightList(int localLightCount)
        {
            return localLightCount > 0;
        }

        internal bool HasLocalLightList()
        {
            return HasLocalLightList(m_LocalCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Clear()
        {
            m_Records.Clear();
            m_TileRects.Clear();
            m_LocalCandidates.Clear();
            m_DirectionalCount = 0;
            m_LocalCount = 0;
            m_UnsupportedShapeCount = 0;
            m_DistanceCulledCount = 0;
            m_OffscreenCount = 0;
            m_SunRecordIndex = -1;
            m_ShadowAllocator.Reset();
        }

        // Per camera: collect -> sort local lights by view depth -> z-bins and tile rectangles -> shadow allocation.
        // Light-list indices are record indices of this camera; nothing here is keyed across frames.
        internal void Build(
            in CullingResults cullingResults,
            Dictionary<ulong, InfinityAdditionalLightData> worldLightLookup,
            Camera camera,
            in FShadowAllocatorSettings settings,
            int2 screenSize)
        {
            Clear();
            m_ScreenSize = math.max(screenSize, new int2(1, 1));

            NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
            Light sun = RenderSettings.sun;
            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            Matrix4x4 worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 gpuProjection = camera != null ? GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) : Matrix4x4.identity;
            bool orthographic = camera != null && camera.orthographic;
            float near = camera != null ? camera.nearClipPlane : 0.1f;
            float far = camera != null ? camera.farClipPlane : 1000.0f;
            m_ZBinParams = LightBinning.ZBinParams(near, far, LightBinning.ZBinCount);

            for (int i = 0; i < visibleLights.Length; ++i)
            {
                VisibleLight visible = visibleLights[i];
                Light light = visible.light;
                if (light == null || !light.enabled)
                {
                    continue;
                }

                if (!LightShapeUtility.IsSupportedNativeType(visible.lightType))
                {
                    m_UnsupportedShapeCount++;
                    continue;
                }

                InfinityAdditionalLightData ext = null;
                if (worldLightLookup != null)
                {
                    worldLightLookup.TryGetValue(UnityEntityId.ToUInt64(light), out ext);
                }

                if (ext == null)
                {
                    light.TryGetComponent(out ext);
                }

                FLightShape shape = FLightRecordPack.ResolveShape(light, ext);
                if (!FLightRecordPack.TryPack(light, ext, shape, i, cameraPosition, out FLightRecord record))
                {
                    if (LightShapeUtility.IsValid(shape)) m_DistanceCulledCount++;
                    else m_UnsupportedShapeCount++;
                    continue;
                }

                if (record.IsDirectional)
                {
                    if (sun != null && light == sun)
                    {
                        m_SunRecordIndex = m_Records.Length;
                    }
                    m_Records.Add(record);
                    m_DirectionalCount++;
                    continue;
                }

                Vector3 centerVS = worldToView.MultiplyPoint3x4(new Vector3(record.positionRange.x, record.positionRange.y, record.positionRange.z));
                float radius = math.max(record.positionRange.w, 0.01f);
                if (!LightBinning.TryComputeTileRect(centerVS, radius, gpuProjection, orthographic, near, m_ScreenSize, out int4 rect))
                {
                    m_OffscreenCount++;
                    continue;
                }
                m_LocalCandidates.Add(new LocalCandidate { record = record, depth = -centerVS.z, tileRect = rect });
            }

            if (m_LocalCandidates.Count > LightBinning.MaxLocalLights)
            {
                throw new InvalidOperationException($"InfinityRP: {m_LocalCandidates.Count} visible local lights exceed the light-list limit of {LightBinning.MaxLocalLights}.");
            }

            m_LocalCandidates.Sort(s_ByDepth);
            var depthRanges = new NativeArray<float2>(math.max(1, m_LocalCandidates.Count), Allocator.Temp);
            for (int i = 0; i < m_LocalCandidates.Count; ++i)
            {
                LocalCandidate candidate = m_LocalCandidates[i];
                float radius = math.max(candidate.record.positionRange.w, 0.01f);
                depthRanges[i] = new float2(math.max(candidate.depth - radius, near), math.max(candidate.depth + radius, near));
                m_Records.Add(candidate.record);
                m_TileRects.Add(candidate.tileRect);
            }
            m_LocalCount = m_LocalCandidates.Count;
            LightBinning.BuildZBins(depthRanges, m_LocalCount, m_ZBinParams, m_ZBins);
            depthRanges.Dispose();

            if (m_SunRecordIndex < 0 && m_DirectionalCount > 0)
            {
                m_SunRecordIndex = 0;
            }

            m_ShadowAllocator.Allocate(m_Records, cullingResults, camera, settings);
            EnsureCapacity(m_Records.Length, m_TileRects.Length, math.max(1, m_ShadowAllocator.LocalSliceCount));
        }

        internal void EnsureCapacity(int recordCount, int rectCount, int localMatrixCount)
        {
            int records = math.max(1, recordCount);
            int rects = math.max(1, rectCount);
            int matrices = math.max(1, localMatrixCount);
            if (m_LightRecordBuffer == null || m_RecordCapacity < records)
            {
                ReplaceBuffer(ref m_LightRecordBuffer, records, m_RecordStride);
                m_RecordCapacity = records;
            }

            if (m_TileRectBuffer == null || m_RectCapacity < rects)
            {
                ReplaceBuffer(ref m_TileRectBuffer, rects, 16);
                m_RectCapacity = rects;
            }

            if (m_LocalShadowMatrixBuffer == null || m_LocalMatrixCapacity < matrices)
            {
                var matrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, matrices, 64);
                GraphicsBuffer rectBuffer;
                try { rectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, matrices, LocalShadowSliceStride); }
                catch { matrixBuffer.Release(); throw; }
                if (m_LocalShadowMatrixBuffer != null) m_RetiredBuffers.Add(m_LocalShadowMatrixBuffer);
                if (m_LocalShadowRectBuffer != null) m_RetiredBuffers.Add(m_LocalShadowRectBuffer);
                m_LocalShadowMatrixBuffer = matrixBuffer;
                m_LocalShadowRectBuffer = rectBuffer;
                m_LocalMatrixCapacity = matrices;
            }
        }

        internal void RequireCapacity(int recordCount, int rectCount, int localMatrixCount)
        {
            if (m_LightRecordBuffer == null || m_RecordCapacity < math.max(1, recordCount))
            {
                throw new InvalidOperationException("InfinityRP: Light record buffer capacity was not ensured before upload.");
            }

            if (m_TileRectBuffer == null || m_RectCapacity < math.max(1, rectCount))
            {
                throw new InvalidOperationException("InfinityRP: Light tile rectangle buffer capacity was not ensured before upload.");
            }

            if (m_LocalShadowMatrixBuffer == null || m_LocalMatrixCapacity < math.max(1, localMatrixCount))
            {
                throw new InvalidOperationException("InfinityRP: Local shadow matrix buffer capacity was not ensured before upload.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Upload<TCommands>(in TCommands cmdBuffer) where TCommands : struct, ITransferCommands
        {
            RequireCapacity(m_Records.Length, m_TileRects.Length, math.max(1, m_ShadowAllocator.LocalSliceCount));

            if (m_Records.Length > 0)
            {
                cmdBuffer.SetBufferData(m_LightRecordBuffer, m_Records.AsArray());
            }

            if (m_TileRects.Length > 0)
            {
                cmdBuffer.SetBufferData(m_TileRectBuffer, m_TileRects.AsArray());
            }

            cmdBuffer.SetBufferData(m_ZBinBuffer, m_ZBins);

            if (m_ShadowAllocator.LocalSliceCount > 0)
            {
                cmdBuffer.SetBufferData(m_LocalShadowMatrixBuffer, m_ShadowAllocator.LocalMatrices, 0, 0, m_ShadowAllocator.LocalSliceCount);
                cmdBuffer.SetBufferData(m_LocalShadowRectBuffer, m_ShadowAllocator.LocalGPU, 0, 0, m_ShadowAllocator.LocalSliceCount);
            }

            cmdBuffer.SetBufferData(m_EmptyTileMaskBuffer, s_EmptyMask, 0, 0, 1);
        }

        internal bool TryGetSunRecord(out FLightRecord record)
        {
            if (m_SunRecordIndex >= 0 && m_SunRecordIndex < m_Records.Length)
            {
                record = m_Records[m_SunRecordIndex];
                return true;
            }

            record = default;
            return false;
        }

        internal void WriteValidationDump(System.Text.StringBuilder builder)
        {
            builder.Append("LIGHT_STATE_DUMP").AppendLine();
            builder.Append("directional=").Append(m_DirectionalCount);
            builder.Append(" local=").Append(m_LocalCount);
            builder.Append(" records=").Append(m_Records.Length);
            builder.Append(" sunIndex=").Append(m_SunRecordIndex);
            builder.Append(" offscreen=").Append(m_OffscreenCount).AppendLine();
            builder.Append("cascadeAllocated=").Append(m_ShadowAllocator.CascadeAllocatedCount);
            builder.Append(" cascadeVisible=").Append(m_ShadowAllocator.CascadeVisibleLightIndex);
            builder.Append(" localSlices=").Append(m_ShadowAllocator.LocalSliceCount).AppendLine();

            for (int i = 0; i < m_Records.Length; ++i)
            {
                FLightRecord record = m_Records[i];
                builder.Append("  record[").Append(i).Append(']');
                builder.Append(" directional=").Append(record.IsDirectional);
                builder.Append(" visible=").Append(record.visibleLightIndex);
                builder.Append(" radiance=").Append(record.radiance);
                builder.Append(" posRange=").Append(record.positionRange);
                builder.Append(" flags=").Append(record.flags);
                builder.Append(" shadowFilter=").Append((EShadowFilter)record.shadowFilter);
                builder.Append(" shadowIndex=").Append(record.shadowMatrixIndex);
                builder.Append(" slices=").Append(record.shadowSliceCount);
                builder.AppendLine();
            }

            for (int i = 0; i < m_ShadowAllocator.LocalSliceCount; ++i)
            {
                FLocalShadowSlice slice = m_ShadowAllocator.LocalSlices[i];
                builder.Append("  slice[").Append(i).Append(']');
                builder.Append(" record=").Append(slice.recordIndex);
                builder.Append(" visible=").Append(slice.visibleLightIndex);
                builder.Append(" face=").Append(slice.face);
                builder.Append(" spot=").Append(slice.isSpot);
                builder.Append(" valid=").Append(slice.valid);
                builder.Append(" uv=").Append(slice.atlasUVRect);
                builder.AppendLine();
            }
        }

        internal static void ResolveSun(LightContext context, out Vector4 direction, out Vector4 radiance)
        {
            direction = new Vector4(0, 1, 0, 0);
            radiance = new Vector4(1, 1, 1, 1);
            if (context != null && context.TryGetSunRecord(out FLightRecord sun))
            {
                direction = -sun.forward;
                direction.w = 0;
                radiance = sun.radiance;
            }
        }

        public void Dispose()
        {
            FlushRetiredBuffers();
            m_LightRecordBuffer?.Release();
            m_TileRectBuffer?.Release();
            m_ZBinBuffer?.Release();
            m_EmptyTileMaskBuffer?.Release();
            m_LocalShadowMatrixBuffer?.Release();
            m_LocalShadowRectBuffer?.Release();
            if (m_Records.IsCreated) m_Records.Dispose();
            if (m_TileRects.IsCreated) m_TileRects.Dispose();
            if (m_ZBins.IsCreated) m_ZBins.Dispose();
        }
    }
}
