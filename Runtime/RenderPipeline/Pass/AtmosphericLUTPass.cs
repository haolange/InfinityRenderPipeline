using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AtmosphericLUTPassUtilityData
    {
        internal static string TransmittanceName = "AtmosphereTransmittanceLUT";
        internal static string MultiScatteringName = "AtmosphereMultiScatteringLUT";
        internal static string SkyViewName = "AtmosphereSkyViewLUT";
        internal static string AerialPerspectiveName = "AtmosphereAerialPerspectiveLUT";
        internal static string CubemapName = "AtmosphereCubemap";
        internal static string GGXPrefilterName = "AtmosphereGGXPrefilter";
        internal static string SunBufferName = "AtmosphereSunBuffer";
        internal static string SkySHName = "AtmosphereSkySH";
        internal static string SHPartialName = "AtmosphereSHPartial";
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
        internal static int Atmo_CubemapSizeID = Shader.PropertyToID("Atmo_CubemapSize");
        internal static int Atmo_SHPartialCountID = Shader.PropertyToID("Atmo_SHPartialCount");
        internal static int Atmo_PrefilterMipID = Shader.PropertyToID("Atmo_PrefilterMip");
        internal static int Atmo_PrefilterRoughnessID = Shader.PropertyToID("Atmo_PrefilterRoughness");
        internal static int UAV_TransmittanceLUTID = Shader.PropertyToID("UAV_TransmittanceLUT");
        internal static int UAV_MultiScatteringLUTID = Shader.PropertyToID("UAV_MultiScatteringLUT");
        internal static int UAV_SkyViewLUTID = Shader.PropertyToID("UAV_SkyViewLUT");
        internal static int UAV_AerialPerspectiveLUTID = Shader.PropertyToID("UAV_AerialPerspectiveLUT");
        internal static int UAV_AtmosphereCubemapID = Shader.PropertyToID("UAV_AtmosphereCubemap");
        internal static int UAV_GGXPrefilterID = Shader.PropertyToID("UAV_GGXPrefilter");
        internal static int UAV_SunBufferID = Shader.PropertyToID("UAV_SunBuffer");
        internal static int UAV_SHPartialID = Shader.PropertyToID("UAV_SHPartial");
        internal static int UAV_SHCoefficientsID = Shader.PropertyToID("UAV_SHCoefficients");
        internal static int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static int SRV_MultiScatteringLUTID = Shader.PropertyToID("SRV_MultiScatteringLUT");
        internal static int SRV_AtmosphereCubemapID = Shader.PropertyToID("SRV_AtmosphereCubemap");
        internal static int KernelTransmittance = 0;
        internal static int KernelMultiScattering = 1;
        internal static int KernelSkyView = 2;
        internal static int KernelAerialPerspective = 3;
        internal static int KernelCubemap = 4;
        internal static int KernelSunBuffer = 5;
        internal static int KernelSHProject = 7;
        internal static int KernelSHReduce = 8;
        internal static int KernelGGXPrefilter = 9;

        internal static int GGXMipCount(int cubemapSize)
        {
            int mipCount = 1;
            int size = Mathf.Max(cubemapSize, 1);
            while (size > 1)
            {
                size >>= 1;
                mipCount++;
            }

            return mipCount;
        }
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
            public int generateShared;
            public int generateView;
            public int generateIBL;
            public int ggxMipCount;
            public int shPartialCount;
            public ComputeShader atmosphericLUTShader;
            public RGTextureRef transmittanceLUT;
            public RGTextureRef multiScatteringLUT;
            public RGTextureRef skyViewLUT;
            public RGTextureRef aerialPerspectiveLUT;
            public RGTextureRef atmosphereCubemap;
            public RGTextureRef ggxPrefilter;
            public RGBufferRef sunBuffer;
            public RGBufferRef shCoefficients;
            public RGBufferRef shPartial;
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

        static TextureDescriptor CreateAtmosphereCubemapDescriptor(int size, string name, bool useMips)
        {
            TextureDescriptor descriptor = new TextureDescriptor(size, size, 6);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex2DArray;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = useMips;
            descriptor.autoGenerateMips = false;
            descriptor.filterMode = useMips ? FilterMode.Trilinear : FilterMode.Bilinear;
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
            cmdEncoder.SetComputeIntParam(shader, AtmosphericLUTPassUtilityData.Atmo_CubemapSizeID, parameter.cubemapSize);
            cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
            cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
        }

        void ComputeAtmosphericLUT(RenderContext renderContext, Camera camera, CommandBuffer cmdBuffer)
        {
            if (pipelineAsset.atmosphericalProfile == null)
            {
                throw new InvalidOperationException("InfinityRP: AtmosphericalProfile is required. Atmosphere lives only on the profile.");
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.atmosphericLUTShader,
                "TransmittanceLUT", "MultiScatteringLUT", "SkyViewLUT", "AerialPerspectiveLUT",
                "AtmosphereCubemap", "SunBuffer", "AtmosphereComposite",
                "AtmosphereSHProject", "AtmosphereSHReduce", "AtmosphereGGXPrefilter"))
            {
                throw new InvalidOperationException("InfinityRP: Atmosphere is designed on but atmosphericLUTShader is missing a required kernel.");
            }

            AtmosphereParameter parameter = AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile);
            parameter.ThrowIfInvalid();

            LightContext.ResolveSun(renderContext.lightContext, out Vector4 sunDirection, out Vector4 sunIlluminance);
            Vector3 cameraPosition = camera.transform.position;
            AtmosphereViewKey viewKey = AtmosphereViewKey.Create(parameter, (Vector3)sunDirection, cameraPosition);
            AtmosphereIBLKey iblKey = AtmosphereIBLKey.Create(parameter, (Vector3)sunDirection);

            TextureDescriptor transmittanceDsc = CreateAtmosphereLUTDescriptor(parameter.transmittanceLUTWidth, parameter.transmittanceLUTHeight, AtmosphericLUTPassUtilityData.TransmittanceName);
            TextureDescriptor multiScatterDsc = CreateAtmosphereLUTDescriptor(parameter.multiScatteringLUTSize, parameter.multiScatteringLUTSize, AtmosphericLUTPassUtilityData.MultiScatteringName);
            TextureDescriptor skyViewDsc = CreateAtmosphereLUTDescriptor(parameter.skyViewLUTWidth, parameter.skyViewLUTHeight, AtmosphericLUTPassUtilityData.SkyViewName);
            TextureDescriptor aerialDsc = new TextureDescriptor(parameter.aerialPerspectiveSize, parameter.aerialPerspectiveSize, parameter.aerialPerspectiveSize);
            aerialDsc.name = AtmosphericLUTPassUtilityData.AerialPerspectiveName;
            aerialDsc.dimension = TextureDimension.Tex3D;
            aerialDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            aerialDsc.depthBufferBits = EDepthBits.None;
            aerialDsc.enableRandomWrite = true;
            aerialDsc.filterMode = FilterMode.Trilinear;
            aerialDsc.wrapMode = TextureWrapMode.Clamp;
            TextureDescriptor cubemapDsc = CreateAtmosphereCubemapDescriptor(parameter.cubemapSize, AtmosphericLUTPassUtilityData.CubemapName, false);
            TextureDescriptor prefilterDsc = CreateAtmosphereCubemapDescriptor(parameter.cubemapSize, AtmosphericLUTPassUtilityData.GGXPrefilterName, true);
            BufferDescriptor sunDsc = new BufferDescriptor(1, sizeof(float) * 4, ComputeBufferType.Structured);
            sunDsc.name = AtmosphericLUTPassUtilityData.SunBufferName;
            BufferDescriptor shDsc = new BufferDescriptor(9, sizeof(float) * 4, ComputeBufferType.Structured);
            shDsc.name = AtmosphericLUTPassUtilityData.SkySHName;

            m_AtmosphereSharedCache.ResolveShared(parameter, transmittanceDsc, multiScatterDsc, out FTextureRef transmittanceHandle, out FTextureRef multiScatterHandle, out bool sharedHit);
            m_ActiveFrameState.atmosphereViewCache.Resolve(viewKey, skyViewDsc, aerialDsc, sunDsc, out FTextureRef skyViewHandle, out FTextureRef aerialHandle, out FBufferRef sunHandle, out bool viewHit);
            m_AtmosphereSharedCache.ResolveIBL(iblKey, cubemapDsc, prefilterDsc, shDsc, out FTextureRef cubemapHandle, out FTextureRef prefilterHandle, out FBufferRef shHandle, out bool iblHit);

            RGTextureRef transmittanceLUT = m_RGBuilder.ImportTexture(transmittanceHandle);
            RGTextureRef multiScatteringLUT = m_RGBuilder.ImportTexture(multiScatterHandle);
            RGTextureRef skyViewLUT = m_RGBuilder.ImportTexture(skyViewHandle);
            RGTextureRef aerialPerspectiveLUT = m_RGBuilder.ImportTexture(aerialHandle);
            RGTextureRef atmosphereCubemap = m_RGBuilder.ImportTexture(cubemapHandle);
            RGTextureRef ggxPrefilter = m_RGBuilder.ImportTexture(prefilterHandle);
            RGBufferRef sunBuffer = m_RGBuilder.ImportBuffer(sunHandle);
            RGBufferRef shCoefficients = m_RGBuilder.ImportBuffer(shHandle);

            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, transmittanceLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, multiScatteringLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, skyViewLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, aerialPerspectiveLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereCubemap, atmosphereCubemap);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, ggxPrefilter);
            m_RGScoper.RegisterBuffer(InfinityShaderIDs.AtmosphereSunBuffer, sunBuffer);
            m_RGScoper.RegisterBuffer(InfinityShaderIDs.AtmosphereSkySH, shCoefficients);

            int ggxMipCount = AtmosphericLUTPassUtilityData.GGXMipCount(parameter.cubemapSize);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, transmittanceHandle.texture);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, multiScatterHandle.texture);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, skyViewHandle.texture);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, aerialHandle.texture);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.AtmosphereCubemap, cubemapHandle.texture);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, prefilterHandle.texture);
            cmdBuffer.SetGlobalBuffer(InfinityShaderIDs.AtmosphereSunBuffer, sunHandle.buffer);
            cmdBuffer.SetGlobalBuffer(InfinityShaderIDs.AtmosphereSkySH, shHandle.buffer);
            cmdBuffer.SetGlobalFloat(InfinityShaderIDs.AtmosphereIBLMaxMip, ggxMipCount - 1);

            bool generateShared = !sharedHit;
            bool generateView = !viewHit;
            bool generateIBL = !iblHit;
            if (!generateShared && !generateView && !generateIBL)
            {
                return;
            }

            int groupsX = Mathf.CeilToInt(parameter.cubemapSize / 8.0f);
            int shPartialCount = groupsX * groupsX * 6;
            RGBufferRef shPartial = default;
            if (generateIBL)
            {
                BufferDescriptor partialDsc = new BufferDescriptor(Mathf.Max(1, shPartialCount * 9), sizeof(float) * 4, ComputeBufferType.Structured);
                partialDsc.name = AtmosphericLUTPassUtilityData.SHPartialName;
                shPartial = m_RGBuilder.CreateBuffer(partialDsc);
            }

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<AtmosphericLUTPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericLUT)))
            {
                ref AtmosphericLUTPassData passData = ref passRef.GetPassData<AtmosphericLUTPassData>();
                passData.parameter = parameter;
                passData.sunDirection = sunDirection;
                passData.sunIlluminance = sunIlluminance;
                passData.worldSpaceCameraPos = cameraPosition;
                passData.matrix_InvViewProj = GraphicsUtility.GetComputeInvViewProj(camera);
                passData.generateShared = generateShared ? 1 : 0;
                passData.generateView = generateView ? 1 : 0;
                passData.generateIBL = generateIBL ? 1 : 0;
                passData.ggxMipCount = ggxMipCount;
                passData.shPartialCount = shPartialCount;
                passData.atmosphericLUTShader = pipelineAsset.atmosphericLUTShader;

                if (generateShared)
                {
                    passData.transmittanceLUT = passRef.WriteTexture(transmittanceLUT);
                    passData.multiScatteringLUT = passRef.WriteTexture(multiScatteringLUT);
                    m_AtmosphereSharedCache.MarkSharedProduced();
                }
                else
                {
                    passData.transmittanceLUT = passRef.ReadTexture(transmittanceLUT);
                    passData.multiScatteringLUT = passRef.ReadTexture(multiScatteringLUT);
                }

                if (generateView)
                {
                    passData.skyViewLUT = passRef.WriteTexture(skyViewLUT);
                    passData.aerialPerspectiveLUT = passRef.WriteTexture(aerialPerspectiveLUT);
                    passData.sunBuffer = passRef.WriteBuffer(sunBuffer);
                    m_ActiveFrameState.atmosphereViewCache.MarkProduced();
                }

                if (generateIBL)
                {
                    passData.atmosphereCubemap = passRef.WriteTexture(atmosphereCubemap);
                    passData.ggxPrefilter = passRef.WriteTexture(ggxPrefilter);
                    passData.shCoefficients = passRef.WriteBuffer(shCoefficients);
                    passData.shPartial = passRef.WriteBuffer(shPartial);
                    m_AtmosphereSharedCache.MarkIBLProduced();
                }

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in AtmosphericLUTPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    BindAtmosphereParameters(passData, cmdEncoder);
                    ComputeShader shader = passData.atmosphericLUTShader;

                    if (passData.generateShared != 0)
                    {
                        cmdEncoder.BeginSample("AtmoTransmittance");
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelTransmittance, AtmosphericLUTPassUtilityData.UAV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelTransmittance, Mathf.CeilToInt(passData.parameter.transmittanceLUTWidth / 8.0f), Mathf.CeilToInt(passData.parameter.transmittanceLUTHeight / 8.0f), 1);
                        cmdEncoder.EndSample("AtmoTransmittance");

                        cmdEncoder.BeginSample("AtmoMultiScatter");
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelMultiScattering, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelMultiScattering, AtmosphericLUTPassUtilityData.UAV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelMultiScattering, Mathf.CeilToInt(passData.parameter.multiScatteringLUTSize / 8.0f), Mathf.CeilToInt(passData.parameter.multiScatteringLUTSize / 8.0f), 1);
                        cmdEncoder.EndSample("AtmoMultiScatter");
                    }

                    if (passData.generateView != 0)
                    {
                        cmdEncoder.BeginSample("AtmoSkyView");
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelSkyView, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelSkyView, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelSkyView, AtmosphericLUTPassUtilityData.UAV_SkyViewLUTID, passData.skyViewLUT);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelSkyView, Mathf.CeilToInt(passData.parameter.skyViewLUTWidth / 8.0f), Mathf.CeilToInt(passData.parameter.skyViewLUTHeight / 8.0f), 1);
                        cmdEncoder.EndSample("AtmoSkyView");

                        cmdEncoder.BeginSample("AtmoAerial");
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, AtmosphericLUTPassUtilityData.UAV_AerialPerspectiveLUTID, passData.aerialPerspectiveLUT);
                        int aerialGroups = Mathf.CeilToInt(passData.parameter.aerialPerspectiveSize / 4.0f);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelAerialPerspective, aerialGroups, aerialGroups, aerialGroups);
                        cmdEncoder.EndSample("AtmoAerial");

                        cmdEncoder.BeginSample("AtmoSun");
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelSunBuffer, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeBufferParam(shader, AtmosphericLUTPassUtilityData.KernelSunBuffer, AtmosphericLUTPassUtilityData.UAV_SunBufferID, passData.sunBuffer);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelSunBuffer, 1, 1, 1);
                        cmdEncoder.EndSample("AtmoSun");
                    }

                    if (passData.generateIBL != 0)
                    {
                        int cubemapGroups = Mathf.CeilToInt(passData.parameter.cubemapSize / 8.0f);
                        cmdEncoder.BeginSample("AtmoCubemap");
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelCubemap, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelCubemap, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelCubemap, AtmosphericLUTPassUtilityData.UAV_AtmosphereCubemapID, passData.atmosphereCubemap);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelCubemap, cubemapGroups, cubemapGroups, 6);
                        cmdEncoder.EndSample("AtmoCubemap");

                        cmdEncoder.BeginSample("AtmoSHProject");
                        cmdEncoder.SetComputeIntParam(shader, AtmosphericLUTPassUtilityData.Atmo_SHPartialCountID, passData.shPartialCount);
                        cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelSHProject, AtmosphericLUTPassUtilityData.SRV_AtmosphereCubemapID, passData.atmosphereCubemap);
                        cmdEncoder.SetComputeBufferParam(shader, AtmosphericLUTPassUtilityData.KernelSHProject, AtmosphericLUTPassUtilityData.UAV_SHPartialID, passData.shPartial);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelSHProject, cubemapGroups, cubemapGroups, 6);
                        cmdEncoder.EndSample("AtmoSHProject");

                        cmdEncoder.BeginSample("AtmoSHReduce");
                        cmdEncoder.SetComputeBufferParam(shader, AtmosphericLUTPassUtilityData.KernelSHReduce, AtmosphericLUTPassUtilityData.UAV_SHPartialID, passData.shPartial);
                        cmdEncoder.SetComputeBufferParam(shader, AtmosphericLUTPassUtilityData.KernelSHReduce, AtmosphericLUTPassUtilityData.UAV_SHCoefficientsID, passData.shCoefficients);
                        cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelSHReduce, 1, 1, 1);
                        cmdEncoder.EndSample("AtmoSHReduce");

                        for (int mip = 0; mip < passData.ggxMipCount; ++mip)
                        {
                            string ggxMarker = $"AtmoGGXMip{mip}";
                            cmdEncoder.BeginSample(ggxMarker);
                            int mipSize = Mathf.Max(1, passData.parameter.cubemapSize >> mip);
                            float roughness = passData.ggxMipCount > 1 ? mip / (float)(passData.ggxMipCount - 1) : 0.0f;
                            cmdEncoder.SetComputeIntParam(shader, AtmosphericLUTPassUtilityData.Atmo_PrefilterMipID, mip);
                            cmdEncoder.SetComputeFloatParam(shader, AtmosphericLUTPassUtilityData.Atmo_PrefilterRoughnessID, roughness);
                            cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelGGXPrefilter, AtmosphericLUTPassUtilityData.SRV_AtmosphereCubemapID, passData.atmosphereCubemap);
                            cmdEncoder.SetComputeTextureParam(shader, AtmosphericLUTPassUtilityData.KernelGGXPrefilter, AtmosphericLUTPassUtilityData.UAV_GGXPrefilterID, passData.ggxPrefilter, mip);
                            int mipGroups = Mathf.CeilToInt(mipSize / 8.0f);
                            cmdEncoder.DispatchCompute(shader, AtmosphericLUTPassUtilityData.KernelGGXPrefilter, mipGroups, mipGroups, 6);
                            cmdEncoder.EndSample(ggxMarker);
                        }
                    }
                });
            }
        }
    }
}
