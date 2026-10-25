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
        // Reflection probes (Lighting/ReflectionProbes.hlsl).
        public static readonly int ReflectionProbes = Shader.PropertyToID("SRV_ReflectionProbes");
        public static readonly int ProbeZBins = Shader.PropertyToID("SRV_ProbeZBins");
        public static readonly int ProbeTileMasks = Shader.PropertyToID("SRV_ProbeTileMasks");
        public static readonly int ProbeTileParams = Shader.PropertyToID("g_ProbeTileParams");
        public static readonly int ReflectionProbeCount = Shader.PropertyToID("g_ReflectionProbeCount");
        public static readonly int ReflectionProbeAtlas = Shader.PropertyToID("SRV_ReflectionProbeAtlas");
        public static readonly int ReflectionProbeBuffer = Shader.PropertyToID("_ReflectionProbeBuffer");
        public static readonly int ProbeTileRectBuffer = Shader.PropertyToID("_ProbeTileRectBuffer");
        public static readonly int ProbeZBinBuffer = Shader.PropertyToID("_ProbeZBinBuffer");
        // Light cookies (Lighting/LightCookie.hlsl).
        public static readonly int LightCookies = Shader.PropertyToID("SRV_LightCookies");
        public static readonly int LightCookieAtlas = Shader.PropertyToID("SRV_LightCookieAtlas");
        public static readonly int LightCookieBuffer = Shader.PropertyToID("_LightCookieBuffer");
    }

    // One reflection probe of the camera's list (Unity order: importance, then smaller first).
    // GPU-RECORD: FReflectionProbeRecord Shaders/ShaderLibrary/Lighting/ReflectionProbes.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct FReflectionProbeRecord
    {
        // World to influence-box space (rows of a 3x4 affine; identity rotation, the box center at the origin).
        public Vector4 worldToBox0;
        public Vector4 worldToBox1;
        public Vector4 worldToBox2;
        // xyz = influence half extents including the blend band (sphere: radius in x); w = blend distance.
        public Vector4 extents;
        // xyz = capture position in box space; w = 1 with box projection.
        public Vector4 capture;
        // xyz = projection half extents (sphere: radius in x).
        public Vector4 projection;
        // x = intensity (HDR decode multiplier); y = first atlas slice; z = atlas mip count - 1; w = 1 sphere influence.
        public Vector4 parameters;
    }

    // Cookie and IES data of one light (record.cookieIndex). Rects are atlas uv (offset xy, scale zw); z = 0 means none.
    // GPU-RECORD: FLightCookie Shaders/ShaderLibrary/Lighting/LightCookie.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct FLightCookieGPU
    {
        public Vector4 cookieRect;
        public Vector4 iesRect;
        // x = 1 when the IES block is octahedral (cube profile), 0 when projected (2D profile); y = cookie block mip
        // count - 1; z = IES block mip count - 1.
        public Vector4 layout;
        // Directional cookies: x, y = 1 / cookieSize2D; z, w = light position along right / up, divided by the size.
        public Vector4 tiling;
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
        int m_ContactLightCount;
        int2 m_ScreenSize;
        Vector4 m_ZBinParams;
        GraphicsBuffer m_LightRecordBuffer;
        GraphicsBuffer m_TileRectBuffer;
        GraphicsBuffer m_ZBinBuffer;
        GraphicsBuffer m_EmptyTileMaskBuffer;
        GraphicsBuffer m_LocalShadowMatrixBuffer;
        GraphicsBuffer m_LocalShadowRectBuffer;
        GraphicsBuffer m_ProbeBuffer;
        GraphicsBuffer m_ProbeTileRectBuffer;
        GraphicsBuffer m_ProbeZBinBuffer;
        int m_ProbeCapacity;
        readonly List<FReflectionProbeRecord> m_Probes = new List<FReflectionProbeRecord>();
        readonly List<int> m_ProbeOrder = new List<int>();
        FReflectionProbeRecord[] m_ProbeUpload = new FReflectionProbeRecord[1];
        NativeList<int4> m_ProbeTileRects;
        NativeArray<uint> m_ProbeZBins;
        static readonly int ProbeStride = Marshal.SizeOf<FReflectionProbeRecord>();
        Matrix4x4 m_WorldToView;
        Matrix4x4 m_GpuProjection;
        bool m_Orthographic;
        float m_Near;
        GraphicsBuffer m_CookieBuffer;
        int m_CookieCapacity;
        readonly List<FLightCookieGPU> m_Cookies = new List<FLightCookieGPU>();
        FLightCookieGPU[] m_CookieUpload = new FLightCookieGPU[1];
        static readonly int CookieStride = Marshal.SizeOf<FLightCookieGPU>();
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
            m_CookieCapacity = 1;
            m_CookieBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_CookieCapacity, CookieStride);
            m_ProbeCapacity = 1;
            m_ProbeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_ProbeCapacity, ProbeStride);
            m_ProbeTileRectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_ProbeCapacity, 16);
            m_ProbeZBinBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, LightBinning.ZBinCount, sizeof(uint));
            m_ProbeTileRects = new NativeList<int4>(8, Allocator.Persistent);
            m_ProbeZBins = new NativeArray<uint>(LightBinning.ZBinCount, Allocator.Persistent);
            m_Records = new NativeList<FLightRecord>(8, Allocator.Persistent);
            m_TileRects = new NativeList<int4>(8, Allocator.Persistent);
            m_ZBins = new NativeArray<uint>(LightBinning.ZBinCount, Allocator.Persistent);
            m_ShadowAllocator = new ShadowAllocator();
            m_SunRecordIndex = -1;
        }

        internal int DirectionalLightCount => m_DirectionalCount;
        internal int LocalLightCount => m_LocalCount;
        // Local lights with a contact shadow bit this frame (record.misc.z >= 0).
        internal int ContactLightCount => m_ContactLightCount;
        // The sun record traces contact shadows.
        internal bool SunHasContactShadow => m_SunRecordIndex >= 0 && m_SunRecordIndex < m_Records.Length
            && (m_Records[m_SunRecordIndex].flags & FLightRecordFlags.EnableContactShadow) != 0;
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
        internal GraphicsBuffer CookieBuffer => m_CookieBuffer;
        internal GraphicsBuffer ReflectionProbeBuffer => m_ProbeBuffer;
        internal GraphicsBuffer ProbeTileRectBuffer => m_ProbeTileRectBuffer;
        internal GraphicsBuffer ProbeZBinBuffer => m_ProbeZBinBuffer;
        // Reflection probes of this camera (record order = blending priority).
        internal int ReflectionProbeCount => m_Probes.Count;
        // First probe-atlas slice of list entry index (six faces from there; record parameters.y).
        internal int ReflectionProbeFirstSlice(int index) => (int)m_Probes[index].parameters.y;
        internal int ProbeTileWordCount => LightBinning.WordCount(m_Probes.Count);
        // x, y = screen size; z = tiles per row; w = mask words per tile.
        internal Vector4 ProbeTileParams => new Vector4(m_ScreenSize.x, m_ScreenSize.y, TileCount.x, ProbeTileWordCount);
        // Records with a cookie or IES profile in the atlas this camera.
        internal int CookieCount => m_Cookies.Count;
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
            m_ContactLightCount = 0;
            m_Cookies.Clear();
            m_Probes.Clear();
            m_ProbeTileRects.Clear();
            for (int b = 0; b < m_ProbeZBins.Length; ++b) m_ProbeZBins[b] = LightBinning.EmptyBin;
            m_ShadowAllocator.Reset();
        }

        // Per camera: collect -> sort local lights by view depth -> z-bins and tile rectangles -> shadow allocation.
        // Light-list indices are record indices of this camera; nothing here is keyed across frames.
        internal void Build(
            in CullingResults cullingResults,
            Dictionary<ulong, InfinityAdditionalLightData> worldLightLookup,
            Camera camera,
            in FShadowAllocatorSettings settings,
            int2 screenSize, MeshPipeline.MeshWorld meshWorld, LocalShadowCache shadowCache = null)
        {
            Clear();
            m_ScreenSize = math.max(screenSize, new int2(1, 1));

            NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
            Light sun = RenderSettings.sun;
            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            Matrix4x4 worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
            Matrix4x4 gpuProjection = camera != null ? Pipeline.GraphicsUtility.GetComputeGpuProjection(camera) : Matrix4x4.identity;
            bool orthographic = camera != null && camera.orthographic;
            float near = camera != null ? camera.nearClipPlane : 0.1f;
            m_WorldToView = worldToView;
            m_GpuProjection = gpuProjection;
            m_Orthographic = orthographic;
            m_Near = near;
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

            m_ContactLightCount = ContactShadowAllocation.Assign(m_Records, m_DirectionalCount, m_TileRects, m_LocalCount);
            m_ShadowAllocator.Allocate(m_Records, cullingResults, camera, settings, meshWorld, shadowCache);
            EnsureCapacity(m_Records.Length, m_TileRects.Length, math.max(1, m_ShadowAllocator.LocalSliceCount));
        }

        // Builds this camera's reflection probe list from Unity's visible probes (already sorted by importance, then size),
        // assigns atlas slots (queuing filter passes) and bins the probes like local lights: logarithmic z-bins over the
        // list order and conservative tile rectangles of each influence volume's bounding sphere.
        NativeArray<VisibleReflectionProbe> m_ProbeSortInput;
        Comparison<int> m_ProbeComparison;
        int CompareProbes(int a, int b) => ReflectionProbePriority.Compare(m_ProbeSortInput[a].importance, ReflectionProbePriority.Volume(m_ProbeSortInput[a].bounds),
            m_ProbeSortInput[b].importance, ReflectionProbePriority.Volume(m_ProbeSortInput[b].bounds));

        internal void ResolveReflectionProbes(ReflectionProbeAtlas atlas, in CullingResults cullingResults, int frame)
        {
            m_Probes.Clear();
            m_ProbeTileRects.Clear();
            NativeArray<VisibleReflectionProbe> visible = cullingResults.visibleReflectionProbes;
            var depthRanges = new NativeArray<float2>(math.max(1, visible.Length), Allocator.Temp);
            // List order is the blending priority: higher importance first, then the smaller volume.
            m_ProbeOrder.Clear();
            for (int i = 0; i < visible.Length; ++i) m_ProbeOrder.Add(i);
            m_ProbeSortInput = visible;
            if (m_ProbeComparison == null) m_ProbeComparison = CompareProbes;
            m_ProbeOrder.Sort(m_ProbeComparison);
            m_ProbeSortInput = default;
            for (int order = 0; atlas != null && atlas.Enabled && order < m_ProbeOrder.Count && m_Probes.Count < LightBinning.MaxLocalLights; ++order)
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
                bool sphere = component.TryGetComponent(out InfinityAdditionalReflectionProbeData ext) && ext.influence == EReflectionProbeInfluence.Sphere;
                float radius = sphere ? (ext.sphereRadius > 0 ? ext.sphereRadius : math.cmax(new float3(extents.x, extents.y, extents.z))) : 0.0f;
                float boundingRadius = sphere ? radius + blend : (extents + Vector3.one * blend).magnitude;

                Vector3 centerVS = m_WorldToView.MultiplyPoint3x4(center);
                if (!LightBinning.TryComputeTileRect(centerVS, boundingRadius, m_GpuProjection, m_Orthographic, m_Near, m_ScreenSize, out int4 rect))
                {
                    continue;
                }

                bool realtime = component.mode == ReflectionProbeMode.Realtime;
                var key = new FProbeCubeKey(UnityEntityId.ToUInt64(component), UnityEntityId.ToUInt64(texture));
                if (!atlas.Acquire(key, texture, texture.updateCount, realtime, frame, out int slot))
                {
                    continue;
                }

                Vector3 capture = component.transform.position - center;
                var record = new FReflectionProbeRecord
                {
                    worldToBox0 = new Vector4(1, 0, 0, -center.x),
                    worldToBox1 = new Vector4(0, 1, 0, -center.y),
                    worldToBox2 = new Vector4(0, 0, 1, -center.z),
                    extents = sphere ? new Vector4(radius + blend, 0, 0, blend) : new Vector4(extents.x + blend, extents.y + blend, extents.z + blend, blend),
                    capture = new Vector4(capture.x, capture.y, capture.z, probe.isBoxProjection ? 1 : 0),
                    projection = sphere ? new Vector4(radius, 0, 0, 0) : new Vector4(extents.x, extents.y, extents.z, 0),
                    parameters = new Vector4(probe.hdrData.x, slot * 6, atlas.MipCount - 1, sphere ? 1 : 0),
                };
                float depth = -centerVS.z;
                depthRanges[m_Probes.Count] = new float2(math.max(depth - boundingRadius, m_Near), math.max(depth + boundingRadius, m_Near));
                m_Probes.Add(record);
                m_ProbeTileRects.Add(rect);
            }

            LightBinning.BuildZBins(depthRanges, m_Probes.Count, m_ZBinParams, m_ProbeZBins);
            depthRanges.Dispose();

            if (m_ProbeCapacity < m_Probes.Count)
            {
                int capacity = math.max(m_Probes.Count, m_ProbeCapacity * 2);
                ReplaceBuffer(ref m_ProbeBuffer, capacity, ProbeStride);
                ReplaceBuffer(ref m_ProbeTileRectBuffer, capacity, 16);
                m_ProbeCapacity = capacity;
            }
        }

        // Assigns cookie / IES atlas blocks to this camera's records (record.cookieIndex) and queues the uploads of new or
        // changed textures. The cookie texture is the Unity Light's; the IES profile is InfinityAdditionalLightData's.
        // Point lights need cubemap cookies (stored octahedral); every other shape takes a 2D cookie. Cube IES profiles
        // apply to every local shape by direction; 2D IES profiles only to cone and pyramid spots.
        internal void ResolveCookies(LightCookieAtlas atlas, in CullingResults cullingResults,
            Dictionary<ulong, InfinityAdditionalLightData> worldLightLookup, int frame)
        {
            m_Cookies.Clear();
            NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
            for (int i = 0; i < m_Records.Length; ++i)
            {
                FLightRecord record = m_Records[i];
                record.cookieIndex = -1;
                if (atlas != null && atlas.Enabled && record.visibleLightIndex >= 0 && record.visibleLightIndex < visibleLights.Length)
                {
                    Light light = visibleLights[record.visibleLightIndex].light;
                    InfinityAdditionalLightData ext = null;
                    if (light != null && (worldLightLookup == null || !worldLightLookup.TryGetValue(UnityEntityId.ToUInt64(light), out ext)))
                    {
                        light.TryGetComponent(out ext);
                    }
                    if (light != null && TryResolveCookie(atlas, record, light, ext, frame, out FLightCookieGPU cookie))
                    {
                        record.cookieIndex = m_Cookies.Count;
                        m_Cookies.Add(cookie);
                    }
                }
                m_Records[i] = record;
            }

            if (m_CookieBuffer == null || m_CookieCapacity < m_Cookies.Count)
            {
                int capacity = math.max(m_Cookies.Count, m_CookieCapacity * 2);
                ReplaceBuffer(ref m_CookieBuffer, capacity, CookieStride);
                m_CookieCapacity = capacity;
            }
        }

        static bool TryResolveCookie(LightCookieAtlas atlas, in FLightRecord record, Light light, InfinityAdditionalLightData ext, int frame, out FLightCookieGPU cookie)
        {
            cookie = default;
            bool directional = record.IsDirectional;
            bool point = !directional && (record.flags & (FLightRecordFlags.SpotCone | FLightRecordFlags.SpotPyramid | FLightRecordFlags.SpotBox | FLightRecordFlags.Planar)) == 0;
            bool projectedSpot = (record.flags & (FLightRecordFlags.SpotCone | FLightRecordFlags.SpotPyramid)) != 0;

            Texture texture = light.cookie;
            if (texture != null)
            {
                bool cube = texture.dimension == TextureDimension.Cube;
                if (cube == point && (cube || texture.dimension == TextureDimension.Tex2D)
                    && Acquire(atlas, texture, cube ? ECookieLayout.Octahedral : ECookieLayout.Projected, frame, out cookie.cookieRect, out int mips))
                {
                    cookie.layout.y = mips;
                    if (directional)
                    {
                        Vector2 size = light.cookieSize2D;
                        float invX = 1.0f / Mathf.Max(size.x, 1e-4f), invY = 1.0f / Mathf.Max(size.y, 1e-4f);
                        Vector3 position = light.transform.position;
                        Vector3 right = new Vector3(record.right.x, record.right.y, record.right.z);
                        Vector3 up = Vector3.Cross(new Vector3(record.forward.x, record.forward.y, record.forward.z), right);
                        cookie.tiling = new Vector4(invX, invY, Vector3.Dot(position, right) * invX, Vector3.Dot(position, up) * invY);
                    }
                }
            }

            Texture ies = ext != null && !directional ? ext.iesProfile : null;
            if (ies != null)
            {
                bool cube = ies.dimension == TextureDimension.Cube;
                if ((cube || (projectedSpot && ies.dimension == TextureDimension.Tex2D))
                    && Acquire(atlas, ies, cube ? ECookieLayout.Octahedral : ECookieLayout.Projected, frame, out cookie.iesRect, out int mips))
                {
                    cookie.layout.x = cube ? 1 : 0;
                    cookie.layout.z = mips;
                }
            }
            return cookie.cookieRect.z > 0 || cookie.iesRect.z > 0;
        }

        static bool Acquire(LightCookieAtlas atlas, Texture texture, ECookieLayout layout, int frame, out Vector4 rect, out int maxMip)
        {
            UnityEngine.Experimental.Rendering.GraphicsFormat format = texture.graphicsFormat;
            ECookieChannels channels = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsAlphaOnlyFormat(format) ? ECookieChannels.Alpha
                : UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetComponentCount(format) == 1 ? ECookieChannels.Red
                : ECookieChannels.RGB;
            bool ok = atlas.Acquire(texture, layout, channels, UnityEntityId.ToUInt64(texture), texture.updateCount, texture.width, texture.height, frame, out rect);
            maxMip = ok ? (int)math.round(math.log2(math.max(1.0f, rect.z * atlas.AtlasSize))) : 0;
            return ok;
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

            cmdBuffer.SetBufferData(m_ProbeZBinBuffer, m_ProbeZBins);
            if (m_Probes.Count > 0)
            {
                if (m_ProbeUpload.Length < m_Probes.Count) m_ProbeUpload = new FReflectionProbeRecord[m_Probes.Count];
                m_Probes.CopyTo(m_ProbeUpload);
                cmdBuffer.SetBufferData(m_ProbeBuffer, m_ProbeUpload, 0, 0, m_Probes.Count);
                cmdBuffer.SetBufferData(m_ProbeTileRectBuffer, m_ProbeTileRects.AsArray());
            }

            if (m_Cookies.Count > 0)
            {
                if (m_CookieUpload.Length < m_Cookies.Count) m_CookieUpload = new FLightCookieGPU[m_Cookies.Count];
                m_Cookies.CopyTo(m_CookieUpload);
                cmdBuffer.SetBufferData(m_CookieBuffer, m_CookieUpload, 0, 0, m_Cookies.Count);
            }
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
            m_CookieBuffer?.Release();
            m_ProbeBuffer?.Release();
            m_ProbeTileRectBuffer?.Release();
            m_ProbeZBinBuffer?.Release();
            if (m_ProbeTileRects.IsCreated) m_ProbeTileRects.Dispose();
            if (m_ProbeZBins.IsCreated) m_ProbeZBins.Dispose();
            if (m_Records.IsCreated) m_Records.Dispose();
            if (m_TileRects.IsCreated) m_TileRects.Dispose();
            if (m_ZBins.IsCreated) m_ZBins.Dispose();
        }
    }
}
