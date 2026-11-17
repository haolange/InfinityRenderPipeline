using System.Runtime.InteropServices;
using UnityEngine;
using InfinityTech.Core;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_LightTileParameters Shaders/Pass/Lighting/Compute_LightTileMask.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct LightTileParameters
    {
        public Vector4 tileCount;
        public uint itemCount;
        public uint wordCount;
        public uint padding0;
        public uint padding1;
    }

    internal static class LightTileMaskPassUtilityData
    {
        internal static readonly int CBV_LightTileParametersID = Shader.PropertyToID("CBV_LightTileParameters");
        internal const string MaskBufferName = "LightTileMasks";
        internal static readonly int UAV_LightTileMasksID = Shader.PropertyToID("UAV_LightTileMasks");
    }

    // The camera's light-loop constants as Shaders/Library/Lighting/LightLoopUniform.hlsl reads them, uploaded once per
    // camera before its graph (ResolveLightLoopUniform) and bound globally for raster and compute passes.
    // GPU-RECORD: LightLoopUniform Shaders/Library/Lighting/LightLoopUniform.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct LightLoopUniform
    {
        public Vector4 tileParameters;
        public Vector4 zBinParameters;
        public Vector4 probeTileParameters;
        public int directionalCount;
        public int localCount;
        public int sunRecordIndex;
        public int probeCount;
    }

    // Resources of the shared light loop (Shaders/Library/Lighting/LightLoop.hlsl), the light cookies
    // (Lighting/LightCookie.hlsl) and the reflection probes (Lighting/ReflectionProbes.hlsl). Query once at record time,
    // declare the reads with Read, then bind. Each atlas is declared by its shader only under its keyword
    // (INFINITY_LIGHT_COOKIES, INFINITY_REFLECTION_PROBES), set from hasCookieAtlas / hasProbeAtlas, and is bound only
    // when present; without it no record references a cookie and the probe count is 0.
    internal struct LightLoopInputs
    {
        public RGBufferHandle zBinsHandle;
        public RGBufferHandle tileMasksHandle;
        public RGBufferHandle cookiesHandle;
        public RGTextureHandle cookieAtlasHandle;
        public bool hasCookieAtlas;
        public RGBufferHandle probesHandle;
        public RGBufferHandle probeZBinsHandle;
        public RGBufferHandle probeTileMasksHandle;
        public RGTextureHandle probeAtlasHandle;
        public bool hasProbeAtlas;

        public void Read<THandle>(THandle passHandle) where THandle : struct, IPassHandle
        {
            passHandle.ReadBuffer(zBinsHandle);
            passHandle.ReadBuffer(tileMasksHandle);
            passHandle.ReadBuffer(cookiesHandle);
            if (hasCookieAtlas)
            {
                passHandle.ReadTexture(cookieAtlasHandle);
            }
            passHandle.ReadBuffer(probesHandle);
            passHandle.ReadBuffer(probeZBinsHandle);
            passHandle.ReadBuffer(probeTileMasksHandle);
            if (hasProbeAtlas)
            {
                passHandle.ReadTexture(probeAtlasHandle);
            }
        }

        public void Bind<TCommands>(in TCommands commands, ComputeShader shader, int kernel) where TCommands : struct, IComputeCommands
        {
            commands.SetKeyword(LightShaderIDs.ReflectionProbesKeyword, hasProbeAtlas);
            commands.SetKeyword(LightShaderIDs.LightCookiesKeyword, hasCookieAtlas);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.ReflectionProbes, probesHandle);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.ProbeZBins, probeZBinsHandle);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.ProbeTileMasks, probeTileMasksHandle);
            if (hasProbeAtlas)
            {
                commands.SetComputeTextureParam(shader, kernel, LightShaderIDs.ReflectionProbeAtlas, probeAtlasHandle);
            }
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightCookies, cookiesHandle);
            if (hasCookieAtlas)
            {
                commands.SetComputeTextureParam(shader, kernel, LightShaderIDs.LightCookieAtlas, cookieAtlasHandle);
            }
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightZBins, zBinsHandle);
            commands.SetComputeBufferParam(shader, kernel, LightShaderIDs.LightTileMasks, tileMasksHandle);
        }

        public void BindGlobal<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
        {
            commands.SetKeyword(LightShaderIDs.ReflectionProbesKeyword, hasProbeAtlas);
            commands.SetKeyword(LightShaderIDs.LightCookiesKeyword, hasCookieAtlas);
            commands.SetGlobalBuffer(LightShaderIDs.ReflectionProbes, probesHandle);
            commands.SetGlobalBuffer(LightShaderIDs.ProbeZBins, probeZBinsHandle);
            commands.SetGlobalBuffer(LightShaderIDs.ProbeTileMasks, probeTileMasksHandle);
            if (hasProbeAtlas)
            {
                commands.SetGlobalTexture(LightShaderIDs.ReflectionProbeAtlas, probeAtlasHandle);
            }
            commands.SetGlobalBuffer(LightShaderIDs.LightCookies, cookiesHandle);
            if (hasCookieAtlas)
            {
                commands.SetGlobalTexture(LightShaderIDs.LightCookieAtlas, cookieAtlasHandle);
            }
            commands.SetGlobalBuffer(LightShaderIDs.LightZBins, zBinsHandle);
            commands.SetGlobalBuffer(LightShaderIDs.LightTileMasks, tileMasksHandle);
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct LightTileMaskPassData
        {
            public RGConstantsHandle parametersHandle;
            public int wordCount;
            public int2 tileCount;
            public ComputeKernel kernel;
            public RGBufferHandle tileRectsHandle;
            public RGBufferHandle tileMasksHandle;
        }

        // The camera's light-loop constants, from the light context built after culling. Probes count only when the
        // reflection probe feature records a probe list this frame.
        LightLoopUniform ResolveLightLoopUniform(FrameFeatureSet features)
        {
            LightContext lightContext = renderContext.sceneWorld.lights.context;
            bool hasProbeList = features.ShouldRecord(EFrameFeature.ReflectionProbes) && lightContext.ReflectionProbeCount > 0;
            return new LightLoopUniform
            {
                tileParameters = lightContext.TileParams,
                zBinParameters = lightContext.ZBinParams,
                probeTileParameters = lightContext.ProbeTileParams,
                directionalCount = lightContext.DirectionalLightCount,
                localCount = lightContext.LocalLightCount,
                sunRecordIndex = lightContext.SunRecordIndex,
                probeCount = hasProbeList ? lightContext.ReflectionProbeCount : 0
            };
        }

        // The light loop's buffers for this camera: the GPU tile masks when local lights are visible, otherwise a
        // one-word empty mask (the z-bins are all empty, so no pixel reads past it).
        LightLoopInputs QueryLightLoop(in CameraRecordContext context)
        {
            LightContext lightContext = renderContext.sceneWorld.lights.context;
            bool hasCookieAtlas = context.features.ShouldRecord(EFrameFeature.LightCookies);
            bool hasProbeAtlas = context.features.ShouldRecord(EFrameFeature.ReflectionProbes);
            RGTextureHandle cookieAtlasHandle = hasCookieAtlas ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightCookieAtlas) : default;
            RGTextureHandle probeAtlasHandle = hasProbeAtlas ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ReflectionProbeAtlas) : default;
            bool hasProbeList = hasProbeAtlas && lightContext.ReflectionProbeCount > 0;
            return new LightLoopInputs
            {
                probesHandle = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.ReflectionProbeBuffer),
                probeZBinsHandle = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.ProbeZBinBuffer),
                probeTileMasksHandle = hasProbeList
                    ? m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.ProbeTileMaskBuffer)
                    : m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.EmptyLightTileMaskBuffer),
                probeAtlasHandle = probeAtlasHandle,
                hasProbeAtlas = hasProbeAtlas,
                cookiesHandle = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightCookieBuffer),
                cookieAtlasHandle = cookieAtlasHandle,
                hasCookieAtlas = hasCookieAtlas,
                zBinsHandle = m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightZBinBuffer),
                tileMasksHandle = lightContext.HasLocalLightList()
                    ? m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.LightTileMaskBuffer)
                    : m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.EmptyLightTileMaskBuffer),
            };
        }

        void ComputeLightTileMask(in CameraRecordContext context)
        {
            LightContext lightContext = renderContext.sceneWorld.lights.context;
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
            RGBufferHandle tileMasksHandle = m_RGBuilder.scoper.CreateBuffer(masksKey, new BufferDescriptor
            {
                name = masksName,
                count = math.max(1, tileCount.x * tileCount.y * wordCount),
                stride = sizeof(uint),
                target = GraphicsBuffer.Target.Structured
            });

            {
                RGComputePassHandle<LightTileMaskPassData> passHandle = m_RGBuilder.AddComputePass<LightTileMaskPassData>(ProfilingSampler.Get(sampler));
                ref LightTileMaskPassData passData = ref passHandle.GetPassData();
                passData.parametersHandle = passHandle.WriteConstants(new LightTileParameters
                {
                    tileCount = new Vector4(tileCount.x, tileCount.y, 0, 0),
                    itemCount = (uint)itemCount,
                    wordCount = (uint)wordCount
                });
                passData.wordCount = wordCount;
                passData.tileCount = tileCount;
                passData.kernel = kernels.lightTileMask;
                passData.tileRectsHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(rectsKey));
                passData.tileMasksHandle = passHandle.WriteBuffer(tileMasksHandle);

                passHandle.EnablePassCulling(false);
                passHandle.EnableAsyncCompute(true);
                passHandle.SetExecuteFunc((in LightTileMaskPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeKernel kernel = passData.kernel;
                    ComputeShader shader = kernel.shader;
                    int total = passData.tileCount.x * passData.tileCount.y * passData.wordCount;
                    cmdEncoder.SetComputeConstantBufferParam(shader, LightTileMaskPassUtilityData.CBV_LightTileParametersID, passData.parametersHandle);
                    cmdEncoder.SetComputeBufferParam(shader, kernel.index, LightShaderIDs.LightTileRects, passData.tileRectsHandle);
                    cmdEncoder.SetComputeBufferParam(shader, kernel.index, LightTileMaskPassUtilityData.UAV_LightTileMasksID, passData.tileMasksHandle);
                    cmdEncoder.DispatchCompute(shader, kernel.index, kernel.GroupsX(total), 1, 1);
                });
            }
        }
    }
}
