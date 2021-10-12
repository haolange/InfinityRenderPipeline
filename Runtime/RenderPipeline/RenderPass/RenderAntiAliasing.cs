using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct FAntiAliasingPassString
    {
        internal static string PassName = "AntiAliasingPass";
        internal static string TextureName = "AccmulateTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FAntiAliasingPassData
        {
            public Camera camera;
            public RDGTextureRef depthTexture;
            public RDGTextureRef motionTexture;
            public RDGTextureRef hsitoryTexture;
            public RDGTextureRef aliasingTexture;
            public RDGTextureRef accmulateTexture;
            public FTemporalAntiAliasing temporalAA;
        }

        void RenderAntiAliasing(Camera camera, RTHandle historyRenderTexture)
        {
            TextureDescription antiAliasingDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FAntiAliasingPassString.TextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None, enableRandomWrite = true };

            RDGTextureRef hsitoryTexture = m_GraphBuilder.ImportTexture(historyRenderTexture);
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef motionTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.MotionBuffer);
            RDGTextureRef aliasingTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer);
            RDGTextureRef accmulateTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.AntiAliasingBuffer, antiAliasingDescription);

            //Add AntiAliasingPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<FAntiAliasingPassData>(FAntiAliasingPassString.PassName, ProfilingSampler.Get(CustomSamplerId.RenderAntiAliasing)))
            {
                //Setup Phase
                ref FAntiAliasingPassData passData = ref passRef.GetPassData<FAntiAliasingPassData>();
                passData.camera = camera;
                passData.temporalAA = m_TemporalAA;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.hsitoryTexture = passRef.ReadTexture(hsitoryTexture);
                passData.aliasingTexture = passRef.ReadTexture(aliasingTexture);
                passData.accmulateTexture = passRef.WriteTexture(accmulateTexture);

                //Execute Phase
                passRef.SetExecuteFunc((ref FAntiAliasingPassData passData, ref RDGContext graphContext) =>
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
                    passData.temporalAA.Render(graphContext.cmdBuffer, taaParameter, taaInputData, taaOutputData);
                });
            }
        }
    }
}