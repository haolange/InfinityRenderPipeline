using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AtmosphericSkyFogPassUtilityData
    {
        internal static readonly int SRV_SunBufferID = Shader.PropertyToID("SRV_SunBuffer");
    }

    public partial class InfinityRenderPipeline
    {
        struct AtmosphericSkyFogPassData
        {
            public RGConstantsHandle parametersHandle;
            public int2 resolution;
            public ComputeKernel compositeKernel;
            public ComputeShader atmosphericLUTShader;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle skyViewLUTHandle;
            public RGTextureHandle aerialPerspectiveLUTHandle;
            public RGBufferHandle sunBufferHandle;
        }

        void ComputeAtmosphericSkyAndFog(in ViewContext context)
        {
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.opaque);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle skyViewLUTHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT);
            RGTextureHandle aerialPerspectiveLUTHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT);
            RGBufferHandle sunBufferHandle = m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.AtmosphereSunBuffer);

            AtmosphereParameter parameter = context.atmosphere;
            LightContext.ResolveSun(context.lights, out Vector4 sunDirection, out Vector4 sunIlluminance);

            {
                RGComputePassHandle<AtmosphericSkyFogPassData> passHandle = m_RGBuilder.AddComputePass<AtmosphericSkyFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericSkyAndFog));
                ref AtmosphericSkyFogPassData passData = ref passHandle.GetPassData();
                passData.parametersHandle = passHandle.WriteConstants(CreateAtmosphereConstants(parameter, sunDirection, sunIlluminance));
                passData.resolution = new int2(context.dimensions.internalSize.x, context.dimensions.internalSize.y);
                passData.compositeKernel = kernels.atmosphereComposite;
                passData.atmosphericLUTShader = shaders.atmosphericLUTShader;
                passData.lightingTextureHandle = passHandle.ReadTexture(lightingTextureHandle);
                passHandle.WriteTexture(lightingTextureHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                passData.skyViewLUTHandle = passHandle.ReadTexture(skyViewLUTHandle);
                passData.aerialPerspectiveLUTHandle = passHandle.ReadTexture(aerialPerspectiveLUTHandle);
                passData.sunBufferHandle = passHandle.ReadBuffer(sunBufferHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in AtmosphericSkyFogPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.CBV_AtmosphereParametersID, passData.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.SRV_SkyViewLUT, passData.skyViewLUTHandle);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.SRV_AerialPerspectiveLUT, passData.aerialPerspectiveLUTHandle);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetComputeBufferParam(passData.atmosphericLUTShader, passData.compositeKernel.index, AtmosphericSkyFogPassUtilityData.SRV_SunBufferID, passData.sunBufferHandle);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.UAV_LightingTexture, passData.lightingTextureHandle);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, passData.compositeKernel.index, passData.compositeKernel.GroupsX(passData.resolution.x), passData.compositeKernel.GroupsY(passData.resolution.y), 1);
                });
            }
        }
    }
}
