using UnityEngine;
using InfinityTech.Core;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class HalfResDownsamplePassUtilityData
    {
        internal static readonly string DepthTextureName = "HalfResDepthTexture";
        internal static readonly string NormalTextureName = "HalfResNormalTexture";
        internal static readonly int SRV_FullResDepthID = Shader.PropertyToID("SRV_FullResDepthTexture");
        internal static readonly int SRV_FullResNormalID = Shader.PropertyToID("SRV_FullResNormalTexture");
        internal static readonly int UAV_HalfResDepthID = Shader.PropertyToID("UAV_HalfResDepthTexture");
        internal static readonly int UAV_HalfResNormalID = Shader.PropertyToID("UAV_HalfResNormalTexture");
        internal static readonly int HalfRes_FullResolutionID = Shader.PropertyToID("HalfRes_FullResolution");
        internal static readonly int HalfRes_HalfResolutionID = Shader.PropertyToID("HalfRes_HalfResolution");
    }

    public partial class InfinityRenderPipeline
    {
        struct HalfResDownsamplePassData
        {
            public int2 fullResolution;
            public int2 halfResolution;
            public ComputeKernel halfRes;
            public RGTextureHandle depthTexture;
            public RGTextureHandle normalTexture;
            public RGTextureHandle halfResDepthTexture;
            public RGTextureHandle halfResNormalTexture;
        }

        void ComputeHalfResDownsample(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.HalfResDownsample))
            {
                return;
            }

            var sourceDescriptor = m_RGBuilder.GetTextureDescriptor(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
            int fullWidth = sourceDescriptor.width;
            int fullHeight = sourceDescriptor.height;
            int halfWidth = Mathf.Max(1, fullWidth >> 1);
            int halfHeight = Mathf.Max(1, fullHeight >> 1);

            TextureDescriptor halfResDepthDsc = new TextureDescriptor(halfWidth, halfHeight, ETextureSizingDomain.Internal, 2);
            {
                halfResDepthDsc.name = HalfResDownsamplePassUtilityData.DepthTextureName;
                halfResDepthDsc.dimension = TextureDimension.Tex2D;
                halfResDepthDsc.colorFormat = GraphicsFormat.R32_SFloat;
                halfResDepthDsc.depthBufferBits = EDepthBits.None;
                halfResDepthDsc.enableRandomWrite = true;
            }
            RGTextureHandle halfResDepthTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.HalfResDepthBuffer, halfResDepthDsc);

            TextureDescriptor halfResNormalDsc = new TextureDescriptor(halfWidth, halfHeight, ETextureSizingDomain.Internal, 2);
            {
                halfResNormalDsc.name = HalfResDownsamplePassUtilityData.NormalTextureName;
                halfResNormalDsc.dimension = TextureDimension.Tex2D;
                halfResNormalDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                halfResNormalDsc.depthBufferBits = EDepthBits.None;
                halfResNormalDsc.enableRandomWrite = true;
            }
            RGTextureHandle halfResNormalTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.HalfResNormalBuffer, halfResNormalDsc);

            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add HalfResDownsamplePass
            using (var passRef = m_RGBuilder.AddComputePass<HalfResDownsamplePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeHalfResDownsample)))
            {
                //Setup Phase
                ref HalfResDownsamplePassData passData = ref passRef.GetPassData();
                passData.fullResolution = new int2(fullWidth, fullHeight);
                passData.halfResolution = new int2(halfWidth, halfHeight);
                passData.halfRes = kernels.halfResDownsample;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.normalTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.halfResDepthTexture = passRef.WriteTexture(halfResDepthTexture);
                passData.halfResNormalTexture = passRef.WriteTexture(halfResNormalTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in HalfResDownsamplePassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.SRV_FullResDepthID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.SRV_FullResNormalID, passData.normalTexture);
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.UAV_HalfResDepthID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.UAV_HalfResNormalID, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeVectorParam(passData.halfRes.shader, HalfResDownsamplePassUtilityData.HalfRes_FullResolutionID, new Vector4(passData.fullResolution.x, passData.fullResolution.y, 1.0f / passData.fullResolution.x, 1.0f / passData.fullResolution.y));
                    cmdEncoder.SetComputeVectorParam(passData.halfRes.shader, HalfResDownsamplePassUtilityData.HalfRes_HalfResolutionID, new Vector4(passData.halfResolution.x, passData.halfResolution.y, 1.0f / passData.halfResolution.x, 1.0f / passData.halfResolution.y));
                    cmdEncoder.DispatchCompute(passData.halfRes.shader, passData.halfRes.index, passData.halfRes.GroupsX(passData.halfResolution.x), passData.halfRes.GroupsY(passData.halfResolution.y), 1);
                });
            }
            context.features.MarkProduced(EFrameFeature.HalfResDownsample);
        }
    }
}
