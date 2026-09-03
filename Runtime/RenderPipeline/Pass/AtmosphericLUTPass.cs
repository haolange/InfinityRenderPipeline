using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AtmosphericLUTPassUtilityData
    {
        internal static string TransmittanceName = "AtmosphereTransmittanceLUT";
        internal static string MultiScatteringName = "AtmosphereMultiScatteringLUT";
        internal static string SkyViewName = "AtmosphereSkyViewLUT";
        internal static string AerialPerspectiveName = "AtmosphereAerialPerspectiveLUT";
        internal static string SunBufferName = "AtmosphereSunBuffer";
        internal static int Atmo_PlanetRadiusID = Shader.PropertyToID("Atmo_PlanetRadius");
        internal static int Atmo_AtmosphereHeightID = Shader.PropertyToID("Atmo_AtmosphereHeight");
        internal static int Atmo_RayleighScatteringID = Shader.PropertyToID("Atmo_RayleighScattering");
        internal static int Atmo_RayleighHeightID = Shader.PropertyToID("Atmo_RayleighHeight");
        internal static int Atmo_MieScatteringID = Shader.PropertyToID("Atmo_MieScattering");
        internal static int Atmo_MieAbsorptionID = Shader.PropertyToID("Atmo_MieAbsorption");
        internal static int Atmo_MieHeightID = Shader.PropertyToID("Atmo_MieHeight");
        internal static int Atmo_MieAnisotropyID = Shader.PropertyToID("Atmo_MieAnisotropy");
        internal static int Atmo_OzoneAbsorptionID = Shader.PropertyToID("Atmo_OzoneAbsorption");
        internal static int Atmo_OzoneLayerCenterID = Shader.PropertyToID("Atmo_OzoneLayerCenter");
        internal static int Atmo_OzoneLayerWidthID = Shader.PropertyToID("Atmo_OzoneLayerWidth");
        internal static int Atmo_GroundAlbedoID = Shader.PropertyToID("Atmo_GroundAlbedo");
        internal static int Atmo_BrightnessID = Shader.PropertyToID("Atmo_Brightness");
        internal static int Atmo_MultiScatterStrengthID = Shader.PropertyToID("Atmo_MultiScatterStrength");
        internal static int Atmo_DrawGroundID = Shader.PropertyToID("Atmo_DrawGround");
        internal static int Atmo_AerialPerspectiveDistanceID = Shader.PropertyToID("Atmo_AerialPerspectiveDistance");
        internal static int Atmo_SunAngleID = Shader.PropertyToID("Atmo_SunAngle");
        internal static int Atmo_SunDirectionID = Shader.PropertyToID("Atmo_SunDirection");
        internal static int Atmo_SunIlluminanceID = Shader.PropertyToID("Atmo_SunIlluminance");
        internal static int UAV_TransmittanceLUTID = Shader.PropertyToID("UAV_TransmittanceLUT");
        internal static int UAV_MultiScatteringLUTID = Shader.PropertyToID("UAV_MultiScatteringLUT");
        internal static int UAV_SkyViewLUTID = Shader.PropertyToID("UAV_SkyViewLUT");
        internal static int UAV_AerialPerspectiveLUTID = Shader.PropertyToID("UAV_AerialPerspectiveLUT");
        internal static int UAV_SunBufferID = Shader.PropertyToID("UAV_SunBuffer");
        internal static int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static int SRV_MultiScatteringLUTID = Shader.PropertyToID("SRV_MultiScatteringLUT");
        internal static int KernelTransmittance = 0;
        internal static int KernelMultiScattering = 1;
        internal static int KernelSkyView = 2;
        internal static int KernelAerialPerspective = 3;
        // Kernel 4 is AtmosphereCubemap, which has no consumer and is not dispatched.
        internal static int KernelSunBuffer = 5;
    }

    public partial class InfinityRenderPipeline
    {
        struct AtmosphericLUTPassData
        {
            public AtmosphereParameter parameter;
            public Vector4 sunDirection;
            public Vector4 sunIlluminance;
            public Vector4 worldSpaceCameraPos;
            public Matrix4x4 matrix_InvViewProj;
            public ComputeShader atmosphericLUTShader;
            public RGTextureRef transmittanceLUT;
            public RGTextureRef multiScatteringLUT;
            public RGTextureRef skyViewLUT;
            public RGTextureRef aerialPerspectiveLUT;
            public RGBufferRef sunBuffer;
        }

        static TextureDescriptor CreateAtmosphereLUTDescriptor(int width, int height, string name)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = true;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
        }

        static void BindAtmosphereParameters(in AtmosphericLUTPassData passData, in RGComputeEncoder cmdEncoder)
        {
            ComputeShader shader = passData.atmosphericLUTShader;
            AtmosphereParameter parameter = passData.parameter;
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_PlanetRadiusID, parameter.planetRadius);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_AtmosphereHeightID, parameter.atmosphereHeight);
            cmdEncoder.SetComputeVectorParam(shader, AtmosphericLUTPassUtilityData.Atmo_RayleighScatteringID, parameter.RayleighScatteringPerMeter);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_RayleighHeightID, parameter.rayleighHeight);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_MieScatteringID, parameter.MieScatteringPerMeter);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_MieAbsorptionID, parameter.MieAbsorptionPerMeter);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_MieHeightID, parameter.mieHeight);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_MieAnisotropyID, parameter.mieAnisotropy);
            cmdEncoder.SetComputeVectorParam(shader, AtmosphericLUTPassUtilityData.Atmo_OzoneAbsorptionID, parameter.OzoneAbsorptionPerMeter);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_OzoneLayerCenterID, parameter.ozoneLayerCenter);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_OzoneLayerWidthID, parameter.ozoneLayerWidth);
            cmdEncoder.SetComputeVectorParam(shader, AtmosphericLUTPassUtilityData.Atmo_GroundAlbedoID, (Vector4)parameter.groundAlbedo);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_BrightnessID, parameter.brightness);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_MultiScatterStrengthID, parameter.multiScatterStrength);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_DrawGroundID, parameter.drawGround ? 1.0f : 0.0f);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_AerialPerspectiveDistanceID, parameter.aerialPerspectiveDistance);
            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_SunAngleID, parameter.sunAngle);
            cmdEncoder.SetComputeVectorParam(shader, AtmosphericLUTPassUtilityData.Atmo_SunDirectionID, passData.sunDirection);
            cmdEncoder.SetComputeVectorParam(shader, AtmosphericLUTPassUtilityData.Atmo_SunIlluminanceID, passData.sunIlluminance);
            cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
            cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
        }

        void ComputeAtmosphericLUT(RenderContext renderContext, Camera camera)
        {
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.atmosphericLUTShader, "TransmittanceLUT", "MultiScatteringLUT", "SkyViewLUT", "AerialPerspectiveLUT", "SunBuffer"))
            {
                return;
            }

            AtmosphereParameter parameter = AtmosphereParameter.Resolve(pipelineAsset, ActiveVolumeStack);

            RGTextureRef transmittanceLUT = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, CreateAtmosphereLUTDescriptor(parameter.transmittanceLUTWidth, parameter.transmittanceLUTHeight, AtmosphericLUTPassUtilityData.TransmittanceName));
            RGTextureRef multiScatteringLUT = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, CreateAtmosphereLUTDescriptor(parameter.multiScatteringLUTSize, parameter.multiScatteringLUTSize, AtmosphericLUTPassUtilityData.MultiScatteringName));
            RGTextureRef skyViewLUT = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, CreateAtmosphereLUTDescriptor(parameter.skyViewLUTWidth, parameter.skyViewLUTHeight, AtmosphericLUTPassUtilityData.SkyViewName));

            TextureDescriptor aerialDsc = new TextureDescriptor(parameter.aerialPerspectiveSize, parameter.aerialPerspectiveSize, parameter.aerialPerspectiveSize);
            aerialDsc.name = AtmosphericLUTPassUtilityData.AerialPerspectiveName;
            aerialDsc.dimension = TextureDimension.Tex3D;
            aerialDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            aerialDsc.depthBufferBits = EDepthBits.None;
            aerialDsc.enableRandomWrite = true;
            aerialDsc.filterMode = FilterMode.Trilinear;
            aerialDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef aerialPerspectiveLUT = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, aerialDsc);

            // The AtmosphereCubemap kernel has no consumer yet (no sky IBL path), so it is not
            // recorded. Recording it would burn 6x cubemapSize^2 ray marches per frame for nothing.
            // See AGENTS.md "Known gaps".

            BufferDescriptor sunDsc = new BufferDescriptor(1, sizeof(float) * 4, ComputeBufferType.Structured);
            sunDsc.name = AtmosphericLUTPassUtilityData.SunBufferName;
            RGBufferRef sunBuffer = m_RGScoper.CreateBuffer(InfinityShaderIDs.AtmosphereSunBuffer, sunDsc);

            Vector4 sunDirection = new Vector4(0, 1, 0, 0);
            Vector4 sunIlluminance = new Vector4(1, 1, 1, 1);
            Light sunLight = RenderSettings.sun;
            if (sunLight != null)
            {
                sunDirection = -sunLight.transform.forward;
                sunIlluminance = (Vector4)(sunLight.color * sunLight.intensity);
            }

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<AtmosphericLUTPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericLUT)))
            {
                ref AtmosphericLUTPassData passData = ref passRef.GetPassData<AtmosphericLUTPassData>();
                passData.parameter = parameter;
                passData.sunDirection = sunDirection;
                passData.sunIlluminance = sunIlluminance;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.matrix_InvViewProj = GraphicsUtility.GetComputeInvViewProj(camera);
                passData.atmosphericLUTShader = pipelineAsset.atmosphericLUTShader;
                passData.transmittanceLUT = passRef.WriteTexture(transmittanceLUT);
                passData.multiScatteringLUT = passRef.WriteTexture(multiScatteringLUT);
                passData.skyViewLUT = passRef.WriteTexture(skyViewLUT);
                passData.aerialPerspectiveLUT = passRef.WriteTexture(aerialPerspectiveLUT);
                passData.sunBuffer = passRef.WriteBuffer(sunBuffer);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in AtmosphericLUTPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    BindAtmosphereParameters(passData, cmdEncoder);

                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelTransmittance, AtmosphericLUTPassUtilityData.UAV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelTransmittance, Mathf.CeilToInt(passData.parameter.transmittanceLUTWidth / 8.0f), Mathf.CeilToInt(passData.parameter.transmittanceLUTHeight / 8.0f), 1);

                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelMultiScattering, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelMultiScattering, AtmosphericLUTPassUtilityData.UAV_MultiScatteringLUTID, passData.multiScatteringLUT);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelMultiScattering, Mathf.CeilToInt(passData.parameter.multiScatteringLUTSize / 8.0f), Mathf.CeilToInt(passData.parameter.multiScatteringLUTSize / 8.0f), 1);

                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelSkyView, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelSkyView, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelSkyView, AtmosphericLUTPassUtilityData.UAV_SkyViewLUTID, passData.skyViewLUT);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelSkyView, Mathf.CeilToInt(passData.parameter.skyViewLUTWidth / 8.0f), Mathf.CeilToInt(passData.parameter.skyViewLUTHeight / 8.0f), 1);

                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, AtmosphericLUTPassUtilityData.UAV_AerialPerspectiveLUTID, passData.aerialPerspectiveLUT);
                    int aerialGroups = Mathf.CeilToInt(passData.parameter.aerialPerspectiveSize / 4.0f);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, aerialGroups, aerialGroups, aerialGroups);

                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelSunBuffer, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetComputeBufferParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelSunBuffer, AtmosphericLUTPassUtilityData.UAV_SunBufferID, passData.sunBuffer);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelSunBuffer, 1, 1, 1);
                });
            }
        }
    }
}
