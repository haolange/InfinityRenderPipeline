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
            public RGConstantsHandle parameters;
            public int2 resolution;
            public ComputeKernel compositeKernel;
            public ComputeShader atmosphericLUTShader;
            public RGTextureHandle lightingTexture;
            public RGTextureHandle depthTexture;
            public RGTextureHandle skyViewLUT;
            public RGTextureHandle aerialPerspectiveLUT;
            public RGBufferHandle sunBuffer;
        }

        void ComputeAtmosphericSkyAndFog(in CameraRecordContext context)
        {
            RGTextureHandle lightingTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer);
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle skyViewLUT = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT);
            RGTextureHandle aerialPerspectiveLUT = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT);
            RGBufferHandle sunBuffer = m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.AtmosphereSunBuffer);

            AtmosphereParameter parameter = context.atmosphere;
            LightContext.ResolveSun(renderContext.lightContext, out Vector4 sunDirection, out Vector4 sunIlluminance);

            using (var passRef = m_RGBuilder.AddComputePass<AtmosphericSkyFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericSkyAndFog)))
            {
                ref AtmosphericSkyFogPassData passData = ref passRef.GetPassData();
                passData.parameters = passRef.WriteConstants(CreateAtmosphereConstants(parameter, sunDirection, sunIlluminance));
                passData.resolution = new int2(context.dimensions.internalSize.x, context.dimensions.internalSize.y);
                passData.compositeKernel = kernels.atmosphereComposite;
                passData.atmosphericLUTShader = shaders.atmosphericLUTShader;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passRef.WriteTexture(lightingTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.skyViewLUT = passRef.ReadTexture(skyViewLUT);
                passData.aerialPerspectiveLUT = passRef.ReadTexture(aerialPerspectiveLUT);
                passData.sunBuffer = passRef.ReadBuffer(sunBuffer);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in AtmosphericSkyFogPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.CBV_AtmosphereParametersID, passData.parameters);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.SRV_SkyViewLUT, passData.skyViewLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.SRV_AerialPerspectiveLUT, passData.aerialPerspectiveLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    cmdEncoder.SetComputeBufferParam(passData.atmosphericLUTShader, passData.compositeKernel.index, AtmosphericSkyFogPassUtilityData.SRV_SunBufferID, passData.sunBuffer);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, passData.compositeKernel.index, PassShaderIDs.UAV_LightingTexture, passData.lightingTexture);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, passData.compositeKernel.index, passData.compositeKernel.GroupsX(passData.resolution.x), passData.compositeKernel.GroupsY(passData.resolution.y), 1);
                });
            }
        }
    }
}
