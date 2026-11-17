using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_SuperResolutionParameters Shaders/Pass/Temporal/Compute_SuperResolution.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct SuperResolutionParameters
    {
        public Vector4 resolution;
        public Vector4 inputResolution;
        public int resetHistory;
        public float sharpness;
        public int padding0;
        public int padding1;
    }

    internal static class SuperResolutionPassUtilityData
    {
        internal static readonly int CBV_SuperResolutionParametersID = Shader.PropertyToID("CBV_SuperResolutionParameters");
        internal static readonly string TextureName = "SuperResolutionTexture";
        internal static readonly string HistoryColorTextureName = "HistorySuperResolutionColor";
        internal static readonly int HistoryColorTextureID = Shader.PropertyToID("HistorySuperResolutionColor");
        internal static readonly int HistoryDepthTextureID = Shader.PropertyToID("HistorySuperResolutionDepth");
        internal static readonly int UAV_HistoryColorTextureID = Shader.PropertyToID("UAV_SRHistoryColorTexture");
        internal static readonly int UAV_HistoryDepthTextureID = Shader.PropertyToID("UAV_SRHistoryDepthTexture");
        internal static readonly int UAV_SuperResolutionTextureID = Shader.PropertyToID("UAV_SuperResolutionTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct SuperResolutionPassData
        {
            public RGConstantsHandle parametersHandle;
            public int kernel;
            public int2 groups;
            public ComputeShader superResolutionShader;
            public RGTextureHandle sceneColorTextureHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle motionTextureHandle;
            public RGTextureHandle historyColorTextureHandle;
            public RGTextureHandle superResolutionTextureHandle, pendingColorHandle, pendingDepthHandle;
        }

        void ComputeSuperResolution(in ViewContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.SuperResolution);

            int width = context.dimensions.displaySize.x;
            int height = context.dimensions.displaySize.y;

            TextureDescriptor historyColorDescriptor = TemporalColorDescriptor(width, height,
                SuperResolutionPassUtilityData.HistoryColorTextureName, true, ETextureSizingDomain.Display);
            HistoryRead colorRead = context.historyCache.Read(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor, context.resetsHistory);
            RGTextureHandle historyColorTextureHandle = m_RGBuilder.ImportTexture(colorRead.textureHandle);
            m_RGBuilder.scoper.RegisterTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorTextureHandle);
            var pendingColor = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor));
            var depthHistoryDescriptor = TemporalDepthDescriptor(width, height,
                "HistorySuperResolutionDepth", true, ETextureSizingDomain.Display);
            var pendingDepth = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(SuperResolutionPassUtilityData.HistoryDepthTextureID, depthHistoryDescriptor));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SRAccumulationBuffer, pendingColor);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SRDepthBuffer, pendingDepth);
            context.historyCache.MarkProduced(SuperResolutionPassUtilityData.HistoryColorTextureID);
            context.historyCache.MarkProduced(SuperResolutionPassUtilityData.HistoryDepthTextureID);

            TextureDescriptor superResDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Display);
            {
                superResDsc.name = SuperResolutionPassUtilityData.TextureName;
                superResDsc.dimension = TextureDimension.Tex2D;
                superResDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                superResDsc.enableRandomWrite = true;
            }
            RGTextureHandle superResolutionTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SuperResolutionBuffer, superResDsc);

            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.fogged);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle motionTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            {
                RGComputePassHandle<SuperResolutionPassData> passHandle = m_RGBuilder.AddComputePass<SuperResolutionPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeSuperResolution));
                ref SuperResolutionPassData passData = ref passHandle.GetPassData();
                int2 inputSize = context.dimensions.internalSize;
                passData.parametersHandle = passHandle.WriteConstants(new SuperResolutionParameters
                {
                    resolution = new Vector4(width, height, 1.0f / width, 1.0f / height),
                    inputResolution = new Vector4(inputSize.x, inputSize.y, 1f / inputSize.x, 1f / inputSize.y),
                    resetHistory = colorRead.valid ? 0 : 1,
                    sharpness = 0.5f,
                });
                passData.kernel = kernels.superResolution.index;
                passData.groups = new int2(kernels.superResolution.GroupsX(width), kernels.superResolution.GroupsY(height));
                passData.superResolutionShader = kernels.superResolution.shader;
                passData.sceneColorTextureHandle = passHandle.ReadTexture(lightingTextureHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                passData.motionTextureHandle = passHandle.ReadTexture(motionTextureHandle);
                passData.historyColorTextureHandle = passHandle.ReadTexture(historyColorTextureHandle);
                passData.superResolutionTextureHandle = passHandle.WriteTexture(superResolutionTextureHandle);
                passData.pendingColorHandle = passHandle.WriteTexture(pendingColor);
                passData.pendingDepthHandle = passHandle.WriteTexture(pendingDepth);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in SuperResolutionPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(passData.superResolutionShader, SuperResolutionPassUtilityData.CBV_SuperResolutionParametersID, passData.parametersHandle);

                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_HistoryColorTexture, passData.historyColorTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_SuperResolutionTextureID, passData.superResolutionTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_HistoryColorTextureID, passData.pendingColorHandle);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_HistoryDepthTextureID, passData.pendingDepthHandle);
                    cmdEncoder.DispatchCompute(passData.superResolutionShader, passData.kernel, passData.groups.x, passData.groups.y, 1);
                });
            }

            context.features.MarkProduced(EFrameFeature.SuperResolution);
        }

    }
}
