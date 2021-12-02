using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FAntiAliasingUtilityData
    {
        internal static string PassName = "AntiAliasingPass";
        internal static string HistoryTextureName = "HistoryTexture";
        internal static string AccmulateTextureName = "AccmulateTexture";
        internal static int HistoryTextureID = Shader.PropertyToID("HistoryTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct FAntiAliasingPassData
        {
            public Camera camera;
            public ComputeShader taaShader;
            public FRDGTextureRef depthTexture;
            public FRDGTextureRef motionTexture;
            public FRDGTextureRef hsitoryTexture;
            public FRDGTextureRef aliasingTexture;
            public FRDGTextureRef accmulateTexture;
        }

        void RenderAntiAliasing(Camera camera, FHistoryCache historyCache)
        {
            FTextureDescriptor historyDescriptor = new FTextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = FAntiAliasingUtilityData.HistoryTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = false };
            FTextureDescriptor accmulateDescriptor = new FTextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = FAntiAliasingUtilityData.AccmulateTextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = true };

            FRDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            FRDGTextureRef motionTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
            FRDGTextureRef aliasingTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DiffuseBuffer);
            FRDGTextureRef accmulateTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.AntiAliasingBuffer, accmulateDescriptor);
            FRDGTextureRef hsitoryTexture = m_GraphBuilder.ImportTexture(historyCache.GetTexture(FAntiAliasingUtilityData.HistoryTextureID, historyDescriptor));

            //Add AntiAliasingPass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<FAntiAliasingPassData>(FAntiAliasingUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderAntiAliasing)))
            {
                //Setup Phase
                ref FAntiAliasingPassData passData = ref passRef.GetPassData<FAntiAliasingPassData>();
                passData.camera = camera;
                passData.taaShader = pipelineAsset.taaShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.hsitoryTexture = passRef.ReadTexture(hsitoryTexture);
                passData.aliasingTexture = passRef.ReadTexture(aliasingTexture);
                passData.accmulateTexture = passRef.WriteTexture(accmulateTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in FAntiAliasingPassData passData, in FRDGContext graphContext) =>
                {
                    FTemporalAAInputData taaInputData;
                    {
                        taaInputData.resolution = new float4(passData.camera.pixelWidth, passData.camera.pixelHeight, 1.0f / passData.camera.pixelWidth, 1.0f / passData.camera.pixelHeight);
                        taaInputData.depthTexture = passData.depthTexture;
                        taaInputData.motionTexture = passData.motionTexture;
                        taaInputData.hsitoryTexture = passData.hsitoryTexture;
                        taaInputData.aliasingTexture = passData.aliasingTexture;
                    }
                    FTemporalAAOutputData taaOutputData;
                    {
                        taaOutputData.accmulateTexture = passData.accmulateTexture;
                    }
                    FTemporalAAParameter taaParameter = new FTemporalAAParameter(0.95f, 0.75f, 7500, 1);

                    FTemporalAntiAliasing temporalAA = graphContext.objectPool.Get<FTemporalAntiAliasing>();
                    temporalAA.Render(graphContext.cmdBuffer, passData.taaShader, taaParameter, taaInputData, taaOutputData);

                    graphContext.objectPool.Release(temporalAA);
                });
            }
        }
    }
}