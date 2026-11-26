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
        public static readonly int LightRecordBuffer = Shader.PropertyToID("g_LightRecordBuffer");
        public static readonly int LocalShadowMatrixBuffer = Shader.PropertyToID("SRV_LocalShadowMatrices");
        public static readonly int LocalShadowRectBuffer = Shader.PropertyToID("SRV_LocalShadowRects");
        // Clustered light list (LightLoop.hlsl).
        public static readonly int LightZBins = Shader.PropertyToID("SRV_LightZBins");
        public static readonly int LightTileMasks = Shader.PropertyToID("SRV_LightTileMasks");
        public static readonly int LightTileRects = Shader.PropertyToID("SRV_LightTileRects");
        // Graph keys of the CPU-uploaded light-list buffers.
        public static readonly int LightTileRectBuffer = Shader.PropertyToID("_LightTileRectBuffer");
        public static readonly int LightZBinBuffer = Shader.PropertyToID("_LightZBinBuffer");
        public static readonly int EmptyLightTileMaskBuffer = Shader.PropertyToID("_EmptyLightTileMaskBuffer");
        // Light cookies (Lighting/LightCookie.hlsl).
        public static readonly int LightCookies = Shader.PropertyToID("SRV_LightCookies");
        public static readonly int LightCookieAtlas = Shader.PropertyToID("SRV_LightCookieAtlas");
        public static readonly GlobalKeyword LightCookiesKeyword = GlobalKeyword.Create("INFINITY_LIGHT_COOKIES");
        public static readonly int LightCookieBuffer = Shader.PropertyToID("_LightCookieBuffer");
    }

    // Cookie and IES data of one light (record.cookieIndex). Rects are atlas uv (offset xy, scale zw); z = 0 means none.
    // GPU-RECORD: LightCookie Shaders/Library/Lighting/LightCookie.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct LightCookieGPU
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
            public LightRecord record;
            public float depth;
            public int4 tileRect;
        }

        int m_RecordCapacity;
        int m_RectCapacity;
        int m_LocalMatrixCapacity;
        int m_RecordStride;
        // Mixed lighting

        // The sideband stores the baked visibility of two mixed directional lights (G and B).
        internal const int MixedLightSlotCount = 2;
        // x, y: Unity shadowmask channel of slot 0 and 1, -1 when the slot is empty.
        Vector4 m_MixedLightChannels;
        int m_MixedLightCount;
        int m_DroppedMixedLightCount;

        internal bool HasMixedLights => m_MixedLightCount > 0;
        internal Vector4 MixedLightChannels => m_MixedLightChannels;
        // Mixed directional lights beyond the two slots this camera; they keep their realtime shadow only.
        internal int DroppedMixedLightCount => m_DroppedMixedLightCount;

        // The shadowmask channel of a baked Mixed directional light in Shadowmask mode, else -1. Local lights never read
        // a shadowmask.
        internal static int MixedLightChannel(Light light)
        {
            if (light == null || light.type != LightType.Directional)
            {
                return -1;
            }
            LightBakingOutput baking = light.bakingOutput;
            bool mixed = baking.lightmapBakeType == LightmapBakeType.Mixed && baking.mixedLightingMode == MixedLightingMode.Shadowmask;
            return mixed && baking.occlusionMaskChannel >= 0 && baking.occlusionMaskChannel < 4 ? baking.occlusionMaskChannel : -1;
        }

        void AssignMixedLightSlot(ref LightRecord record, int channel)
        {
            if (m_MixedLightCount >= MixedLightSlotCount)
            {
                m_DroppedMixedLightCount++;
                return;
            }
            record.bakedVisibilitySlot = m_MixedLightCount;
            record.flags |= LightRecordFlags.MixedShadowmask;
            m_MixedLightChannels[m_MixedLightCount] = channel;
            m_MixedLightCount++;
        }

        int m_DirectionalCount;
        int m_LocalCount;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int m_UnsupportedShapeCount;
        int m_DistanceCulledCount;
