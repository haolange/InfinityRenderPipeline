using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class HalfResDownsamplePassUtilityData
    {
        internal static string DepthTextureName = "HalfResDepthTexture";
        internal static string NormalTextureName = "HalfResNormalTexture";
        internal static int SRV_FullResDepthID = Shader.PropertyToID("SRV_FullResDepthTexture");
        internal static int UAV_HalfResDepthID = Shader.PropertyToID("UAV_HalfResDepthTexture");
        internal static int UAV_HalfResNormalID = Shader.PropertyToID("UAV_HalfResNormalTexture");
        internal static int HalfRes_FullResolutionID = Shader.PropertyToID("HalfRes_FullResolution");
        internal static int HalfRes_HalfResolutionID = Shader.PropertyToID("HalfRes_HalfResolution");
    }

    public partial class InfinityRenderPipeline
    {
        struct HalfResDownsamplePassData
        {
            public int2 fullResolution;
            public int2 halfResolution;
            public ComputeShader halfResShader;
            public RGTextureRef depthTexture;
            public RGTextureRef halfResDepthTexture;
            public RGTextureRef halfResNormalTexture;
        }

        void ComputeHalfResDownsample(RenderContext renderContext, Camera camera)
        {
            int fullWidth = camera.pixelWidth;
            int fullHeight = camera.pixelHeight;
            int halfWidth = Mathf.Max(1, fullWidth >> 1);
            int halfHeight = Mathf.Max(1, fullHeight >> 1);

            TextureDescriptor halfResDepthDsc = new TextureDescriptor(halfWidth, halfHeight);
            {
                halfResDepthDsc.name = HalfResDownsamplePassUtilityData.DepthTextureName;
                halfResDepthDsc.dimension = TextureDimension.Tex2D;
                halfResDepthDsc.colorFormat = GraphicsFormat.R32_SFloat;
                halfResDepthDsc.depthBufferBits = EDepthBits.None;
                halfResDepthDsc.enableRandomWrite = true;
            }
            RGTextureRef halfResDepthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.HalfResDepthBuffer, halfResDepthDsc);

            TextureDescriptor halfResNormalDsc = new TextureDescriptor(halfWidth, halfHeight);
            {
                halfResNormalDsc.name = HalfResDownsamplePassUtilityData.NormalTextureName;
                halfResNormalDsc.dimension = TextureDimension.Tex2D;
                halfResNormalDsc.colorFormat = GraphicsFormat.R8G8B8A8_SNorm;
                halfResNormalDsc.depthBufferBits = EDepthBits.None;
                halfResNormalDsc.enableRandomWrite = true;
            }
            RGTextureRef halfResNormalTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.HalfResNormalBuffer, halfResNormalDsc);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add HalfResDownsamplePass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<HalfResDownsamplePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeHalfResDownsample)))
            {
                //Setup Phase
                ref HalfResDownsamplePassData passData = ref passRef.GetPassData<HalfResDownsamplePassData>();
                passData.fullResolution = new int2(fullWidth, fullHeight);
                passData.halfResolution = new int2(halfWidth, halfHeight);
                passData.halfResShader = pipelineAsset.halfResDownsampleShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.halfResDepthTexture = passRef.WriteTexture(halfResDepthTexture);
                passData.halfResNormalTexture = passRef.WriteTexture(halfResNormalTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in HalfResDownsamplePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.halfResShader == null) return;

                    cmdEncoder.SetComputeTextureParam(passData.halfResShader, 0, HalfResDownsamplePassUtilityData.SRV_FullResDepthID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.halfResShader, 0, HalfResDownsamplePassUtilityData.UAV_HalfResDepthID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.halfResShader, 0, HalfResDownsamplePassUtilityData.UAV_HalfResNormalID, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeVectorParam(passData.halfResShader, HalfResDownsamplePassUtilityData.HalfRes_FullResolutionID, new Vector4(passData.fullResolution.x, passData.fullResolution.y, 1.0f / passData.fullResolution.x, 1.0f / passData.fullResolution.y));
                    cmdEncoder.SetComputeVectorParam(passData.halfResShader, HalfResDownsamplePassUtilityData.HalfRes_HalfResolutionID, new Vector4(passData.halfResolution.x, passData.halfResolution.y, 1.0f / passData.halfResolution.x, 1.0f / passData.halfResolution.y));
                    cmdEncoder.DispatchCompute(passData.halfResShader, 0, Mathf.CeilToInt(passData.halfResolution.x / 8.0f), Mathf.CeilToInt(passData.halfResolution.y / 8.0f), 1);
                });
            }
        }
    }
}
