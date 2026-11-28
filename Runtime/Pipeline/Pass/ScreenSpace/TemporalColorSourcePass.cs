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
        internal RGTextureHandle colorHandle, mipsHandle;
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
        public int padding0;
        public int padding1;
        public int padding2;
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
            internal static readonly int Color = Shader.PropertyToID("SRV_TemporalColor"),
                Mips = Shader.PropertyToID("SRV_TemporalColorMips"),
                CBV_TemporalColorParameters = Shader.PropertyToID("CBV_TemporalColorParameters"),
                CBV_TemporalColorMipParameters = Shader.PropertyToID("CBV_TemporalColorMipParameters"),
                SourceMip = Shader.PropertyToID("_TemporalSourceMip"),
                MipInput = Shader.PropertyToID("SRV_TemporalColorMipInput"),
                MipOutput = Shader.PropertyToID("UAV_TemporalColorMipOutput");
        }
        internal void ComputeTemporalColorPyramid(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.TemporalColorPyramid))
            {
                return;
            }
            var state = context.state;
            int2 size = context.dimensions.displaySize;
            var domain = ETextureSizingDomain.Display;
            TextureDescriptor historyDescriptor = TemporalReconstructionUtility.TemporalColorDescriptor(size.x, size.y,
                TemporalReconstructionUtility.HistoryColorTextureName, true, domain);
            HistoryRead colorRead = context.historyCache.Read(TemporalReconstructionUtility.HistoryColorTextureID, historyDescriptor, context.resetsHistory);
            int2 baseSize = math.max(1, size / 2);
            var descriptor = ScreenSpaceRadiance.Configure(
                TemporalReconstructionUtility.TemporalColorDescriptor(baseSize.x, baseSize.y, "TemporalColorMips", true, domain));
            descriptor.viewDivisor = 2;
            descriptor.useMipMap = true;
            descriptor.autoGenerateMips = false;
            descriptor.filterMode = FilterMode.Bilinear;
            var mips = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TemporalColorMipBuffer, descriptor);
            TemporalCameraState temporalState = state.GetTemporalState();
            bool sameDimensions = math.all(temporalState.committedDisplaySize == size);
            var source = new TemporalColorView
            {
                colorHandle = m_RGBuilder.ImportTexture(colorRead.textureHandle), mipsHandle = mips,
                resolution = new Vector4(size.x, size.y, 1f / size.x, 1f / size.y),
                mipCount = PyramidMipBatch.MipCount(size.x, size.y),
                valid = temporalState.acceptedTemporalColor && sameDimensions && colorRead.valid
            };
            temporalState.temporalColorView = source;
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.TemporalColorSourceBuffer, source.colorHandle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RenderCaptureService.current?.RecordTemporalColorState(context.camera, source, context.temporalRequirements.isUpscaling);
#endif
            {
                RGComputePassHandle<TemporalColorMipData> passHandle = m_RGBuilder.AddComputePass<TemporalColorMipData>(ProfilingSampler.Get(CustomSamplerId.ComputeTemporalColorPyramid));
                passHandle.EnableAsyncCompute(true);
                ref TemporalColorMipData passData = ref passHandle.GetPassData();
                passData = default;
                passData.kernel = m_Resources.kernels.temporalColorMip;
                passData.source = source;
                passData.source.colorHandle = passHandle.ReadTexture(source.colorHandle);
                passData.parametersHandle = passHandle.WriteConstants(new TemporalColorMipParameters
                {
                    sourceValid = source.valid ? 1 : 0
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
                    commands.SetComputeTextureParam(shader, kernel, ColorPyramidPassUtilityData.SRV_ColorTextureID, passData.source.colorHandle);
                    commands.SetComputeTextureParam(shader, kernel, TemporalColorBindings.MipInput, passData.source.mipsHandle);
                    int2 inputSize = passData.size;
                    for (int mip = 0; mip < passData.mipCount; ++mip)
                    {
                        int2 outputSize = math.max(1, passData.size / (1 << (mip + 1)));
                        commands.SetComputeIntParam(shader, TemporalColorBindings.SourceMip, mip - 1);
                        commands.SetComputeVectorParam(shader, ColorPyramidPassUtilityData.ColorPyramid_SizeID, new Vector4(inputSize.x,inputSize.y,0,0));
                        commands.SetComputeVectorParam(shader, ColorPyramidPassUtilityData.ColorPyramid_DstSizeID, new Vector4(outputSize.x,outputSize.y,0,0));
                        commands.SetComputeTextureParam(shader, kernel, TemporalColorBindings.MipOutput, passData.source.mipsHandle, mip);
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
            commands.SetComputeTextureParam(shader,kernel,TemporalColorBindings.Mips,source.mipsHandle);
        }
    }
}