#endif
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
        GraphicsBuffer m_CookieBuffer;
        int m_CookieCapacity;
        readonly List<LightCookieGPU> m_Cookies = new List<LightCookieGPU>();
        // InfinityAdditionalLightData of each visible light this frame, read once from the registry by visible index.
        InfinityAdditionalLightData[] m_AdditionalData = System.Array.Empty<InfinityAdditionalLightData>();

        // The registered additional data of a visible light, or null for a plain Unity light.
        internal InfinityAdditionalLightData AdditionalData(int visibleLightIndex) => m_AdditionalData[visibleLightIndex];
        LightCookieGPU[] m_CookieUpload = new LightCookieGPU[1];
        static readonly int CookieStride = Marshal.SizeOf<LightCookieGPU>();
        NativeList<LightRecord> m_Records;
        NativeList<int4> m_TileRects;
        NativeArray<uint> m_ZBins;
        readonly List<LocalCandidate> m_LocalCandidates = new List<LocalCandidate>();
        readonly ShadowAllocator m_ShadowAllocator;
        readonly List<GraphicsBuffer> m_RetiredBuffers = new List<GraphicsBuffer>();
        static readonly uint[] s_EmptyMask = new uint[1];
        static readonly int LocalShadowSliceStride = Marshal.SizeOf<LocalShadowSliceGPU>();
        static readonly Comparison<LocalCandidate> s_ByDepth = (a, b) =>
        {
            int cmp = a.depth.CompareTo(b.depth);
            return cmp != 0 ? cmp : a.record.visibleLightIndex.CompareTo(b.record.visibleLightIndex);
        };
        internal int RetiredBufferCount => m_RetiredBuffers.Count;

        internal void FlushRetiredBuffers()
        {
            foreach (GraphicsBuffer buffer in m_RetiredBuffers)
            {
                buffer.Release();
            }
            m_RetiredBuffers.Clear();
        }

        void ReplaceBuffer(ref GraphicsBuffer buffer, int count, int stride)
        {
            var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
            if (buffer != null)
            {
                m_RetiredBuffers.Add(buffer);
            }
            buffer = replacement;
        }

        internal LightContext()
        {
            m_RecordStride = Marshal.SizeOf<LightRecord>();
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
            m_Records = new NativeList<LightRecord>(8, Allocator.Persistent);
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
            && (m_Records[m_SunRecordIndex].flags & LightRecordFlags.EnableContactShadow) != 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Visible lights skipped because their shape is invalid (for example a zero-area rect) or their native type is
        // unknown; the Rendering Debugger counts them, nothing substitutes them.
        internal int UnsupportedShapeCount => m_UnsupportedShapeCount;
        // Local lights culled this camera by their Maximum Distance.
        internal int DistanceCulledCount => m_DistanceCulledCount;
#endif
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
        // Records with a cookie or IES profile in the atlas this camera.
        internal int CookieCount => m_Cookies.Count;
        internal ShadowAllocator ShadowAllocator => m_ShadowAllocator;
        internal NativeArray<uint> ZBins => m_ZBins;
        internal NativeArray<LightRecord> Records => m_Records.AsArray();

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_UnsupportedShapeCount = 0;
            m_DistanceCulledCount = 0;
#endif
            m_OffscreenCount = 0;
            m_SunRecordIndex = -1;
            m_ContactLightCount = 0;
            m_MixedLightChannels = new Vector4(-1, -1, 0, 0);
            m_MixedLightCount = 0;
            m_DroppedMixedLightCount = 0;
            m_Cookies.Clear();
            m_ShadowAllocator.Reset();
        }

        // Per camera: collect -> sort local lights by view depth -> z-bins and tile rectangles -> shadow allocation.
        // Light-list indices are record indices of this camera; nothing here is keyed across frames.
        internal void Build(
            in CullingResults cullingResults,
            SceneTable<InfinityAdditionalLightData> lights,
            Camera camera,
            in ShadowAllocatorSettings settings,
            int2 screenSize, MeshPipeline.MeshWorld meshWorld, MeshPipeline.MeshPassId shadowPass, int frame, LocalShadowCache shadowCache = null)
        {
            NativeArray<VisibleLight> visibleLights;
            Light sun;
            Vector3 cameraPosition;
            Matrix4x4 worldToView;
            Matrix4x4 gpuProjection;
            bool orthographic;
            float near;
            float far;
            using (LightCpuMarkers.s_Setup.Auto())
            {
                Clear();
                m_ScreenSize = math.max(screenSize, new int2(1, 1));

                visibleLights = cullingResults.visibleLights;
                sun = RenderSettings.sun;
                cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
                worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;
                gpuProjection = camera != null ? GraphicsConventions.GetComputeGpuProjection(camera) : Matrix4x4.identity;
                orthographic = camera != null && camera.orthographic;
                near = camera != null ? camera.nearClipPlane : 0.1f;
                far = camera != null ? camera.farClipPlane : 1000.0f;
                m_ZBinParams = LightBinning.ZBinParams(near, far, LightBinning.ZBinCount);
            }

            // The registry is the only source: InfinityAdditionalLightData registers itself before the frame's proxy update.
            using (LightCpuMarkers.s_Registry.Auto())
            {
                if (m_AdditionalData.Length < visibleLights.Length)
                {
                    m_AdditionalData = new InfinityAdditionalLightData[math.ceilpow2(visibleLights.Length)];
                }
                for (int i = 0; i < visibleLights.Length; ++i)
                {
                    Light light = visibleLights[i].light;
                    m_AdditionalData[i] = null;
                    if (light != null)
                    {
                        lights.TryGet(UnityEntityId.ToUInt64(light), out m_AdditionalData[i]);
                    }
                }
            }

            using (LightCpuMarkers.s_Pack.Auto())
            {
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        m_UnsupportedShapeCount++;
#endif
                        continue;
                    }

                    InfinityAdditionalLightData ext = m_AdditionalData[i];

                    LightSourceShape shape = LightRecordPack.ResolveShape(light, ext);
                    if (!LightRecordPack.TryPack(visible, light, ext, shape, i, cameraPosition, out LightRecord record))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        if (LightShapeUtility.IsValid(shape))
                        {
                            m_DistanceCulledCount++;
                        }
                        else
                        {
                            m_UnsupportedShapeCount++;
                        }
#endif
                        continue;
                    }

                    if (record.IsDirectional)
                    {
                        int mixedChannel = MixedLightChannel(light);
                        if (mixedChannel >= 0)
                        {
                            AssignMixedLightSlot(ref record, mixedChannel);
                        }
                        if (sun != null && light == sun)
                        {
                            m_SunRecordIndex = m_Records.Length;
                        }
                        m_Records.Add(record);
                        m_DirectionalCount++;
                        continue;
                    }

                    Vector3 centerVS = worldToView.MultiplyPoint3x4(new Vector3(record.positionRange.x, record.positionRange.y, record.positionRange.z));
                    float radius = LightShapeUtility.BoundingRadius(record);
                    bool tiled;
                    using (LightCpuMarkers.s_TileRect.Auto())
                    {
                        tiled = LightBinning.TryComputeTileRect(centerVS, radius, gpuProjection, orthographic, near, m_ScreenSize, out int4 rect);
                        if (tiled)
                        {
                            m_LocalCandidates.Add(new LocalCandidate { record = record, depth = -centerVS.z, tileRect = rect });
                        }
                    }
                    if (!tiled)
                    {
                        m_OffscreenCount++;
                    }
                }
            }

            if (m_LocalCandidates.Count > LightBinning.MaxLocalLights)
            {
                throw new InvalidOperationException($"InfinityRP: {m_LocalCandidates.Count} visible local lights exceed the light-list limit of {LightBinning.MaxLocalLights}.");
            }

            using (LightCpuMarkers.s_Sort.Auto())
            {
                m_LocalCandidates.Sort(s_ByDepth);
            }
            using (LightCpuMarkers.s_ZBin.Auto())
            {
                var depthRanges = new NativeArray<float2>(math.max(1, m_LocalCandidates.Count), Allocator.Temp);
                for (int i = 0; i < m_LocalCandidates.Count; ++i)
                {
                    LocalCandidate candidate = m_LocalCandidates[i];
                    float radius = LightShapeUtility.BoundingRadius(candidate.record);
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
            }

            using (LightCpuMarkers.s_Contact.Auto())
            {
                m_ContactLightCount = ContactShadowAllocation.Assign(m_Records, m_DirectionalCount, m_TileRects, m_LocalCount);
            }
            m_ShadowAllocator.Allocate(m_Records, cullingResults, m_AdditionalData, camera, settings, meshWorld, shadowPass, frame, shadowCache);
            using (LightCpuMarkers.s_EnsureCapacity.Auto())
            {
                EnsureCapacity(m_Records.Length, m_TileRects.Length, math.max(1, m_ShadowAllocator.LocalSliceCount));
            }
        }

        // Assigns cookie / IES atlas blocks to this camera's records (record.cookieIndex) and queues the uploads of new or
        // changed textures. The cookie texture is the Unity Light's; the IES profile is InfinityAdditionalLightData's.
        // Point lights need cubemap cookies (stored octahedral); every other shape takes a 2D cookie. Cube IES profiles
        // apply to every local shape by direction; 2D IES profiles to Cone, Pyramid, and Disk + Cone lights.
        internal void ResolveCookies(LightCookieAtlas atlas, in CullingResults cullingResults, int frame)
        {
            m_Cookies.Clear();
            NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
            for (int i = 0; i < m_Records.Length; ++i)
            {
                LightRecord record = m_Records[i];
                record.cookieIndex = -1;
                if (atlas != null && atlas.Enabled && record.visibleLightIndex >= 0 && record.visibleLightIndex < visibleLights.Length)
                {
                    Light light = visibleLights[record.visibleLightIndex].light;
                    InfinityAdditionalLightData ext = m_AdditionalData[record.visibleLightIndex];
                    if (light != null && TryResolveCookie(atlas, record, light, ext, frame, out LightCookieGPU cookie))
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

        static bool TryResolveCookie(LightCookieAtlas atlas, in LightRecord record, Light light, InfinityAdditionalLightData ext, int frame, out LightCookieGPU cookie)
        {
            cookie = default;
            bool directional = record.IsDirectional;
            bool point = !directional && (record.flags & (LightRecordFlags.SpotCone | LightRecordFlags.SpotPyramid | LightRecordFlags.SpotBox | LightRecordFlags.Planar)) == 0;
            bool projectedSpot = (record.flags & (LightRecordFlags.SpotCone | LightRecordFlags.SpotPyramid)) != 0;

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
                try
                {
                    rectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, matrices, LocalShadowSliceStride);
                }
                catch
                {
                    matrixBuffer.Release();
                    throw;
                }
                if (m_LocalShadowMatrixBuffer != null)
                {
                    m_RetiredBuffers.Add(m_LocalShadowMatrixBuffer);
                }
                if (m_LocalShadowRectBuffer != null)
                {
                    m_RetiredBuffers.Add(m_LocalShadowRectBuffer);
                }
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

            if (m_Cookies.Count > 0)
            {
                if (m_CookieUpload.Length < m_Cookies.Count)
                {
                    m_CookieUpload = new LightCookieGPU[m_Cookies.Count];
                }
                m_Cookies.CopyTo(m_CookieUpload);
                cmdBuffer.SetBufferData(m_CookieBuffer, m_CookieUpload, 0, 0, m_Cookies.Count);
            }
        }

        internal bool TryGetSunRecord(out LightRecord record)
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
                LightRecord record = m_Records[i];
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
                LocalShadowSlice slice = m_ShadowAllocator.LocalSlices[i];
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

        // The sun the atmosphere scatters is the camera's sun record. Without one there is no sun: its radiance is zero,
        // and the direction (straight up) only keeps the lookup tables' parameterisation defined.
        internal static void ResolveSun(LightContext context, out Vector4 direction, out Vector4 radiance)
        {
            if (context.TryGetSunRecord(out LightRecord sun))
            {
                direction = -sun.forward;
                direction.w = 0;
                radiance = sun.radiance;
                return;
            }
            direction = new Vector4(0, 1, 0, 0);
            radiance = Vector4.zero;
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
            if (m_Records.IsCreated)
            {
                m_Records.Dispose();
            }
            if (m_TileRects.IsCreated)
            {
                m_TileRects.Dispose();
            }
            if (m_ZBins.IsCreated)
            {
                m_ZBins.Dispose();
            }
        }
    }
}
