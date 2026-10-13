using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class LightTileMaskPassUtilityData
    {
        internal const string KernelName = "BuildTileMask";
        internal const string MaskBufferName = "LightTileMasks";
        internal static readonly int LightTile_LocalCountID = Shader.PropertyToID("LightTile_LocalCount");
        internal static readonly int LightTile_WordCountID = Shader.PropertyToID("LightTile_WordCount");
        internal static readonly int LightTile_TileCountID = Shader.PropertyToID("LightTile_TileCount");
        internal static readonly int UAV_LightTileMasksID = Shader.PropertyToID("UAV_LightTileMasks");
    }

    // Inputs of the shared light loop (Shaders/ShaderLibrary/Lighting/LightLoop.hlsl) and of the light cookies
    // (Lighting/LightCookie.hlsl). Query once at record time, declare the reads with Read, then bind. Without cookies
    // this frame the atlas binds a white texture; no record references it then.
    internal struct FLightLoopInputs
    {
        public RGBufferRef zBins;
        public RGBufferRef tileMasks;
        public RGBufferRef cookies;
        public RGTextureRef cookieAtlas;
        public bool hasCookieAtlas;
        public Vector4 tileParams;
        public Vector4 zBinParams;
        public int directionalCount;
        public int localCount;
        public int sunRecordIndex;

        public void Read(RGComputePassRef passRef)
        {
            passRef.ReadBuffer(zBins);
            passRef.ReadBuffer(tileMasks);
            passRef.ReadBuffer(cookies);
            if (hasCookieAtlas) passRef.ReadTexture(cookieAtlas);
        }

        public void Read(RGRasterPassRef passRef)
        {
            passRef.ReadBuffer(zBins);
            passRef.ReadBuffer(tileMasks);
            passRef.ReadBuffer(cookies);
            if (hasCookieAtlas) passRef.ReadTexture(cookieAtlas);
        }

        public void Bind<TCommands>(in TCommands commands, ComputeShader shader, int kernel) where TCommands : struct, IComputeCommands
        {
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightCookies, cookies);
            commands.SetComputeTextureParam(shader, kernel, LightShaderIDs.LightCookieAtlas, hasCookieAtlas ? (RenderTargetIdentifier)cookieAtlas : Texture2D.whiteTexture);
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
            commands.SetGlobalBuffer(LightShaderIDs.LightCookies, cookies);
            commands.SetGlobalTexture(LightShaderIDs.LightCookieAtlas, hasCookieAtlas ? (RenderTargetIdentifier)cookieAtlas : Texture2D.whiteTexture);
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
            public ComputeShader shader;
            public RGBufferRef tileRects;
            public RGBufferRef tileMasks;
        }

        // The light loop's buffers for this camera: the GPU tile masks when local lights are visible, otherwise a
        // one-word empty mask (the z-bins are all empty, so no pixel reads past it).
        FLightLoopInputs QueryLightLoop(RenderContext renderContext)
        {
            LightContext context = renderContext.lightContext;
            bool hasCookieAtlas = m_RGScoper.TryQueryTexture(InfinityShaderIDs.LightCookieAtlas, out RGTextureRef cookieAtlas);
            return new FLightLoopInputs
            {
                cookies = m_RGScoper.QueryBuffer(LightShaderIDs.LightCookieBuffer),
                cookieAtlas = cookieAtlas,
                hasCookieAtlas = hasCookieAtlas,
                zBins = m_RGScoper.QueryBuffer(LightShaderIDs.LightZBinBuffer),
                tileMasks = context.HasLocalLightList()
                    ? m_RGScoper.QueryBuffer(InfinityShaderIDs.LightTileMaskBuffer)
                    : m_RGScoper.QueryBuffer(LightShaderIDs.EmptyLightTileMaskBuffer),
                tileParams = context.TileParams,
                zBinParams = context.ZBinParams,
                directionalCount = context.DirectionalLightCount,
                localCount = context.LocalLightCount,
                sunRecordIndex = context.SunRecordIndex,
            };
        }

        void ComputeLightTileMask(RenderContext renderContext, Camera camera)
        {
            LightContext context = renderContext?.lightContext;
            if (context == null || !context.HasLocalLightList())
            {
                return;
            }

            // Visible local lights make the list a required producer: a blocked feature fails at record time.
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.LocalLightList);

            int2 tileCount = context.TileCount;
            int wordCount = context.TileWordCount;
            RGBufferRef tileMasks = m_RGScoper.CreateBuffer(InfinityShaderIDs.LightTileMaskBuffer, new BufferDescriptor
            {
                name = LightTileMaskPassUtilityData.MaskBufferName,
                count = math.max(1, tileCount.x * tileCount.y * wordCount),
                stride = sizeof(uint),
                type = ComputeBufferType.Structured
            });

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<LightTileMaskPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeLightTileMask)))
            {
                ref LightTileMaskPassData passData = ref passRef.GetPassData<LightTileMaskPassData>();
                passData.localCount = context.LocalLightCount;
                passData.wordCount = wordCount;
                passData.tileCount = tileCount;
                passData.shader = shaders.lightTileMaskShader;
                passData.tileRects = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightTileRectBuffer));
                passData.tileMasks = passRef.WriteBuffer(tileMasks);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in LightTileMaskPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.shader;
                    int kernel = shader.FindKernel(LightTileMaskPassUtilityData.KernelName);
                    int total = passData.tileCount.x * passData.tileCount.y * passData.wordCount;
                    cmdEncoder.SetComputeIntParam(shader, LightTileMaskPassUtilityData.LightTile_LocalCountID, passData.localCount);
                    cmdEncoder.SetComputeIntParam(shader, LightTileMaskPassUtilityData.LightTile_WordCountID, passData.wordCount);
                    cmdEncoder.SetComputeVectorParam(shader, LightTileMaskPassUtilityData.LightTile_TileCountID, new Vector4(passData.tileCount.x, passData.tileCount.y, 0, 0));
                    cmdEncoder.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightTileRects, passData.tileRects);
                    cmdEncoder.SetComputeBufferParam(shader, kernel, LightTileMaskPassUtilityData.UAV_LightTileMasksID, passData.tileMasks);
                    cmdEncoder.DispatchCompute(shader, kernel, (total + 63) / 64, 1, 1);
                });
            }
            MarkFeatureProduced(EFrameFeature.LocalLightList);
        }
    }
}
