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
        internal static string HistoryTextureName = "HistoryTexture";
        internal static string AccmulateTextureName = "AccmulateTexture";
        internal static int HistoryTextureID = Shader.PropertyToID("HistoryTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct AntiAliasingPassData
        {
            public Camera camera;
            public ComputeShader taaShader;
            public RDGTextureRef depthTexture;
            public RDGTextureRef motionTexture;
            public RDGTextureRef hsitoryTexture;
            public RDGTextureRef aliasingTexture;
            public RDGTextureRef accmulateTexture;
        }

        void RenderAntiAliasing(Camera camera, HistoryCache historyCache)
        {
            TextureDescriptor historyDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.HistoryTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = false };
            TextureDescriptor accmulateDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = AntiAliasingUtilityData.AccmulateTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = true };

            RDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef motionTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
            RDGTextureRef aliasingTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RDGTextureRef accmulateTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, accmulateDescriptor);
            RDGTextureRef hsitoryTexture = m_GraphBuilder.ImportTexture(historyCache.GetTexture(AntiAliasingUtilityData.HistoryTextureID, historyDescriptor));

            //Add AntiAliasingPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<AntiAliasingPassData>(AntiAliasingUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderAntiAliasing)))
            {
                //Setup Phase
                ref AntiAliasingPassData passData = ref passRef.GetPassData<AntiAliasingPassData>();
                passData.camera = camera;
                passData.taaShader = pipelineAsset.taaShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.hsitoryTexture = passRef.ReadTexture(hsitoryTexture);
                passData.aliasingTexture = passRef.ReadTexture(aliasingTexture);
                passData.accmulateTexture = passRef.WriteTexture(accmulateTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in AntiAliasingPassData passData, in RDGContext graphContext) =>
                {
                    TemporalAAInputData taaInputData;
                    {
                        taaInputData.resolution = new float4(passData.camera.pixelWidth, passData.camera.pixelHeight, 1.0f / passData.camera.pixelWidth, 1.0f / passData.camera.pixelHeight);
                        taaInputData.depthTexture = passData.depthTexture;
                        taaInputData.motionTexture = passData.motionTexture;
                        taaInputData.hsitoryTexture = passData.hsitoryTexture;
                        taaInputData.aliasingTexture = passData.aliasingTexture;
                    }
                    TemporalAAOutputData taaOutputData;
                    {
                        taaOutputData.accmulateTexture = passData.accmulateTexture;
                    }
                    TemporalAAParameter taaParameter = new TemporalAAParameter(0.95f, 0.85f, 6500, 0.125f);

                    TemporalAntiAliasing temporalAA = graphContext.objectPool.Get<TemporalAntiAliasing>();
                    temporalAA.Render(graphContext.cmdBuffer, passData.taaShader, taaParameter, taaInputData, taaOutputData);

                    graphContext.objectPool.Release(temporalAA);
                });
            }
        }
    }
}