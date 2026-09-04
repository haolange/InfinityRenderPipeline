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

namespace InfinityTech.Rendering.LightPipeline
{
    internal static class LightShaderIDs
    {
        public static int DirectionalLightCount = Shader.PropertyToID("g_DirectionalLightCount");
        public static int LocalLightCount = Shader.PropertyToID("g_LocalLightCount");
        public static int SunRecordIndex = Shader.PropertyToID("g_SunRecordIndex");
        public static int LightRecordBuffer = Shader.PropertyToID("g_LightRecordBuffer");
        public static int LightBoundsBuffer = Shader.PropertyToID("g_LightBoundsBuffer");
        public static int SRV_LightBoundsBuffer = Shader.PropertyToID("SRV_LightBoundsBuffer");
        public static int LocalShadowMatrixBuffer = Shader.PropertyToID("SRV_LocalShadowMatrices");
        public static int LocalShadowRectBuffer = Shader.PropertyToID("SRV_LocalShadowRects");
        public static int LocalShadowSliceCount = Shader.PropertyToID("g_LocalShadowSliceCount");
        public static int HasTileLightList = Shader.PropertyToID("g_HasTileLightList");
    }

    internal class LightContext : IDisposable
    {
        int m_RecordCapacity;
        int m_BoundsCapacity;
        int m_LocalMatrixCapacity;
        int m_RecordStride;
        int m_BoundsStride;
        int m_DirectionalCount;
        int m_LocalCount;
        int m_SunRecordIndex;
        GraphicsBuffer m_LightRecordBuffer;
        GraphicsBuffer m_LightBoundsBuffer;
        GraphicsBuffer m_LocalShadowMatrixBuffer;
        GraphicsBuffer m_LocalShadowRectBuffer;
        GraphicsBuffer m_ZBinOverflowBuffer;
        GraphicsBuffer m_EmptyTileRangeBuffer;
        GraphicsBuffer m_EmptyTileListBuffer;
        GraphicsBuffer m_EmptyZBinRangeBuffer;
        GraphicsBuffer m_EmptyZBinListBuffer;
        NativeList<FLightRecord> m_Records;
        NativeList<FLightBounds> m_LocalBounds;
        readonly ShadowAllocator m_ShadowAllocator;

