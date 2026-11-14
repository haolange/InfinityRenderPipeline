using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct TemporalColorView
    {
        internal RGTextureHandle color, depth, mips;
        // Written by the reading pass for its own execution; bound by BindTemporalColorView.
        internal RGConstantsHandle parameters;
        internal Vector4 resolution;
        internal int mipCount;
        internal bool valid;
    }

    // GPU-RECORD: CBV_TemporalColorParameters Shaders/Library/TemporalColorSource.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalColorParameters
    {
        public Vector4 resolution;
        public int valid;
        public int mipCount;
        public int padding0;
        public int padding1;
    }

    // GPU-RECORD: CBV_TemporalColorMipParameters Shaders/Pass/Translucent/Compute_PyramidColor.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalColorMipParameters
    {
        public int sourceValid;
        public float farDepth;
        public int padding0;
        public int padding1;
    }

    public partial class InfinityRenderPipeline
    {
        struct TemporalColorMipData
        {
            internal TemporalColorView source;
            internal RGConstantsHandle parameters;
            internal ComputeKernel kernel;
            internal int mipCount;
            internal int2 size;
        }
        static class TemporalColorBindings
        {
            internal static readonly int Color = Shader.PropertyToID("SRV_TemporalColor"), Depth = Shader.PropertyToID("SRV_TemporalDepth"),
                Mips = Shader.PropertyToID("SRV_TemporalColorMips"),
                CBV_TemporalColorParameters = Shader.PropertyToID("CBV_TemporalColorParameters"),
                CBV_TemporalColorMipParameters = Shader.PropertyToID("CBV_TemporalColorMipParameters"),
                SourceDepth = Shader.PropertyToID("_TemporalSurfaceDepth"),
                SourceMip = Shader.PropertyToID("_TemporalSourceMip"), First = Shader.PropertyToID("_TemporalMipFirst");
        }
        void ComputeTemporalColorPyramid(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.TemporalColorPyramid))
            {
                return;
            }
            var state = context.state;
            bool sr = context.temporalResolve == ETemporalResolve.SuperResolution;
            int2 size = sr ? context.dimensions.displaySize : context.dimensions.internalSize;
            var domain = sr ? ETextureSizingDomain.Display : ETextureSizingDomain.Internal;
            int colorID = sr ? SuperResolutionPassUtilityData.HistoryColorTextureID : AntiAliasingUtilityData.HistoryColorTextureID;
            int depthID = sr ? SuperResolutionPassUtilityData.HistoryDepthTextureID : AntiAliasingUtilityData.HistoryDepthTextureID;
            HistoryRead colorRead = context.historyCache.Read(colorID, TemporalColorDescriptor(size.x, size.y,
                sr ? "HistorySuperResolutionColor" : "HistoryColorTexture", true, domain), context.resetsHistory);
            HistoryRead depthRead = context.historyCache.Read(depthID, TemporalDepthDescriptor(size.x, size.y,
                sr ? "HistorySuperResolutionDepth" : "HistoryDepthTexture", true, domain), context.resetsHistory);
            int2 baseSize = math.max(1, size / 2);
            var descriptor = TemporalColorDescriptor(baseSize.x, baseSize.y, "TemporalColorMips", true, domain);
            descriptor.viewDivisor = 2;
            descriptor.useMipMap = true;
            descriptor.autoGenerateMips = false;
            descriptor.filterMode = FilterMode.Bilinear;
            var mips = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TemporalColorMipBuffer, descriptor);
            var source = new TemporalColorView
            {
                color = m_RGBuilder.ImportTexture(colorRead.texture), depth = m_RGBuilder.ImportTexture(depthRead.texture), mips = mips,
                resolution = new Vector4(size.x, size.y, 1f / size.x, 1f / size.y),
                mipCount = PyramidMipBatch.MipCount(size.x, size.y),
                valid = state.acceptedTemporalColor && state.acceptedTemporalColorIsSR == sr && colorRead.valid && depthRead.valid
            };
            state.temporalColorView = source;
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TemporalColorSourceBuffer, source.color);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TemporalColorDepthBuffer, source.depth);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RenderCaptureService.current?.RecordTemporalColorState(context.camera, source, sr);
#endif
            using (var pass = m_RGBuilder.AddComputePass<TemporalColorMipData>(ProfilingSampler.Get(CustomSamplerId.ComputeTemporalColorPyramid)))
            {
                pass.EnableAsyncCompute(true);
                ref var data = ref pass.GetPassData();
                data = default;
                data.kernel = kernels.temporalColorMip;
                data.source = source;
                data.source.color = pass.ReadTexture(source.color);
                data.source.depth = pass.ReadTexture(source.depth);
                data.parameters = pass.WriteConstants(new TemporalColorMipParameters
                {
                    sourceValid = source.valid ? 1 : 0,
                    farDepth = GraphicsConventions.SampledFarDepth
                });
                data.size = size;
                data.mipCount = PyramidMipBatch.MipCount(baseSize.x, baseSize.y);
                pass.WriteTexture(mips);
                pass.SetExecuteFunc((in TemporalColorMipData data, in RGComputeEncoder commands) =>
                {
                    ComputeKernel computeKernel = data.kernel;
                    var shader = computeKernel.shader;
                    int kernel = computeKernel.index;
                    commands.SetComputeConstantBufferParam(shader, TemporalColorBindings.CBV_TemporalColorMipParameters, data.parameters);
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
                        commands.DispatchCompute(shader,kernel,computeKernel.GroupsX(outputSize.x),computeKernel.GroupsY(outputSize.y),1);
                        inputSize = outputSize;
                    }
                });
            }
            context.features.MarkProduced(EFrameFeature.TemporalColorPyramid);
        }
        static TemporalColorView ReadTemporalColorView<TPassData>(RGComputePassHandle<TPassData> pass, TemporalColorView source) where TPassData : struct
        {
            source.color = pass.ReadTexture(source.color);
            source.depth = pass.ReadTexture(source.depth);
            source.mips = pass.ReadTexture(source.mips);
            source.parameters = pass.WriteConstants(new TemporalColorParameters
            {
                resolution = source.resolution,
                valid = source.valid ? 1 : 0,
                mipCount = source.mipCount
            });
            return source;
        }
        static void BindTemporalColorView(in RGComputeEncoder commands, ComputeShader shader, int kernel, in TemporalColorView source)
        {
            commands.SetComputeConstantBufferParam(shader, TemporalColorBindings.CBV_TemporalColorParameters, source.parameters);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Color,source.color);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Depth,source.depth);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Mips,source.mips);
        }
    }
}
