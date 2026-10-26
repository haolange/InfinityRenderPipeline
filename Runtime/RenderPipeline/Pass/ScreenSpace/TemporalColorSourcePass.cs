using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct FTemporalColorView
    {
        internal RGTextureRef color, depth, mips;
        internal Vector4 resolution, jitterDelta;
        internal int mipCount;
        internal bool valid;
    }

    public partial class InfinityRenderPipeline
    {
        struct TemporalColorMipData
        {
            internal FTemporalColorView source;
            internal ComputeShader shader;
            internal int kernel, mipCount;
            internal int2 size;
        }
        static class TemporalColorBindings
        {
            internal static readonly int Color = Shader.PropertyToID("SRV_TemporalColor"), Depth = Shader.PropertyToID("SRV_TemporalDepth"),
                Mips = Shader.PropertyToID("SRV_TemporalColorMips"), Valid = Shader.PropertyToID("TemporalColorValid"),
                Resolution = Shader.PropertyToID("TemporalColorResolution"), Jitter = Shader.PropertyToID("TemporalColorJitterDelta"),
                MipCount = Shader.PropertyToID("TemporalColorMipCount"), SourceDepth = Shader.PropertyToID("_TemporalSurfaceDepth"),
                SourceValid = Shader.PropertyToID("_TemporalSourceValid"), SourceMip = Shader.PropertyToID("_TemporalSourceMip"), First = Shader.PropertyToID("_TemporalMipFirst"),
                Far = Shader.PropertyToID("_TemporalFarDepth");
        }
        void ComputeTemporalColorPyramid()
        {
            if (!ShouldRecordFeature(EFrameFeature.TemporalColorPyramid)) return;
            var state = m_ActiveFrameState;
            bool sr = state.dimensions.superResolution;
            int2 size = sr ? state.dimensions.displaySize : state.dimensions.internalSize;
            var domain = sr ? ETextureSizingDomain.Display : ETextureSizingDomain.Internal;
            int colorID = sr ? SuperResolutionPassUtilityData.HistoryColorTextureID : AntiAliasingUtilityData.HistoryColorTextureID;
            int depthID = sr ? SuperResolutionPassUtilityData.HistoryDepthTextureID : AntiAliasingUtilityData.HistoryDepthTextureID;
            var color = state.historyCache.GetTexture(colorID, TemporalColorDescriptor(size.x, size.y,
                sr ? "HistorySuperResolutionColor" : "HistoryColorTexture", true, domain), out bool colorCreated);
            var depth = state.historyCache.GetTexture(depthID, TemporalDepthDescriptor(size.x, size.y,
                sr ? "HistorySuperResolutionDepth" : "HistoryDepthTexture", true, domain), out bool depthCreated);
            int2 baseSize = math.max(1, size / 2);
            var descriptor = TemporalColorDescriptor(baseSize.x, baseSize.y, "TemporalColorMips", true, domain);
            descriptor.viewDivisor = 2; descriptor.useMipMap = true; descriptor.autoGenerateMips = false;
            descriptor.filterMode = FilterMode.Bilinear;
            var mips = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.TemporalColorMipBuffer, descriptor);
            Vector2 currentJitter = ProjectionJitterUV(state.cameraUniform.matrix_FlipYJitterProj, state.cameraUniform.matrix_FlipYProj);
            var source = new FTemporalColorView
            {
                color = m_RGBuilder.ImportTexture(color), depth = m_RGBuilder.ImportTexture(depth), mips = mips,
                resolution = new Vector4(size.x, size.y, 1f / size.x, 1f / size.y),
                jitterDelta = new Vector4(state.cameraUniform.previousJitterUV.x - currentJitter.x,
                    state.cameraUniform.previousJitterUV.y - currentJitter.y, 0, 0),
                mipCount = PyramidMipBatch.MipCount(size.x, size.y),
                valid = state.acceptedTemporalColor && state.acceptedTemporalColorIsSR == sr
                    && !state.cameraUniform.historyReset && !colorCreated && !depthCreated
                    && state.historyCache.TextureGeneration(colorID) > 0 && state.historyCache.TextureGeneration(depthID) > 0
            };
            state.temporalColorView = source;
            m_RGScoper.RegisterTexture(InfinityShaderIDs.TemporalColorSourceBuffer, source.color);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.TemporalColorDepthBuffer, source.depth);
            RenderCaptureService.current?.RecordTemporalColorState(state.camera, source, sr);
            using (var pass = m_RGBuilder.AddComputePass<TemporalColorMipData>(ProfilingSampler.Get(CustomSamplerId.ComputeTemporalColorPyramid)))
            {
                pass.EnableAsyncCompute(true);
                ref var data = ref pass.GetPassData<TemporalColorMipData>(); data = default;
                data.shader = shaders.colorPyramidShader; data.kernel = data.shader.FindKernel("TemporalColorMip");
                data.source = source; data.source.color = pass.ReadTexture(source.color);
                data.source.depth = pass.ReadTexture(source.depth); data.size = size;
                data.mipCount = PyramidMipBatch.MipCount(baseSize.x, baseSize.y);
                pass.WriteTexture(mips);
                pass.SetExecuteFunc((in TemporalColorMipData data, in RGComputeEncoder commands) =>
                {
                    var shader = data.shader; int kernel = data.kernel;
                    commands.SetComputeIntParam(shader, TemporalColorBindings.SourceValid, data.source.valid ? 1 : 0);
                    commands.SetComputeFloatParam(shader, TemporalColorBindings.Far, GraphicsUtility.SampledFarDepth);
                    commands.SetComputeTextureParam(shader, kernel, TemporalColorBindings.SourceDepth, data.source.depth);
                    int2 inputSize = data.size;
                    for (int mip = 0; mip < data.mipCount; ++mip)
                    {
                        int2 outputSize = math.max(1, data.size / (1 << (mip + 1)));
                        commands.SetComputeIntParam(shader, TemporalColorBindings.First, mip == 0 ? 1 : 0);
                        commands.SetComputeIntParam(shader, TemporalColorBindings.SourceMip, math.max(0,mip-1));
                        commands.SetComputeVectorParam(shader, ColorPyramidPassUtilityData.ColorPyramid_SizeID, new Vector4(inputSize.x,inputSize.y,0,0));
                        commands.SetComputeVectorParam(shader, ColorPyramidPassUtilityData.ColorPyramid_DstSizeID, new Vector4(outputSize.x,outputSize.y,0,0));
                        commands.SetComputeTextureParam(shader, kernel, ColorPyramidPassUtilityData.SRV_ColorTextureID,
                            mip == 0 ? data.source.color : data.source.mips);
                        commands.SetComputeTextureParam(shader, kernel, ColorPyramidPassUtilityData.UAV_ColorPyramidID, data.source.mips, mip);
                        commands.DispatchCompute(shader,kernel,(outputSize.x+7)/8,(outputSize.y+7)/8,1);
                        inputSize = outputSize;
                    }
                });
            }
            MarkFeatureProduced(EFrameFeature.TemporalColorPyramid);
        }
        static FTemporalColorView ReadTemporalColorView(RGComputePassRef pass, FTemporalColorView source)
        {
            source.color = pass.ReadTexture(source.color); source.depth = pass.ReadTexture(source.depth);
            source.mips = pass.ReadTexture(source.mips); return source;
        }
        static void BindTemporalColorView(in RGComputeEncoder commands, ComputeShader shader, int kernel, in FTemporalColorView source)
        {
            commands.SetComputeIntParam(shader, TemporalColorBindings.Valid, source.valid ? 1 : 0);
            commands.SetComputeIntParam(shader, TemporalColorBindings.MipCount, source.mipCount);
            commands.SetComputeVectorParam(shader, TemporalColorBindings.Resolution, source.resolution);
            commands.SetComputeVectorParam(shader, TemporalColorBindings.Jitter, source.jitterDelta);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Color,source.color);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Depth,source.depth);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Mips,source.mips);
        }
    }
}
