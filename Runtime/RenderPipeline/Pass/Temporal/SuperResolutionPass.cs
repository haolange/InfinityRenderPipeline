using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SuperResolutionPassUtilityData
    {
        internal static string TextureName = "SuperResolutionTexture";
        internal static string HistoryColorTextureName = "HistorySuperResolutionColor";
        internal static int HistoryColorTextureID = Shader.PropertyToID("HistorySuperResolutionColor");
        internal static readonly int HistoryDepthTextureID = Shader.PropertyToID("HistorySuperResolutionDepth");
        internal static readonly int UAV_HistoryColorTextureID = Shader.PropertyToID("UAV_SRHistoryColorTexture");
        internal static readonly int UAV_HistoryDepthTextureID = Shader.PropertyToID("UAV_SRHistoryDepthTexture");
        internal static int SR_ResolutionID = Shader.PropertyToID("SR_Resolution");
        internal static int SR_JitterID = Shader.PropertyToID("SR_Jitter");
        internal static int SR_FrameIndexID = Shader.PropertyToID("SR_FrameIndex");
        internal static int SR_SharpnessID = Shader.PropertyToID("SR_Sharpness");
        internal static int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static int SRV_HistoryColorTextureID = Shader.PropertyToID("SRV_HistoryColorTexture");
        internal static int UAV_SuperResolutionTextureID = Shader.PropertyToID("UAV_SuperResolutionTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct SuperResolutionPassData
        {
            public int2 resolution, inputResolution;
            public bool resetHistory;
            public float2 jitter;
            public int frameIndex, kernel;
            public float sharpness;
            public ComputeShader superResolutionShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef motionTexture;
            public RGTextureRef historyColorTexture;
            public RGTextureRef superResolutionTexture, pendingColor, pendingDepth;
        }

        void ComputeSuperResolution(RenderContext renderContext, Camera camera, HistoryCache historyCache, in float2 jitter)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.SuperResolution);

            if (shaders.superResolutionShader == null)
            {
                throw new InvalidOperationException("InfinityRP: enableSuperResolution is true but superResolutionShader is not assigned.");
            }

            int width = m_ActiveFrameState.dimensions.displaySize.x;
            int height = m_ActiveFrameState.dimensions.displaySize.y;

            TextureDescriptor historyColorDescriptor = TemporalColorDescriptor(width, height,
                SuperResolutionPassUtilityData.HistoryColorTextureName, true, ETextureSizingDomain.Display);
            RGTextureRef historyColorTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor, out bool historyCreated));
            m_RGScoper.RegisterTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorTexture);
            var pendingColor = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor));
            var depthHistoryDescriptor = TemporalDepthDescriptor(width, height,
                "HistorySuperResolutionDepth", true, ETextureSizingDomain.Display);
            var pendingDepth = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(SuperResolutionPassUtilityData.HistoryDepthTextureID, depthHistoryDescriptor));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.SRAccumulationBuffer, pendingColor);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.SRDepthBuffer, pendingDepth);
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
            RGTextureRef superResolutionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SuperResolutionBuffer, superResDsc);

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(TranslucentFeatureUtility.ResolveTemporalSceneColorId());
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SuperResolutionPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeSuperResolution)))
            {
                ref SuperResolutionPassData passData = ref passRef.GetPassData<SuperResolutionPassData>();
                passData.resolution = new int2(width, height);
                passData.inputResolution = m_ActiveFrameState.dimensions.internalSize;
                passData.resetHistory = m_CameraUniform.historyReset || historyCreated
                    || historyCache.TextureGeneration(SuperResolutionPassUtilityData.HistoryColorTextureID) == 0;
                passData.jitter = jitter;
                passData.frameIndex = Time.frameCount;
                passData.kernel = shaders.superResolutionShader.FindKernel("SuperResolutionCS");
                passData.sharpness = 0.5f;
                passData.superResolutionShader = shaders.superResolutionShader;
                passData.sceneColorTexture = passRef.ReadTexture(lightingTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyColorTexture = passRef.ReadTexture(historyColorTexture);
                passData.superResolutionTexture = passRef.WriteTexture(superResolutionTexture);
                passData.pendingColor = passRef.WriteTexture(pendingColor);
                passData.pendingDepth = passRef.WriteTexture(pendingDepth);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SuperResolutionPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, Shader.PropertyToID("SR_InputResolution"), new Vector4(passData.inputResolution.x, passData.inputResolution.y, 1f / passData.inputResolution.x, 1f / passData.inputResolution.y));
                    cmdEncoder.SetComputeIntParam(passData.superResolutionShader, Shader.PropertyToID("SR_ResetHistory"), passData.resetHistory ? 1 : 0);
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_JitterID, new Vector4(passData.jitter.x, passData.jitter.y, 0, 0));
                    cmdEncoder.SetComputeIntParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeFloatParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_SharpnessID, passData.sharpness);

                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.SRV_HistoryColorTextureID, passData.historyColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_SuperResolutionTextureID, passData.superResolutionTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_HistoryColorTextureID, passData.pendingColor);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, passData.kernel, SuperResolutionPassUtilityData.UAV_HistoryDepthTextureID, passData.pendingDepth);
                    cmdEncoder.DispatchCompute(passData.superResolutionShader, passData.kernel, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.SuperResolution);
        }

    }
}
