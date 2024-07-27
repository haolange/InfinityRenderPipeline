using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.Feature;
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
            public ComputeShader taaShader;
            public RGTextureRef depthTexture;
            public RGTextureRef motionTexture;
            public RGTextureRef historyDepthTexture;
            public RGTextureRef hsitoryColorTexture;
            public RGTextureRef aliasingColorTexture;
            public RGTextureRef accmulateColorTexture;
        }

        void ComputeAntiAliasing(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            TextureDescriptor historyDepthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.HistoryDepthTextureName, colorFormat = GraphicsFormat.R16G16_SFloat, depthBufferBits = EDepthBits.None };
            TextureDescriptor historyColorDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.HistoryColorTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = false };
            TextureDescriptor accmulateDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.AccmulateTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = true };

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
            RGTextureRef hsitoryDepthTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(AntiAliasingUtilityData.HistoryDepthTextureID, historyDepthDescriptor));
            RGTextureRef hsitoryColorTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(AntiAliasingUtilityData.HistoryColorTextureID, historyColorDescriptor));
            RGTextureRef aliasingColorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef accmulateColorTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, accmulateDescriptor);

            //Add AntiAliasingPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<AntiAliasingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAntiAliasing)))
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
                passRef.SetExecuteFunc((in AntiAliasingPassData passData, in RGContext graphContext) =>
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
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<AntiAliasingPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryBuffer)))
            {
                //Setup Phase
                ref AntiAliasingPassData passData = ref passRef.GetPassData<AntiAliasingPassData>();
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.historyDepthTexture = passRef.ReadTexture(hsitoryDepthTexture);
                passData.hsitoryColorTexture = passRef.ReadTexture(hsitoryColorTexture);
                passData.accmulateColorTexture = passRef.ReadTexture(accmulateColorTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in AntiAliasingPassData passData, in RGContext graphContext) =>
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