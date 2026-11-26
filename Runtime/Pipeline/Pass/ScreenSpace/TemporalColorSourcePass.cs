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
        internal RGTextureHandle colorHandle, depthHandle, mipsHandle;
        // Written by the reading pass for its own execution; bound by BindTemporalColorView.
        internal RGConstantsHandle parametersHandle;
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

    internal sealed class TemporalColorSourcePass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;

        internal TemporalColorSourcePass(RGBuilder graph, InfinityRenderPipelineResources resources)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
        }

        struct TemporalColorMipData
        {
            internal TemporalColorView source;
            internal RGConstantsHandle parametersHandle;
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
        internal void ComputeTemporalColorPyramid(in ViewContext context)
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
            HistoryRead colorRead = context.historyCache.Read(colorID, AntiAliasingPass.TemporalColorDescriptor(size.x, size.y,
                sr ? "HistorySuperResolutionColor" : "HistoryColorTexture", true, domain), context.resetsHistory);
            HistoryRead depthRead = context.historyCache.Read(depthID, AntiAliasingPass.TemporalDepthDescriptor(size.x, size.y,
                sr ? "HistorySuperResolutionDepth" : "HistoryDepthTexture", true, domain), context.resetsHistory);
            int2 baseSize = math.max(1, size / 2);
            var descriptor = AntiAliasingPass.TemporalColorDescriptor(baseSize.x, baseSize.y, "TemporalColorMips", true, domain);
            descriptor.viewDivisor = 2;
            descriptor.useMipMap = true;
            descriptor.autoGenerateMips = false;
            descriptor.filterMode = FilterMode.Bilinear;
            var mips = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TemporalColorMipBuffer, descriptor);
            TemporalCameraState temporalState = state.GetTemporalState();
            var source = new TemporalColorView
            {
                colorHandle = m_RGBuilder.ImportTexture(colorRead.textureHandle), depthHandle = m_RGBuilder.ImportTexture(depthRead.textureHandle), mipsHandle = mips,
                resolution = new Vector4(size.x, size.y, 1f / size.x, 1f / size.y),
                mipCount = PyramidMipBatch.MipCount(size.x, size.y),
                valid = temporalState.acceptedTemporalColor && temporalState.acceptedTemporalColorIsUpscaled == sr && colorRead.valid && depthRead.valid
            };
            temporalState.temporalColorView = source;
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TemporalColorSourceBuffer, source.colorHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TemporalColorDepthBuffer, source.depthHandle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RenderCaptureService.current?.RecordTemporalColorState(context.camera, source, sr);
#endif
            {
                RGComputePassHandle<TemporalColorMipData> passHandle = m_RGBuilder.AddComputePass<TemporalColorMipData>(ProfilingSampler.Get(CustomSamplerId.ComputeTemporalColorPyramid));
                passHandle.EnableAsyncCompute(true);
                ref TemporalColorMipData passData = ref passHandle.GetPassData();
                passData = default;
                passData.kernel = m_Resources.kernels.temporalColorMip;
                passData.source = source;
                passData.source.colorHandle = passHandle.ReadTexture(source.colorHandle);
                passData.source.depthHandle = passHandle.ReadTexture(source.depthHandle);
                passData.parametersHandle = passHandle.WriteConstants(new TemporalColorMipParameters
                {
                    sourceValid = source.valid ? 1 : 0,
                    farDepth = GraphicsConventions.SampledFarDepth
                });
                passData.size = size;
                passData.mipCount = PyramidMipBatch.MipCount(baseSize.x, baseSize.y);
                passHandle.WriteTexture(mips);
                passHandle.SetExecuteFunc((in TemporalColorMipData passData, in RGComputeEncoder commands) =>
                {
                    ComputeKernel computeKernel = passData.kernel;
                    var shader = computeKernel.shader;
                    int kernel = computeKernel.index;
                    commands.SetComputeConstantBufferParam(shader, TemporalColorBindings.CBV_TemporalColorMipParameters, passData.parametersHandle);
                    commands.SetComputeTextureParam(shader, kernel, TemporalColorBindings.SourceDepth, passData.source.depthHandle);
                    int2 inputSize = passData.size;
                    for (int mip = 0; mip < passData.mipCount; ++mip)
                    {
                        int2 outputSize = math.max(1, passData.size / (1 << (mip + 1)));
                        commands.SetComputeIntParam(shader, TemporalColorBindings.First, mip == 0 ? 1 : 0);
                        commands.SetComputeIntParam(shader, TemporalColorBindings.SourceMip, math.max(0,mip-1));
                        commands.SetComputeVectorParam(shader, ColorPyramidPassUtilityData.ColorPyramid_SizeID, new Vector4(inputSize.x,inputSize.y,0,0));
                        commands.SetComputeVectorParam(shader, ColorPyramidPassUtilityData.ColorPyramid_DstSizeID, new Vector4(outputSize.x,outputSize.y,0,0));
                        commands.SetComputeTextureParam(shader, kernel, ColorPyramidPassUtilityData.SRV_ColorTextureID,
                            mip == 0 ? passData.source.colorHandle : passData.source.mipsHandle);
                        commands.SetComputeTextureParam(shader, kernel, ColorPyramidPassUtilityData.UAV_ColorPyramidID, passData.source.mipsHandle, mip);
                        commands.DispatchCompute(shader,kernel,computeKernel.GroupsX(outputSize.x),computeKernel.GroupsY(outputSize.y),1);
                        inputSize = outputSize;
                    }
                });
            }
            context.features.MarkProduced(EFrameFeature.TemporalColorPyramid);
        }
        internal static TemporalColorView ReadTemporalColorView<TPassData>(RGComputePassHandle<TPassData> passHandle, TemporalColorView source) where TPassData : struct
        {
            source.colorHandle = passHandle.ReadTexture(source.colorHandle);
            source.depthHandle = passHandle.ReadTexture(source.depthHandle);
            source.mipsHandle = passHandle.ReadTexture(source.mipsHandle);
            source.parametersHandle = passHandle.WriteConstants(new TemporalColorParameters
            {
                resolution = source.resolution,
                valid = source.valid ? 1 : 0,
                mipCount = source.mipCount
            });
            return source;
        }
        internal static void BindTemporalColorView(in RGComputeEncoder commands, ComputeShader shader, int kernel, in TemporalColorView source)
        {
            commands.SetComputeConstantBufferParam(shader, TemporalColorBindings.CBV_TemporalColorParameters, source.parametersHandle);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Color,source.colorHandle);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Depth,source.depthHandle);
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Mips,source.mipsHandle);
        }
    }
}
