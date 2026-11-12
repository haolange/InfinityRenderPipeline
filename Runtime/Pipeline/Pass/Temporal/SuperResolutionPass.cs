using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SuperResolutionPassUtilityData
    {
        internal static readonly int SR_InputResolutionID = Shader.PropertyToID("SR_InputResolution");
        internal static readonly int SR_ResetHistoryID = Shader.PropertyToID("SR_ResetHistory");
        internal static readonly string TextureName = "SuperResolutionTexture";
        internal static readonly string HistoryColorTextureName = "HistorySuperResolutionColor";
        internal static readonly int HistoryColorTextureID = Shader.PropertyToID("HistorySuperResolutionColor");
        internal static readonly int HistoryDepthTextureID = Shader.PropertyToID("HistorySuperResolutionDepth");
        internal static readonly int UAV_HistoryColorTextureID = Shader.PropertyToID("UAV_SRHistoryColorTexture");
        internal static readonly int UAV_HistoryDepthTextureID = Shader.PropertyToID("UAV_SRHistoryDepthTexture");
        internal static readonly int SR_ResolutionID = Shader.PropertyToID("SR_Resolution");
        internal static readonly int SR_JitterID = Shader.PropertyToID("SR_Jitter");
        internal static readonly int SR_FrameIndexID = Shader.PropertyToID("SR_FrameIndex");
        internal static readonly int SR_SharpnessID = Shader.PropertyToID("SR_Sharpness");
        internal static readonly int UAV_SuperResolutionTextureID = Shader.PropertyToID("UAV_SuperResolutionTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct SuperResolutionPassData
        {
            public int2 resolution, inputResolution;
            public bool resetHistory;
            public float2 jitter;
            public int frameIndex, kernel;
            public int2 groups;
            public float sharpness;
            public ComputeShader superResolutionShader;
            public RGTextureHandle sceneColorTexture;
            public RGTextureHandle depthTexture;
            public RGTextureHandle motionTexture;
            public RGTextureHandle historyColorTexture;
            public RGTextureHandle superResolutionTexture, pendingColor, pendingDepth;
        }

        void ComputeSuperResolution(RenderContext renderContext, Camera camera, HistoryCache historyCache, in float2 jitter)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.SuperResolution);

            int width = m_ActiveFrameState.dimensions.displaySize.x;
            int height = m_ActiveFrameState.dimensions.displaySize.y;

            TextureDescriptor historyColorDescriptor = TemporalColorDescriptor(width, height,
                SuperResolutionPassUtilityData.HistoryColorTextureName, true, ETextureSizingDomain.Display);
            RGTextureHandle historyColorTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor, out bool historyCreated));
            m_RGBuilder.scoper.RegisterTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorTexture);
            var pendingColor = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor));
            var depthHistoryDescriptor = TemporalDepthDescriptor(width, height,
                "HistorySuperResolutionDepth", true, ETextureSizingDomain.Display);
            var pendingDepth = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(SuperResolutionPassUtilityData.HistoryDepthTextureID, depthHistoryDescriptor));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SRAccumulationBuffer, pendingColor);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SRDepthBuffer, pendingDepth);
            historyCache.MarkProduced(SuperResolutionPassUtilityData.HistoryColorTextureID);
            historyCache.MarkProduced(SuperResolutionPassUtilityData.HistoryDepthTextureID);

            TextureDescriptor superResDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Display);
            {
                superResDsc.name = SuperResolutionPassUtilityData.TextureName;
                superResDsc.dimension = TextureDimension.Tex2D;
                superResDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                superResDsc.depthBufferBits = EDepthBits.None;
                superResDsc.enableRandomWrite = true;
            }
            RGTextureHandle superResolutionTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SuperResolutionBuffer, superResDsc);
            m_RGBuilder.scoper.PublishTextureRole(InfinityShaderIDs.ResolvedSceneColor, superResolutionTexture);

            RGTextureHandle lightingTexture = m_RGBuilder.scoper.QueryTexture(TranslucentFeatureUtility.ResolveTemporalSceneColorId());
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle motionTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            using (var passRef = m_RGBuilder.AddComputePass<SuperResolutionPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeSuperResolution)))
            {
                ref SuperResolutionPassData passData = ref passRef.GetPassData();
                passData.resolution = new int2(width, height);
                passData.inputResolution = m_ActiveFrameState.dimensions.internalSize;
                passData.resetHistory = m_CameraUniform.historyReset || historyCreated
                    || historyCache.TextureGeneration(SuperResolutionPassUtilityData.HistoryColorTextureID) == 0;
                passData.jitter = jitter;
                passData.frameIndex = Time.frameCount;
                passData.kernel = kernels.superResolution.index;
                passData.groups = new int2(kernels.superResolution.GroupsX(width), kernels.superResolution.GroupsY(height));
                passData.sharpness = 0.5f;
                passData.superResolutionShader = kernels.superResolution.shader;
                passData.sceneColorTexture = passRef.ReadTexture(lightingTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyColorTexture = passRef.ReadTexture(historyColorTexture);
                passData.superResolutionTexture = passRef.WriteTexture(superResolutionTexture);
                passData.pendingColor = passRef.WriteTexture(pendingColor);
                passData.pendingDepth = passRef.WriteTexture(pendingDepth);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SuperResolutionPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_InputResolutionID, new Vector4(passData.inputResolution.x, passData.inputResolution.y, 1f / passData.inputResolution.x, 1f / passData.inputResolution.y));
                    cmdEncoder.SetComputeIntParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_ResetHistoryID, passData.resetHistory ? 1 : 0);
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_JitterID, new Vector4(passData.jitter.x, passData.jitter.y, 0, 0));
                    cmdEncoder.SetComputeIntParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeFloatParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_SharpnessID, passData.sharpness);

                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, PassShaderIDs.SRV_HistoryColorTexture, passData.historyColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_SuperResolutionTextureID, passData.superResolutionTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_HistoryColorTextureID, passData.pendingColor);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_HistoryDepthTextureID, passData.pendingDepth);
                    cmdEncoder.DispatchCompute(passData.superResolutionShader, passData.kernel, passData.groups.x, passData.groups.y, 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.SuperResolution);
        }

    }
}
