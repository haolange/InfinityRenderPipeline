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
        internal static readonly int Atmo_FarDepthID = Shader.PropertyToID("Atmo_FarDepth");
    }

    public partial class InfinityRenderPipeline
    {
        struct AtmosphericSkyFogPassData
        {
            public AtmosphereParameter parameter;
            public Vector4 sunDirection;
            public Vector4 sunIlluminance;
            public Vector4 worldSpaceCameraPos;
            public Matrix4x4 matrix_InvViewProj;
            public float farDepth;
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
            if (!m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer, out RGTextureHandle lightingTexture) ||
                !m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.DepthBuffer, out RGTextureHandle depthTexture) ||
                !m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, out RGTextureHandle skyViewLUT) ||
                !m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, out RGTextureHandle aerialPerspectiveLUT) ||
                !m_RGBuilder.scoper.TryQueryBuffer(InfinityShaderIDs.AtmosphereSunBuffer, out RGBufferHandle sunBuffer))
            {
                throw new System.InvalidOperationException("InfinityRP: Atmosphere composite is missing Lighting, Depth, SkyView, Aerial, or SunBuffer.");
            }

            AtmosphereParameter parameter = context.atmosphere;
            LightContext.ResolveSun(renderContext.lightContext, out Vector4 sunDirection, out Vector4 sunIlluminance);

            using (var passRef = m_RGBuilder.AddComputePass<AtmosphericSkyFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericSkyAndFog)))
            {
                ref AtmosphericSkyFogPassData passData = ref passRef.GetPassData();
                passData.parameter = parameter;
                passData.sunDirection = sunDirection;
                passData.sunIlluminance = sunIlluminance;
                passData.worldSpaceCameraPos = context.camera.transform.position;
                passData.matrix_InvViewProj = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.farDepth = GraphicsConventions.SampledFarDepth;
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
                    AtmosphericLUTPassData lutData = new AtmosphericLUTPassData
                    {
                        parameter = passData.parameter,
                        sunDirection = passData.sunDirection,
                        sunIlluminance = passData.sunIlluminance,
                        worldSpaceCameraPos = passData.worldSpaceCameraPos,
                        matrix_InvViewProj = passData.matrix_InvViewProj,
                        atmosphericLUTShader = passData.atmosphericLUTShader
                    };
                    BindAtmosphereParameters(lutData, cmdEncoder);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericSkyFogPassUtilityData.Atmo_FarDepthID, passData.farDepth);
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
