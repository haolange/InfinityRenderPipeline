using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AtmosphericSkyFogPassUtilityData
    {
        internal static int SRV_SkyViewLUTID = Shader.PropertyToID("SRV_SkyViewLUT");
        internal static int SRV_AerialPerspectiveLUTID = Shader.PropertyToID("SRV_AerialPerspectiveLUT");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_SunBufferID = Shader.PropertyToID("SRV_SunBuffer");
        internal static int UAV_LightingTextureID = Shader.PropertyToID("UAV_LightingTexture");
        internal static int Atmo_FarDepthID = Shader.PropertyToID("Atmo_FarDepth");
        internal static int KernelComposite = 6;
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
            public ComputeShader atmosphericLUTShader;
            public RGTextureRef lightingTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef skyViewLUT;
            public RGTextureRef aerialPerspectiveLUT;
            public RGBufferRef sunBuffer;
        }

        void RenderAtmosphericSkyAndFog(RenderContext renderContext, Camera camera)
        {
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.atmosphericLUTShader, "AtmosphereComposite"))
            {
                return;
            }
            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.LightingBuffer, out RGTextureRef lightingTexture) ||
                !m_RGScoper.TryQueryTexture(InfinityShaderIDs.DepthBuffer, out RGTextureRef depthTexture) ||
                !m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, out RGTextureRef skyViewLUT) ||
                !m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, out RGTextureRef aerialPerspectiveLUT) ||
                !m_RGScoper.TryQueryBuffer(InfinityShaderIDs.AtmosphereSunBuffer, out RGBufferRef sunBuffer))
            {
                return;
            }

            AtmosphereParameter parameter = AtmosphereParameter.Resolve(pipelineAsset);
            Vector4 sunDirection = new Vector4(0, 1, 0, 0);
            Vector4 sunIlluminance = new Vector4(1, 1, 1, 1);
            Light sunLight = RenderSettings.sun;
            if (sunLight != null)
            {
                sunDirection = -sunLight.transform.forward;
                sunIlluminance = (Vector4)(sunLight.color * sunLight.intensity);
            }

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<AtmosphericSkyFogPassData>(ProfilingSampler.Get(CustomSamplerId.RenderAtmosphericSkyAndFog)))
            {
                ref AtmosphericSkyFogPassData passData = ref passRef.GetPassData<AtmosphericSkyFogPassData>();
                passData.parameter = parameter;
                passData.sunDirection = sunDirection;
                passData.sunIlluminance = sunIlluminance;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.matrix_InvViewProj = GraphicsUtility.GetComputeInvViewProj(camera);
                passData.farDepth = GraphicsUtility.SampledFarDepth;
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.atmosphericLUTShader = pipelineAsset.atmosphericLUTShader;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passRef.WriteTexture(lightingTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.skyViewLUT = passRef.ReadTexture(skyViewLUT);
                passData.aerialPerspectiveLUT = passRef.ReadTexture(aerialPerspectiveLUT);
                passData.sunBuffer = passRef.ReadBuffer(sunBuffer);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in AtmosphericSkyFogPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
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
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericSkyFogPassUtilityData.KernelComposite, AtmosphericSkyFogPassUtilityData.SRV_SkyViewLUTID, passData.skyViewLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericSkyFogPassUtilityData.KernelComposite, AtmosphericSkyFogPassUtilityData.SRV_AerialPerspectiveLUTID, passData.aerialPerspectiveLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericSkyFogPassUtilityData.KernelComposite, AtmosphericSkyFogPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeBufferParam(passData.atmosphericLUTShader, AtmosphericSkyFogPassUtilityData.KernelComposite, AtmosphericSkyFogPassUtilityData.SRV_SunBufferID, passData.sunBuffer);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericSkyFogPassUtilityData.KernelComposite, AtmosphericSkyFogPassUtilityData.UAV_LightingTextureID, passData.lightingTexture);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericSkyFogPassUtilityData.KernelComposite, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }
    }
}
