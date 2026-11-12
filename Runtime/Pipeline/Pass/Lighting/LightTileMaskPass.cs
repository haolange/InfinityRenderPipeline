using UnityEngine;
using InfinityTech.Core;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class LightTileMaskPassUtilityData
    {
        internal const string MaskBufferName = "LightTileMasks";
        internal static readonly int LightTile_LocalCountID = Shader.PropertyToID("LightTile_LocalCount");
        internal static readonly int LightTile_WordCountID = Shader.PropertyToID("LightTile_WordCount");
        internal static readonly int LightTile_TileCountID = Shader.PropertyToID("LightTile_TileCount");
        internal static readonly int UAV_LightTileMasksID = Shader.PropertyToID("UAV_LightTileMasks");
    }

    // Inputs of the shared light loop (Shaders/Library/Lighting/LightLoop.hlsl), the light cookies
    // (Lighting/LightCookie.hlsl) and the reflection probes (Lighting/ReflectionProbes.hlsl). Query once at record time,
    // declare the reads with Read, then bind. Without cookies this frame the cookie atlas binds a white texture; no
    // record references it then.
    internal struct LightLoopInputs
    {
        public RGBufferHandle zBins;
        public RGBufferHandle tileMasks;
        public RGBufferHandle cookies;
        public RGTextureHandle cookieAtlas;
        public bool hasCookieAtlas;
        public RGBufferHandle probes;
        public RGBufferHandle probeZBins;
        public RGBufferHandle probeTileMasks;
        public RGTextureHandle probeAtlas;
        public bool hasProbeAtlas;
        public int probeCount;
        public Vector4 probeTileParams;
        public Vector4 tileParams;
        public Vector4 zBinParams;
        public int directionalCount;
        public int localCount;
        public int sunRecordIndex;

        // Bound for SRV_ReflectionProbeAtlas when no probe is in use (shaders never read it then).
        static Texture2DArray s_EmptyProbeAtlas;
        static Texture2DArray EmptyProbeAtlas
        {
            get
            {
                if (s_EmptyProbeAtlas == null)
                {
                    s_EmptyProbeAtlas = new Texture2DArray(1, 1, 6, TextureFormat.RGBAHalf, false, true) { name = "EmptyReflectionProbeAtlas", hideFlags = HideFlags.HideAndDontSave };
                    s_EmptyProbeAtlas.Apply(false, true);
                }
                return s_EmptyProbeAtlas;
            }
        }

        public void Read<TPassData>(RGComputePassHandle<TPassData> passRef) where TPassData : struct
        {
            passRef.ReadBuffer(zBins);
            passRef.ReadBuffer(tileMasks);
            passRef.ReadBuffer(cookies);
            if (hasCookieAtlas)
            {
                passRef.ReadTexture(cookieAtlas);
            }
            passRef.ReadBuffer(probes);
            passRef.ReadBuffer(probeZBins);
            passRef.ReadBuffer(probeTileMasks);
            if (hasProbeAtlas)
            {
                passRef.ReadTexture(probeAtlas);
            }
        }

        public void Read<TPassData>(RGRasterPassHandle<TPassData> passRef) where TPassData : struct
        {
            passRef.ReadBuffer(zBins);
            passRef.ReadBuffer(tileMasks);
            passRef.ReadBuffer(cookies);
            if (hasCookieAtlas)
            {
                passRef.ReadTexture(cookieAtlas);
            }
            passRef.ReadBuffer(probes);
            passRef.ReadBuffer(probeZBins);
            passRef.ReadBuffer(probeTileMasks);
            if (hasProbeAtlas)
            {
                passRef.ReadTexture(probeAtlas);
            }
        }

        public void Bind<TCommands>(in TCommands commands, ComputeShader shader, int kernel) where TCommands : struct, IComputeCommands
        {
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.ReflectionProbes, probes);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.ProbeZBins, probeZBins);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.ProbeTileMasks, probeTileMasks);
            if (hasProbeAtlas)
            {
                commands.SetComputeTextureParam(shader, kernel, LightShaderIDs.ReflectionProbeAtlas, probeAtlas);
            }
            else
            {
                commands.SetComputeTextureParam(shader, kernel, LightShaderIDs.ReflectionProbeAtlas, EmptyProbeAtlas, TextureView.Whole(EmptyProbeAtlas));
            }
            commands.SetComputeIntParam(shader, LightShaderIDs.ReflectionProbeCount, probeCount);
            commands.SetComputeVectorParam(shader, LightShaderIDs.ProbeTileParams, probeTileParams);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightCookies, cookies);
            if (hasCookieAtlas)
            {
                commands.SetComputeTextureParam(shader, kernel, LightShaderIDs.LightCookieAtlas, cookieAtlas);
            }
            else
            {
                commands.SetComputeTextureParam(shader, kernel, LightShaderIDs.LightCookieAtlas, Texture2D.whiteTexture, TextureView.Whole(Texture2D.whiteTexture));
            }
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightZBins, zBins);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightTileMasks, tileMasks);
            commands.SetComputeVectorParam(shader, LightShaderIDs.LightTileParams, tileParams);
            commands.SetComputeVectorParam(shader, LightShaderIDs.LightZBinParams, zBinParams);
            commands.SetComputeIntParam(shader, LightShaderIDs.DirectionalLightCount, directionalCount);
            commands.SetComputeIntParam(shader, LightShaderIDs.LocalLightCount, localCount);
            commands.SetComputeIntParam(shader, LightShaderIDs.SunRecordIndex, sunRecordIndex);
        }

        public void BindGlobal<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
        {
            commands.SetGlobalBuffer(LightShaderIDs.ReflectionProbes, probes);
            commands.SetGlobalBuffer(LightShaderIDs.ProbeZBins, probeZBins);
            commands.SetGlobalBuffer(LightShaderIDs.ProbeTileMasks, probeTileMasks);
            if (hasProbeAtlas)
            {
                commands.SetGlobalTexture(LightShaderIDs.ReflectionProbeAtlas, probeAtlas);
            }
            else
            {
                commands.SetGlobalTexture(LightShaderIDs.ReflectionProbeAtlas, EmptyProbeAtlas, TextureView.Whole(EmptyProbeAtlas));
            }
            commands.SetGlobalInt(LightShaderIDs.ReflectionProbeCount, probeCount);
            commands.SetGlobalVector(LightShaderIDs.ProbeTileParams, probeTileParams);
            commands.SetGlobalBuffer(LightShaderIDs.LightCookies, cookies);
            if (hasCookieAtlas)
            {
                commands.SetGlobalTexture(LightShaderIDs.LightCookieAtlas, cookieAtlas);
            }
            else
            {
                commands.SetGlobalTexture(LightShaderIDs.LightCookieAtlas, Texture2D.whiteTexture, TextureView.Whole(Texture2D.whiteTexture));
            }
            commands.SetGlobalBuffer(LightShaderIDs.LightZBins, zBins);
            commands.SetGlobalBuffer(LightShaderIDs.LightTileMasks, tileMasks);
            commands.SetGlobalVector(LightShaderIDs.LightTileParams, tileParams);
            commands.SetGlobalVector(LightShaderIDs.LightZBinParams, zBinParams);
            commands.SetGlobalInt(LightShaderIDs.DirectionalLightCount, directionalCount);
            commands.SetGlobalInt(LightShaderIDs.LocalLightCount, localCount);
            commands.SetGlobalInt(LightShaderIDs.SunRecordIndex, sunRecordIndex);
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct LightTileMaskPassData
        {
            public int localCount;
            public int wordCount;
            public int2 tileCount;
            public ComputeKernel kernel;
            public RGBufferHandle tileRects;
            public RGBufferHandle tileMasks;
        }

        // The light loop's buffers for this camera: the GPU tile masks when local lights are visible, otherwise a
        // one-word empty mask (the z-bins are all empty, so no pixel reads past it).
        LightLoopInputs QueryLightLoop()
        {
            LightContext lightContext = renderContext.lightContext;
            bool hasCookieAtlas = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.LightCookieAtlas, out RGTextureHandle cookieAtlas);
            bool hasProbeAtlas = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.ReflectionProbeAtlas, out RGTextureHandle probeAtlas);
            bool hasProbeList = hasProbeAtlas && lightContext.ReflectionProbeCount > 0;
            return new LightLoopInputs
            {
                probes = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.ReflectionProbeBuffer),
                probeZBins = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.ProbeZBinBuffer),
                probeTileMasks = hasProbeList
                    ? m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.ProbeTileMaskBuffer)
                    : m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.EmptyLightTileMaskBuffer),
                probeAtlas = probeAtlas,
                hasProbeAtlas = hasProbeAtlas,
                probeCount = hasProbeList ? lightContext.ReflectionProbeCount : 0,
                probeTileParams = lightContext.ProbeTileParams,
                cookies = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightCookieBuffer),
                cookieAtlas = cookieAtlas,
                hasCookieAtlas = hasCookieAtlas,
                zBins = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightZBinBuffer),
                tileMasks = lightContext.HasLocalLightList()
                    ? m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.LightTileMaskBuffer)
                    : m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.EmptyLightTileMaskBuffer),
                tileParams = lightContext.TileParams,
                zBinParams = lightContext.ZBinParams,
                directionalCount = lightContext.DirectionalLightCount,
                localCount = lightContext.LocalLightCount,
                sunRecordIndex = lightContext.SunRecordIndex,
            };
        }

        void ComputeLightTileMask(in CameraRecordContext context)
        {
            LightContext lightContext = renderContext.lightContext;
            if (!lightContext.HasLocalLightList())
            {
                return;
            }

            // Visible local lights make the list a required producer: a blocked feature fails at record time.
            context.features.ThrowIfCannotProduce(EFrameFeature.LocalLightList);

            RecordTileMaskPass(LightShaderIDs.LightTileRectBuffer, InfinityShaderIDs.LightTileMaskBuffer, LightTileMaskPassUtilityData.MaskBufferName,
                lightContext.LocalLightCount, lightContext.TileWordCount, lightContext.TileCount, CustomSamplerId.ComputeLightTileMask);
            context.features.MarkProduced(EFrameFeature.LocalLightList);
        }

        // Builds tile bitmasks (tiles x words) from per-item tile rectangles: local lights, and reflection probes as the
        // second index space of the clustered list.
        void RecordTileMaskPass(int rectsKey, int masksKey, string masksName, int itemCount, int wordCount, int2 tileCount, CustomSamplerId sampler)
        {
            RGBufferHandle tileMasks = m_RGBuilder.scoper.CreateBuffer(masksKey, new BufferDescriptor
            {
                name = masksName,
                count = math.max(1, tileCount.x * tileCount.y * wordCount),
                stride = sizeof(uint),
                target = GraphicsBuffer.Target.Structured
            });

            using (var passRef = m_RGBuilder.AddComputePass<LightTileMaskPassData>(ProfilingSampler.Get(sampler)))
            {
                ref LightTileMaskPassData passData = ref passRef.GetPassData();
                passData.localCount = itemCount;
                passData.wordCount = wordCount;
                passData.tileCount = tileCount;
                passData.kernel = kernels.lightTileMask;
                passData.tileRects = passRef.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(rectsKey));
                passData.tileMasks = passRef.WriteBuffer(tileMasks);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in LightTileMaskPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeKernel kernel = passData.kernel;
                    ComputeShader shader = kernel.shader;
                    int total = passData.tileCount.x * passData.tileCount.y * passData.wordCount;
                    cmdEncoder.SetComputeIntParam(shader, LightTileMaskPassUtilityData.LightTile_LocalCountID, passData.localCount);
                    cmdEncoder.SetComputeIntParam(shader, LightTileMaskPassUtilityData.LightTile_WordCountID, passData.wordCount);
                    cmdEncoder.SetComputeVectorParam(shader, LightTileMaskPassUtilityData.LightTile_TileCountID, new Vector4(passData.tileCount.x, passData.tileCount.y, 0, 0));
                    cmdEncoder.SetComputeBufferParam(shader, kernel.index, LightShaderIDs.LightTileRects, passData.tileRects);
                    cmdEncoder.SetComputeBufferParam(shader, kernel.index, LightTileMaskPassUtilityData.UAV_LightTileMasksID, passData.tileMasks);
                    cmdEncoder.DispatchCompute(shader, kernel.index, kernel.GroupsX(total), 1, 1);
                });
            }
        }
    }
}
