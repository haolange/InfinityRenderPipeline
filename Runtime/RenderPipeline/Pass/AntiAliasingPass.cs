using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AntiAliasingUtilityData
    {
        internal static string HistoryDepthTextureName = "HistoryDepthTexture";
        internal static string HistoryColorTextureName = "HistoryColorTexture";
        internal static string AccmulateTextureName = "AccmulateTexture";
        internal static int HistoryDepthTextureID = Shader.PropertyToID("HistoryDepthTexture");
        internal static int HistoryColorTextureID = Shader.PropertyToID("HistoryColorTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct AntiAliasingPassData
        {
            public float4 resolution;
            public bool resetHistory;
            public ComputeShader taaShader;
            public RGTextureRef depthTexture;
            public RGTextureRef motionTexture;
            public RGTextureRef historyDepthTexture;
            public RGTextureRef historyColorTexture;
            public RGTextureRef aliasingColorTexture;
            public RGTextureRef accmulateColorTexture;
        }

        void ComputeAntiAliasing(RenderContext renderContext, Camera camera, HistoryCache historyCache, CameraUniform cameraUniform)
        {
            if (pipelineAsset.taaShader == null)
            {
                throw new InvalidOperationException("InfinityRP: TAA path is active but taaShader is not assigned.");
            }

            TextureDescriptor historyDepthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.HistoryDepthTextureName, depthBufferBits = EDepthBits.Depth32, enableRandomWrite = false };
            TextureDescriptor historyColorDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.HistoryColorTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = false };
            TextureDescriptor accmulateDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.AccmulateTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = true };

            RGTextureRef historyDepthTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(AntiAliasingUtilityData.HistoryDepthTextureID, historyDepthDescriptor, out bool historyDepthCreated));
            RGTextureRef historyColorTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(AntiAliasingUtilityData.HistoryColorTextureID, historyColorDescriptor, out bool historyColorCreated));

            m_RGScoper.RegisterTexture(AntiAliasingUtilityData.HistoryDepthTextureID, historyDepthTexture);
            m_RGScoper.RegisterTexture(AntiAliasingUtilityData.HistoryColorTextureID, historyColorTexture);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
            RGTextureRef aliasingColorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef accmulateColorTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, accmulateDescriptor);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<AntiAliasingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAntiAliasing)))
            {
                ref AntiAliasingPassData passData = ref passRef.GetPassData<AntiAliasingPassData>();
                passData.resolution = new float4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight);
                passData.resetHistory = cameraUniform.historyReset || historyColorCreated || historyDepthCreated;
                passData.taaShader = pipelineAsset.taaShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyDepthTexture = passRef.ReadTexture(historyDepthTexture);
                passData.historyColorTexture = passRef.ReadTexture(historyColorTexture);
                passData.aliasingColorTexture = passRef.ReadTexture(aliasingColorTexture);
                passData.accmulateColorTexture = passRef.WriteTexture(accmulateColorTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in AntiAliasingPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    TemporalAAInputData taaInputData;
                    {
                        taaInputData.resolution = passData.resolution;
                        taaInputData.depthTexture = passData.depthTexture;
                        taaInputData.motionTexture = passData.motionTexture;
                        taaInputData.historyDepthTexture = passData.historyDepthTexture;
                        taaInputData.historyColorTexture = passData.historyColorTexture;
                        taaInputData.aliasingColorTexture = passData.aliasingColorTexture;
                    }
                    TemporalAAOutputData taaOutputData;
                    {
                        taaOutputData.accmulateColorTexture = passData.accmulateColorTexture;
                    }
                    TemporalAAParameter taaParameter = passData.resetHistory
                        ? new TemporalAAParameter(0.0f, 0.0f, 200, 1.25f, 0.35f)
                        : new TemporalAAParameter(0.97f, 0.95f, 200, 1.25f, 0.35f);

                    TemporalAntiAliasingGenerator temporalAAGenerator = objectPool.Get<TemporalAntiAliasingGenerator>();
                    temporalAAGenerator.Dispatch(cmdEncoder, passData.taaShader, taaParameter, taaInputData, taaOutputData);
                    objectPool.Release(temporalAAGenerator);
                });
            }
        }

        struct CopyHistoryAntiAliasingPassData
        {
            public RGTextureRef historyColorTexture;
            public RGTextureRef accmulateColorTexture;
        }

        void CopyHistoryAntiAliasing(RenderContext renderContext)
        {
            RGTextureRef historyColorTexture = m_RGScoper.QueryTexture(AntiAliasingUtilityData.HistoryColorTextureID);
            RGTextureRef accmulateColorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryAntiAliasingPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryAntiAliasing)))
            {
                passRef.ReadTexture(accmulateColorTexture);
                passRef.WriteTexture(historyColorTexture);

                ref CopyHistoryAntiAliasingPassData passData = ref passRef.GetPassData<CopyHistoryAntiAliasingPassData>();
                passData.accmulateColorTexture = accmulateColorTexture;
                passData.historyColorTexture = historyColorTexture;

                passRef.SetExecuteFunc((in CopyHistoryAntiAliasingPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.accmulateColorTexture, passData.historyColorTexture);
                });
            }
        }

        struct CopyHistoryDepthPassData
        {
            public RGTextureRef depthTexture;
            public RGTextureRef historyDepthTexture;
        }

        void CopyHistoryDepth(RenderContext renderContext)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef historyDepthTexture = m_RGScoper.QueryTexture(AntiAliasingUtilityData.HistoryDepthTextureID);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryDepthPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryDepth)))
            {
                passRef.ReadTexture(depthTexture);
                passRef.WriteTexture(historyDepthTexture);

                ref CopyHistoryDepthPassData passData = ref passRef.GetPassData<CopyHistoryDepthPassData>();
                passData.depthTexture = depthTexture;
                passData.historyDepthTexture = historyDepthTexture;

                passRef.SetExecuteFunc((in CopyHistoryDepthPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.depthTexture, passData.historyDepthTexture);
                });
            }
        }
    }
}
