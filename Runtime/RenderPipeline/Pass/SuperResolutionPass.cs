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
            public int2 resolution;
            public float2 jitter;
            public int frameIndex;
            public float sharpness;
            public ComputeShader superResolutionShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef motionTexture;
            public RGTextureRef historyColorTexture;
            public RGTextureRef superResolutionTexture;
        }

        void ComputeSuperResolution(RenderContext renderContext, Camera camera, HistoryCache historyCache, in float2 jitter)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.SuperResolution);

            if (pipelineAsset.superResolutionShader == null)
            {
                throw new InvalidOperationException("InfinityRP: enableSuperResolution is true but superResolutionShader is not assigned.");
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor historyColorDescriptor = new TextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex2D,
                name = SuperResolutionPassUtilityData.HistoryColorTextureName,
                colorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = false
            };
            RGTextureRef historyColorTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor));
            m_RGScoper.RegisterTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorTexture);

            TextureDescriptor superResDsc = new TextureDescriptor(width, height);
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
                passData.jitter = jitter;
                passData.frameIndex = Time.frameCount;
                passData.sharpness = 0.5f;
                passData.superResolutionShader = pipelineAsset.superResolutionShader;
                passData.sceneColorTexture = passRef.ReadTexture(lightingTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyColorTexture = passRef.ReadTexture(historyColorTexture);
                passData.superResolutionTexture = passRef.WriteTexture(superResolutionTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SuperResolutionPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeVectorParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_JitterID, new Vector4(passData.jitter.x, passData.jitter.y, 0, 0));
                    cmdEncoder.SetComputeIntParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeFloatParam(passData.superResolutionShader, SuperResolutionPassUtilityData.SR_SharpnessID, passData.sharpness);

                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, 0, SuperResolutionPassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, 0, SuperResolutionPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, 0, SuperResolutionPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, 0, SuperResolutionPassUtilityData.SRV_HistoryColorTextureID, passData.historyColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.superResolutionShader, 0, SuperResolutionPassUtilityData.UAV_SuperResolutionTextureID, passData.superResolutionTexture);
                    cmdEncoder.DispatchCompute(passData.superResolutionShader, 0, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.SuperResolution);
        }

        struct CopyHistorySuperResolutionPassData
        {
            public RGTextureRef historyColorTexture;
            public RGTextureRef superResolutionTexture;
        }

        void CopyHistorySuperResolution(RenderContext renderContext, HistoryCache historyCache, Camera camera)
        {
            TextureDescriptor historyColorDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight)
            {
                dimension = TextureDimension.Tex2D,
                name = SuperResolutionPassUtilityData.HistoryColorTextureName,
                colorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = false
            };
            RGTextureRef historyColorTexture = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(SuperResolutionPassUtilityData.HistoryColorTextureID, historyColorDescriptor));
            RGTextureRef superResolutionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.SuperResolutionBuffer);
            historyCache.MarkProduced(SuperResolutionPassUtilityData.HistoryColorTextureID);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistorySuperResolutionPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistorySuperResolution)))
            {
                passRef.ReadTexture(superResolutionTexture);
                passRef.WriteTexture(historyColorTexture);

                ref CopyHistorySuperResolutionPassData passData = ref passRef.GetPassData<CopyHistorySuperResolutionPassData>();
                passData.superResolutionTexture = superResolutionTexture;
                passData.historyColorTexture = historyColorTexture;

                passRef.SetExecuteFunc((in CopyHistorySuperResolutionPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.superResolutionTexture, passData.historyColorTexture);
                });
            }
        }
    }
}
