using System.Runtime.InteropServices;
using UnityEngine;
using InfinityTech.Core;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_HalfResParameters Shaders/Pass/Geometry/Compute_HalfResDownsample.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct HalfResParameters
    {
        public Vector4 fullResolution;
        public Vector4 halfResolution;
    }

    internal static class HalfResDownsamplePassUtilityData
    {
        internal static readonly int CBV_HalfResParametersID = Shader.PropertyToID("CBV_HalfResParameters");
        internal static readonly string DepthTextureName = "HalfResDepthTexture";
        internal static readonly string NormalTextureName = "HalfResNormalTexture";
        internal static readonly int SRV_FullResDepthID = Shader.PropertyToID("SRV_FullResDepthTexture");
        internal static readonly int SRV_FullResNormalID = Shader.PropertyToID("SRV_FullResNormalTexture");
        internal static readonly int UAV_HalfResDepthID = Shader.PropertyToID("UAV_HalfResDepthTexture");
        internal static readonly int UAV_HalfResNormalID = Shader.PropertyToID("UAV_HalfResNormalTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct HalfResDownsamplePassData
        {
            public int2 halfResolution;
            public RGConstantsHandle parametersHandle;
            public ComputeKernel halfRes;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle normalTextureHandle;
            public RGTextureHandle halfResDepthTextureHandle;
            public RGTextureHandle halfResNormalTextureHandle;
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
                halfResDepthDsc.enableRandomWrite = true;
            }
            RGTextureHandle halfResDepthTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.HalfResDepthBuffer, halfResDepthDsc);

            TextureDescriptor halfResNormalDsc = new TextureDescriptor(halfWidth, halfHeight, ETextureSizingDomain.Internal, 2);
            {
                halfResNormalDsc.name = HalfResDownsamplePassUtilityData.NormalTextureName;
                halfResNormalDsc.dimension = TextureDimension.Tex2D;
                halfResNormalDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                halfResNormalDsc.enableRandomWrite = true;
            }
            RGTextureHandle halfResNormalTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.HalfResNormalBuffer, halfResNormalDsc);

            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add HalfResDownsamplePass
            {
                RGComputePassHandle<HalfResDownsamplePassData> passHandle = m_RGBuilder.AddComputePass<HalfResDownsamplePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeHalfResDownsample));
                //Setup Phase
                ref HalfResDownsamplePassData passData = ref passHandle.GetPassData();
                passData.parametersHandle = passHandle.WriteConstants(new HalfResParameters
                {
                    fullResolution = new Vector4(fullWidth, fullHeight, 1.0f / fullWidth, 1.0f / fullHeight),
                    halfResolution = new Vector4(halfWidth, halfHeight, 1.0f / halfWidth, 1.0f / halfHeight)
                });
                passData.halfResolution = new int2(halfWidth, halfHeight);
                passData.halfRes = kernels.halfResDownsample;
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                passData.normalTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.halfResDepthTextureHandle = passHandle.WriteTexture(halfResDepthTextureHandle);
                passData.halfResNormalTextureHandle = passHandle.WriteTexture(halfResNormalTextureHandle);

                //Execute Phase
                passHandle.EnablePassCulling(false);
                passHandle.EnableAsyncCompute(true);
                passHandle.SetExecuteFunc((in HalfResDownsamplePassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(passData.halfRes.shader, HalfResDownsamplePassUtilityData.CBV_HalfResParametersID, passData.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.SRV_FullResDepthID, passData.depthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.SRV_FullResNormalID, passData.normalTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.UAV_HalfResDepthID, passData.halfResDepthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.halfRes.shader, passData.halfRes.index, HalfResDownsamplePassUtilityData.UAV_HalfResNormalID, passData.halfResNormalTextureHandle);
                    cmdEncoder.DispatchCompute(passData.halfRes.shader, passData.halfRes.index, passData.halfRes.GroupsX(passData.halfResolution.x), passData.halfRes.GroupsY(passData.halfResolution.y), 1);
                });
            }
            context.features.MarkProduced(EFrameFeature.HalfResDownsample);
        }
    }
}
