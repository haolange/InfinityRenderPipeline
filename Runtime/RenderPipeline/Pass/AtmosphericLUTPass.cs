using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AtmosphericLUTPassUtilityData
    {
        internal static string TransmittanceName = "AtmosphereTransmittanceLUT";
        internal static string ScatteringName = "AtmosphereScatteringLUT";
        internal static string MultiScatteringName = "AtmosphereMultiScatteringLUT";
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
        internal static int UAV_TransmittanceLUTID = Shader.PropertyToID("UAV_TransmittanceLUT");
        internal static int UAV_MultiScatteringLUTID = Shader.PropertyToID("UAV_MultiScatteringLUT");
        internal static int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static int KernelTransmittance = 0;
        internal static int KernelMultiScattering = 1;
    }

    public partial class InfinityRenderPipeline
    {
        struct AtmosphericLUTPassData
        {
            public float planetRadius;
            public float atmosphereHeight;
            public Color rayleighScattering;
            public float rayleighHeight;
            public float mieScattering;
            public float mieAbsorption;
            public float mieHeight;
            public float mieAnisotropy;
            public Color ozoneAbsorption;
            public float ozoneLayerCenter;
            public float ozoneLayerWidth;
            public Color groundAlbedo;
            public int2 transmittanceLUTSize;
            public int multiScatteringLUTSize;
            public ComputeShader atmosphericLUTShader;
            public RGTextureRef transmittanceLUT;
            public RGTextureRef multiScatteringLUT;
        }

        void ComputeAtmosphericLUT(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var atmo = stack.GetComponent<AtmosphericScattering>();
            if (atmo == null) return;

            int transmittanceWidth = atmo.TransmittanceLUTWidth.value;
            int transmittanceHeight = atmo.TransmittanceLUTHeight.value;
            int multiScatteringSize = atmo.MultiScatteringLUTSize.value;

            TextureDescriptor transmittanceDsc = new TextureDescriptor(transmittanceWidth, transmittanceHeight);
            {
                transmittanceDsc.name = AtmosphericLUTPassUtilityData.TransmittanceName;
                transmittanceDsc.dimension = TextureDimension.Tex2D;
                transmittanceDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                transmittanceDsc.depthBufferBits = EDepthBits.None;
                transmittanceDsc.enableRandomWrite = true;
            }
            RGTextureRef transmittanceLUT = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, transmittanceDsc);

            TextureDescriptor multiScatteringDsc = new TextureDescriptor(multiScatteringSize, multiScatteringSize);
            {
                multiScatteringDsc.name = AtmosphericLUTPassUtilityData.MultiScatteringName;
                multiScatteringDsc.dimension = TextureDimension.Tex2D;
                multiScatteringDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                multiScatteringDsc.depthBufferBits = EDepthBits.None;
                multiScatteringDsc.enableRandomWrite = true;
            }
            RGTextureRef multiScatteringLUT = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, multiScatteringDsc);

            //Add AtmosphericLUTPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<AtmosphericLUTPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericLUT)))
            {
                //Setup Phase
                ref AtmosphericLUTPassData passData = ref passRef.GetPassData<AtmosphericLUTPassData>();
                passData.planetRadius = atmo.PlanetRadius.value;
                passData.atmosphereHeight = atmo.AtmosphereHeight.value;
                passData.rayleighScattering = atmo.RayleighScattering.value;
                passData.rayleighHeight = atmo.RayleighHeight.value;
                passData.mieScattering = atmo.MieScattering.value;
                passData.mieAbsorption = atmo.MieAbsorption.value;
                passData.mieHeight = atmo.MieHeight.value;
                passData.mieAnisotropy = atmo.MieAnisotropy.value;
                passData.ozoneAbsorption = atmo.OzoneAbsorption.value;
                passData.ozoneLayerCenter = atmo.OzoneLayerCenter.value;
                passData.ozoneLayerWidth = atmo.OzoneLayerWidth.value;
                passData.groundAlbedo = atmo.GroundAlbedo.value;
                passData.transmittanceLUTSize = new int2(transmittanceWidth, transmittanceHeight);
                passData.multiScatteringLUTSize = multiScatteringSize;
                passData.atmosphericLUTShader = pipelineAsset.atmosphericLUTShader;
                passData.transmittanceLUT = passRef.WriteTexture(transmittanceLUT);
                passData.multiScatteringLUT = passRef.WriteTexture(multiScatteringLUT);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in AtmosphericLUTPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.atmosphericLUTShader == null) return;

                    // Set atmospheric parameters
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_PlanetRadiusID, passData.planetRadius);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_AtmosphereHeightID, passData.atmosphereHeight);
                    cmdEncoder.SetComputeVectorParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_RayleighScatteringID, (Vector4)passData.rayleighScattering);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_RayleighHeightID, passData.rayleighHeight);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_MieScatteringID, passData.mieScattering);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_MieAbsorptionID, passData.mieAbsorption);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_MieHeightID, passData.mieHeight);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_MieAnisotropyID, passData.mieAnisotropy);
                    cmdEncoder.SetComputeVectorParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_OzoneAbsorptionID, (Vector4)passData.ozoneAbsorption);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_OzoneLayerCenterID, passData.ozoneLayerCenter);
                    cmdEncoder.SetComputeFloatParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_OzoneLayerWidthID, passData.ozoneLayerWidth);
                    cmdEncoder.SetComputeVectorParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.Atmo_GroundAlbedoID, (Vector4)passData.groundAlbedo);

                    // Kernel 0: Transmittance LUT
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelTransmittance, AtmosphericLUTPassUtilityData.UAV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelTransmittance, Mathf.CeilToInt(passData.transmittanceLUTSize.x / 8.0f), Mathf.CeilToInt(passData.transmittanceLUTSize.y / 8.0f), 1);

                    // Kernel 1: Multi-Scattering LUT
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelMultiScattering, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetComputeTextureParam(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelMultiScattering, AtmosphericLUTPassUtilityData.UAV_MultiScatteringLUTID, passData.multiScatteringLUT);
                    cmdEncoder.DispatchCompute(passData.atmosphericLUTShader, AtmosphericLUTPassUtilityData.KernelMultiScattering, Mathf.CeilToInt(passData.multiScatteringLUTSize / 8.0f), Mathf.CeilToInt(passData.multiScatteringLUTSize / 8.0f), 1);
                });
            }
        }
    }
}
