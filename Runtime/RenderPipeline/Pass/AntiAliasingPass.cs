using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AntiAliasingUtilityData
    {
        internal static string PassName = "AntiAliasingPass";
        internal static string HistoryDepthTextureName = "HistoryDepthTexture";
        internal static string HistoryColorTextureName = "HistoryColorTexture";
        internal static string AccmulateTextureName = "AccmulateTexture";
        internal static int HistoryDepthTextureID = Shader.PropertyToID("HistoryDepthTexture");
        internal static int HistoryColorTextureID = Shader.PropertyToID("HistoryColorTexture");
    }

    internal static class CopyHistoryUtilityData
    {
        internal static string PassName = "CopyHistoryPass";
    }

    public partial class InfinityRenderPipeline
    {
        struct AntiAliasingPassData
        {
            public float4 resolution;
            public ComputeShader taaShader;
            public RDGTextureRef depthTexture;
            public RDGTextureRef motionTexture;
            public RDGTextureRef historyDepthTexture;
            public RDGTextureRef hsitoryColorTexture;
            public RDGTextureRef aliasingColorTexture;
            public RDGTextureRef accmulateColorTexture;
        }

        void ComputeAntiAliasing(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            TextureDescriptor historyDepthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.HistoryDepthTextureName, colorFormat = GraphicsFormat.R16G16_SFloat, depthBufferBits = EDepthBits.None };
            TextureDescriptor historyColorDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.HistoryColorTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = false };
            TextureDescriptor accmulateDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.AccmulateTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = true };

            RDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef motionTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
            RDGTextureRef hsitoryDepthTexture = m_GraphBuilder.ImportTexture(historyCache.GetTexture(AntiAliasingUtilityData.HistoryDepthTextureID, historyDepthDescriptor));
            RDGTextureRef hsitoryColorTexture = m_GraphBuilder.ImportTexture(historyCache.GetTexture(AntiAliasingUtilityData.HistoryColorTextureID, historyColorDescriptor));
            RDGTextureRef aliasingColorTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RDGTextureRef accmulateColorTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, accmulateDescriptor);

            //Add AntiAliasingPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<AntiAliasingPassData>(AntiAliasingUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.ComputeAntiAliasing)))
            {
                //Setup Phase
                ref AntiAliasingPassData passData = ref passRef.GetPassData<AntiAliasingPassData>();
                passData.resolution = new float4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight);
                passData.taaShader = pipelineAsset.taaShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyDepthTexture = passRef.ReadTexture(hsitoryDepthTexture);
                passData.hsitoryColorTexture = passRef.ReadTexture(hsitoryColorTexture);
                passData.aliasingColorTexture = passRef.ReadTexture(aliasingColorTexture);
                passData.accmulateColorTexture = passRef.WriteTexture(accmulateColorTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in AntiAliasingPassData passData, in RDGContext graphContext) =>
                {
                    TemporalAAInputData taaInputData;
                    {
                        taaInputData.resolution = passData.resolution;
                        taaInputData.depthTexture = passData.depthTexture;
                        taaInputData.motionTexture = passData.motionTexture;
                        taaInputData.historyDepthTexture = passData.historyDepthTexture;
                        taaInputData.historyColorTexture = passData.hsitoryColorTexture;
                        taaInputData.aliasingColorTexture = passData.aliasingColorTexture;
                    }
                    TemporalAAOutputData taaOutputData;
                    {
                        taaOutputData.accmulateColorTexture = passData.accmulateColorTexture;
                    }
                    TemporalAAParameter taaParameter = new TemporalAAParameter(0.97f, 0.9f, 6000, 1); // x: static, y: dynamic, z: motion amplification, w: temporalScale

                    TemporalAntiAliasing temporalAA = graphContext.objectPool.Get<TemporalAntiAliasing>();
                    temporalAA.Render(graphContext.cmdBuffer, passData.taaShader, taaParameter, taaInputData, taaOutputData);
                    graphContext.objectPool.Release(temporalAA);
                });
            }

            //Add CopyHistoryPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<AntiAliasingPassData>(CopyHistoryUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.CopyHistoryBuffer)))
            {
                //Setup Phase
                ref AntiAliasingPassData passData = ref passRef.GetPassData<AntiAliasingPassData>();
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.historyDepthTexture = passRef.ReadTexture(hsitoryDepthTexture);
                passData.hsitoryColorTexture = passRef.ReadTexture(hsitoryColorTexture);
                passData.accmulateColorTexture = passRef.ReadTexture(accmulateColorTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in AntiAliasingPassData passData, in RDGContext graphContext) =>
                {
                    TemporalAAInputData taaInputData;
                    {
                        taaInputData.resolution = passData.resolution;
                        taaInputData.depthTexture = passData.depthTexture;
                        taaInputData.motionTexture = passData.motionTexture;
                        taaInputData.historyDepthTexture = passData.historyDepthTexture;
                        taaInputData.historyColorTexture = passData.hsitoryColorTexture;
                        taaInputData.aliasingColorTexture = passData.aliasingColorTexture;
                    }
                    TemporalAAOutputData taaOutputData;
                    {
                        taaOutputData.accmulateColorTexture = passData.accmulateColorTexture;
                    }

                    TemporalAntiAliasing temporalAA = graphContext.objectPool.Get<TemporalAntiAliasing>();
                    temporalAA.CopyToHistory(graphContext.cmdBuffer, taaInputData, taaOutputData);
                    graphContext.objectPool.Release(temporalAA);
                });
            }
        }
    }
}