        internal LightContext()
        {
            m_RecordStride = Marshal.SizeOf<FLightRecord>();
            m_BoundsStride = Marshal.SizeOf<FLightBounds>();
            m_RecordCapacity = 1;
            m_BoundsCapacity = 1;
            m_LocalMatrixCapacity = 1;
            m_LightRecordBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_RecordCapacity, m_RecordStride);
            m_LightBoundsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_BoundsCapacity, m_BoundsStride);
            m_LocalShadowMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LocalMatrixCapacity, 64);
            m_LocalShadowRectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LocalMatrixCapacity, 16);
            m_ZBinOverflowBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            m_EmptyTileRangeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint) * 2);
            m_EmptyTileListBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            m_EmptyZBinRangeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint) * 2);
            m_EmptyZBinListBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            m_Records = new NativeList<FLightRecord>(8, Allocator.Persistent);
            m_LocalBounds = new NativeList<FLightBounds>(8, Allocator.Persistent);
            m_ShadowAllocator = new ShadowAllocator();
            m_SunRecordIndex = -1;
        }

        internal int DirectionalLightCount => m_DirectionalCount;
        internal int LocalLightCount => m_LocalCount;
        internal int SunRecordIndex => m_SunRecordIndex;
        internal int RecordCount => m_Records.Length;
        internal GraphicsBuffer LightRecordBuffer => m_LightRecordBuffer;
        internal GraphicsBuffer LightBoundsBuffer => m_LightBoundsBuffer;
        internal GraphicsBuffer LocalShadowMatrixBuffer => m_LocalShadowMatrixBuffer;
        internal GraphicsBuffer LocalShadowRectBuffer => m_LocalShadowRectBuffer;
        internal GraphicsBuffer ZBinOverflowBuffer => m_ZBinOverflowBuffer;
        internal GraphicsBuffer EmptyTileRangeBuffer => m_EmptyTileRangeBuffer;
        internal GraphicsBuffer EmptyTileListBuffer => m_EmptyTileListBuffer;
        internal GraphicsBuffer EmptyZBinRangeBuffer => m_EmptyZBinRangeBuffer;
        internal GraphicsBuffer EmptyZBinListBuffer => m_EmptyZBinListBuffer;
        internal ShadowAllocator ShadowAllocator => m_ShadowAllocator;
        internal uint LastZBinOverflow { get; set; }

        internal static bool HasZBinningLightList(int localLightCount)
        {
            return localLightCount > 0;
        }

        internal bool HasZBinningLightList()
        {
            return HasZBinningLightList(m_LocalCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Clear()
        {
            m_Records.Clear();
            m_LocalBounds.Clear();
            m_DirectionalCount = 0;
            m_LocalCount = 0;
            m_SunRecordIndex = -1;
            m_ShadowAllocator.Reset();
        }

        internal void Build(
            in CullingResults cullingResults,
            Dictionary<int, LightComponent> worldLightLookup,
            Camera camera,
            in FShadowAllocatorSettings settings)
        {
            Clear();

            NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
            Light sun = RenderSettings.sun;

            for (int pass = 0; pass < 2; ++pass)
            {
                bool wantDirectional = pass == 0;
                for (int i = 0; i < visibleLights.Length; ++i)
                {
                    VisibleLight visible = visibleLights[i];
                    Light light = visible.light;
                    if (light == null || !light.enabled)
                    {
                        continue;
                    }

                    ELightType type = FLightRecordPack.MapUnityType(visible.lightType);
                    bool isDirectional = type == ELightType.Directional;
                    if (isDirectional != wantDirectional)
                    {
                        continue;
                    }

                    LightComponent ext = null;
                    if (worldLightLookup != null)
                    {
                        worldLightLookup.TryGetValue(UnityEntityId.ToInt32(light), out ext);
                    }

                    if (ext == null)
                    {
                        light.TryGetComponent(out ext);
                    }

                    FLightRecord record = FLightRecordPack.FromUnityLight(light, ext, type, i);
                    if (sun != null && light == sun)
                    {
                        m_SunRecordIndex = m_Records.Length;
                    }

                    m_Records.Add(record);
                    if (isDirectional)
                    {
                        m_DirectionalCount++;
                    }
                    else
                    {
                        m_LocalCount++;
                    }
                }
            }

            if (m_SunRecordIndex < 0)
            {
                for (int i = 0; i < m_Records.Length; ++i)
                {
                    if (m_Records[i].lightType == (int)ELightType.Directional)
                    {
                        m_SunRecordIndex = i;
                        break;
                    }
                }
            }

            m_ShadowAllocator.Allocate(m_Records, cullingResults, camera, settings);

            Matrix4x4 worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            for (int i = 0; i < m_Records.Length; ++i)
            {
                FLightRecord record = m_Records[i];
                if (record.lightType == (int)ELightType.Directional)
                {
                    continue;
                }

                m_LocalBounds.Add(FLightRecordPack.LocalBounds(record, worldToView));
            }

            EnsureCapacity(m_Records.Length, m_LocalBounds.Length, math.max(1, m_ShadowAllocator.LocalSliceCount));
        }

        internal void EnsureCapacity(int recordCount, int boundsCount, int localMatrixCount)
        {
            int records = math.max(1, recordCount);
            int bounds = math.max(1, boundsCount);
            int matrices = math.max(1, localMatrixCount);
            if (m_LightRecordBuffer == null || m_RecordCapacity < records)
            {
                m_LightRecordBuffer?.Release();
                m_RecordCapacity = records;
                m_LightRecordBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_RecordCapacity, m_RecordStride);
            }

            if (m_LightBoundsBuffer == null || m_BoundsCapacity < bounds)
            {
                m_LightBoundsBuffer?.Release();
                m_BoundsCapacity = bounds;
                m_LightBoundsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_BoundsCapacity, m_BoundsStride);
            }

            if (m_LocalShadowMatrixBuffer == null || m_LocalMatrixCapacity < matrices)
            {
                m_LocalShadowMatrixBuffer?.Release();
                m_LocalShadowRectBuffer?.Release();
                m_LocalMatrixCapacity = matrices;
                m_LocalShadowMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LocalMatrixCapacity, 64);
                m_LocalShadowRectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LocalMatrixCapacity, 16);
            }
        }

        internal void RequireCapacity(int recordCount, int boundsCount, int localMatrixCount)
        {
            if (m_LightRecordBuffer == null || m_RecordCapacity < math.max(1, recordCount))
            {
                throw new InvalidOperationException("InfinityRP: Light record buffer capacity was not ensured before upload.");
            }

            if (m_LightBoundsBuffer == null || m_BoundsCapacity < math.max(1, boundsCount))
            {
                throw new InvalidOperationException("InfinityRP: Light bounds buffer capacity was not ensured before upload.");
            }

            if (m_LocalShadowMatrixBuffer == null || m_LocalMatrixCapacity < math.max(1, localMatrixCount))
            {
                throw new InvalidOperationException("InfinityRP: Local shadow matrix buffer capacity was not ensured before upload.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetLightData(CommandBuffer cmdBuffer)
        {
            RequireCapacity(m_Records.Length, m_LocalBounds.Length, math.max(1, m_ShadowAllocator.LocalSliceCount));

            if (m_Records.Length > 0)
            {
                cmdBuffer.SetBufferData(m_LightRecordBuffer, m_Records.AsArray());
            }

            if (m_LocalBounds.Length > 0)
            {
                cmdBuffer.SetBufferData(m_LightBoundsBuffer, m_LocalBounds.AsArray());
            }

            if (m_ShadowAllocator.LocalSliceCount > 0)
            {
                cmdBuffer.SetBufferData(m_LocalShadowMatrixBuffer, m_ShadowAllocator.LocalMatrices, 0, 0, m_ShadowAllocator.LocalSliceCount);
                cmdBuffer.SetBufferData(m_LocalShadowRectBuffer, m_ShadowAllocator.LocalUVRects, 0, 0, m_ShadowAllocator.LocalSliceCount);
            }

            cmdBuffer.SetGlobalInt(LightShaderIDs.DirectionalLightCount, m_DirectionalCount);
            cmdBuffer.SetGlobalInt(LightShaderIDs.LocalLightCount, m_LocalCount);
            cmdBuffer.SetGlobalInt(LightShaderIDs.HasTileLightList, 0);
            cmdBuffer.SetGlobalInt(LightShaderIDs.SunRecordIndex, m_SunRecordIndex);
            cmdBuffer.SetGlobalInt(LightShaderIDs.LocalShadowSliceCount, m_ShadowAllocator.LocalSliceCount);
            cmdBuffer.SetGlobalBuffer(LightShaderIDs.LightRecordBuffer, m_LightRecordBuffer);
            cmdBuffer.SetGlobalBuffer(LightShaderIDs.LightBoundsBuffer, m_LightBoundsBuffer);
            cmdBuffer.SetGlobalBuffer(LightShaderIDs.SRV_LightBoundsBuffer, m_LightBoundsBuffer);
            cmdBuffer.SetGlobalBuffer(LightShaderIDs.LocalShadowMatrixBuffer, m_LocalShadowMatrixBuffer);
            cmdBuffer.SetGlobalBuffer(LightShaderIDs.LocalShadowRectBuffer, m_LocalShadowRectBuffer);
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
            builder.Append(" sunIndex=").Append(m_SunRecordIndex).AppendLine();
            builder.Append("cascadeAllocated=").Append(m_ShadowAllocator.CascadeAllocatedCount);
            builder.Append(" cascadeVisible=").Append(m_ShadowAllocator.CascadeVisibleLightIndex);
            builder.Append(" localSlices=").Append(m_ShadowAllocator.LocalSliceCount).AppendLine();

            for (int i = 0; i < m_Records.Length; ++i)
            {
                FLightRecord record = m_Records[i];
                builder.Append("  record[").Append(i).Append(']');
                builder.Append(" type=").Append((ELightType)record.lightType);
                builder.Append(" visible=").Append(record.visibleLightIndex);
                builder.Append(" radiance=").Append(record.radiance);
                builder.Append(" posRange=").Append(record.positionRange);
                builder.Append(" flags=").Append(record.flags);
                builder.Append(" wantsShadow=").Append(record.unused0);
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
                direction = sun.directionSpot;
                radiance = sun.radiance;
            }
        }

        public void Dispose()
        {
            m_LightRecordBuffer?.Release();
            m_LightBoundsBuffer?.Release();
            m_LocalShadowMatrixBuffer?.Release();
            m_LocalShadowRectBuffer?.Release();
            m_ZBinOverflowBuffer?.Release();
            m_EmptyTileRangeBuffer?.Release();
            m_EmptyTileListBuffer?.Release();
            m_EmptyZBinRangeBuffer?.Release();
            m_EmptyZBinListBuffer?.Release();
            if (m_Records.IsCreated)
            {
                m_Records.Dispose();
            }

            if (m_LocalBounds.IsCreated)
            {
                m_LocalBounds.Dispose();
            }
        }
    }
}
