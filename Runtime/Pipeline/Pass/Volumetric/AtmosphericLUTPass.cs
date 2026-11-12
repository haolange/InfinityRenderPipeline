using System;
using InfinityTech.Core;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class AtmosphericLUTPassUtilityData
    {
        static readonly ProfilingSampler[] s_GgxMips = new ProfilingSampler[16];

        internal static ProfilingSampler GgxMipSampler(int mip)
        {
            if ((uint)mip >= (uint)s_GgxMips.Length)
                throw new System.ArgumentOutOfRangeException(nameof(mip));
            return s_GgxMips[mip] ?? (s_GgxMips[mip] = new ProfilingSampler("AtmoGGXMip" + mip));
        }
        internal static readonly string TransmittanceName = "AtmosphereTransmittanceLUT";
        internal static readonly string MultiScatteringName = "AtmosphereMultiScatteringLUT";
        internal static readonly string SkyViewName = "AtmosphereSkyViewLUT";
        internal static readonly string AerialPerspectiveName = "AtmosphereAerialPerspectiveLUT";
        internal static readonly string CubemapName = "AtmosphereCubemap";
        internal static readonly string GGXPrefilterName = "AtmosphereGGXPrefilter";
        internal static readonly string SunBufferName = "AtmosphereSunBuffer";
        internal static readonly string SkySHName = "AtmosphereSkySH";
        internal static readonly string SHPartialName = "AtmosphereSHPartial";
        internal static readonly int Atmo_PlanetRadiusID = Shader.PropertyToID("Atmo_PlanetRadius");
        internal static readonly int Atmo_AtmosphereHeightID = Shader.PropertyToID("Atmo_AtmosphereHeight");
        internal static readonly int Atmo_RayleighScatteringID = Shader.PropertyToID("Atmo_RayleighScattering");
        internal static readonly int Atmo_RayleighHeightID = Shader.PropertyToID("Atmo_RayleighHeight");
        internal static readonly int Atmo_MieScatteringID = Shader.PropertyToID("Atmo_MieScattering");
        internal static readonly int Atmo_MieAbsorptionID = Shader.PropertyToID("Atmo_MieAbsorption");
        internal static readonly int Atmo_MieHeightID = Shader.PropertyToID("Atmo_MieHeight");
        internal static readonly int Atmo_MieAnisotropyID = Shader.PropertyToID("Atmo_MieAnisotropy");
        internal static readonly int Atmo_OzoneAbsorptionID = Shader.PropertyToID("Atmo_OzoneAbsorption");
        internal static readonly int Atmo_OzoneLayerCenterID = Shader.PropertyToID("Atmo_OzoneLayerCenter");
        internal static readonly int Atmo_OzoneLayerWidthID = Shader.PropertyToID("Atmo_OzoneLayerWidth");
        internal static readonly int Atmo_GroundAlbedoID = Shader.PropertyToID("Atmo_GroundAlbedo");
        internal static readonly int Atmo_BrightnessID = Shader.PropertyToID("Atmo_Brightness");
        internal static readonly int Atmo_MultiScatterStrengthID = Shader.PropertyToID("Atmo_MultiScatterStrength");
        internal static readonly int Atmo_DrawGroundID = Shader.PropertyToID("Atmo_DrawGround");
        internal static readonly int Atmo_AerialPerspectiveDistanceID = Shader.PropertyToID("Atmo_AerialPerspectiveDistance");
        internal static readonly int Atmo_SunAngleID = Shader.PropertyToID("Atmo_SunAngle");
        internal static readonly int Atmo_SunDirectionID = Shader.PropertyToID("Atmo_SunDirection");
        internal static readonly int Atmo_SunIlluminanceID = Shader.PropertyToID("Atmo_SunIlluminance");
        internal static readonly int Atmo_CubemapSizeID = Shader.PropertyToID("Atmo_CubemapSize");
        internal static readonly int Atmo_SHPartialCountID = Shader.PropertyToID("Atmo_SHPartialCount");
        internal static readonly int UAV_TransmittanceLUTID = Shader.PropertyToID("UAV_TransmittanceLUT");
        internal static readonly int UAV_MultiScatteringLUTID = Shader.PropertyToID("UAV_MultiScatteringLUT");
        internal static readonly int UAV_SkyViewLUTID = Shader.PropertyToID("UAV_SkyViewLUT");
        internal static readonly int UAV_AerialPerspectiveLUTID = Shader.PropertyToID("UAV_AerialPerspectiveLUT");
        internal static readonly int UAV_AtmosphereCubemapID = Shader.PropertyToID("UAV_AtmosphereCubemap");
        internal static readonly int UAV_SunBufferID = Shader.PropertyToID("UAV_SunBuffer");
        internal static readonly int UAV_SHPartialID = Shader.PropertyToID("UAV_SHPartial");
        internal static readonly int UAV_SHCoefficientsID = Shader.PropertyToID("UAV_SHCoefficients");
        internal static readonly int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static readonly int SRV_MultiScatteringLUTID = Shader.PropertyToID("SRV_MultiScatteringLUT");
        internal static readonly int SRV_AtmosphereCubemapID = Shader.PropertyToID("SRV_AtmosphereCubemap");

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
            public ComputeKernel transmittanceKernel;
            public ComputeKernel multiScatteringKernel;
            public ComputeKernel skyViewKernel;
            public ComputeKernel aerialPerspectiveKernel;
            public ComputeKernel cubemapKernel;
            public ComputeKernel sunBufferKernel;
            public ComputeKernel shProjectKernel;
            public ComputeKernel shReduceKernel;
            public ComputeKernel filterArrayToArrayKernel;
            public RGTextureHandle transmittanceLUT;
            public RGTextureHandle multiScatteringLUT;
            public RGTextureHandle skyViewLUT;
            public RGTextureHandle aerialPerspectiveLUT;
            public RGTextureHandle atmosphereCubemap;
            public RGTextureHandle ggxPrefilter;
            public RGBufferHandle sunBuffer;
            public RGBufferHandle shCoefficients;
            public RGBufferHandle shPartial;
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

        sealed class AtmosphereProduction : IRGPassQueueObserver
        {
            public AtmosphereSharedCache shared;
            public AtmosphereViewCache view;
            public bool generateShared, generateView, generateIBL;
            public void OnQueueFailed() { }
            public void OnQueued()
            {
                if (generateShared)
                {
                    shared.MarkSharedProduced();
                }
                if (generateView)
                {
                    view.MarkProduced();
                }
                if (generateIBL)
                {
                    shared.MarkIBLProduced();
                }
            }
        }

        void ComputeAtmosphericLUT(RenderContext renderContext, Camera camera)
        {
            if (pipelineAsset.atmosphericalProfile == null)
            {
                throw new InvalidOperationException("InfinityRP: AtmosphericalProfile is required. Atmosphere lives only on the profile.");
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

            m_AtmosphereSharedCache.ResolveShared(parameter, transmittanceDsc, multiScatterDsc, out TextureHandle transmittanceHandle, out TextureHandle multiScatterHandle, out bool sharedHit);
            m_ActiveFrameState.atmosphereViewCache.Resolve(viewKey, skyViewDsc, aerialDsc, sunDsc, out TextureHandle skyViewHandle, out TextureHandle aerialHandle, out BufferHandle sunHandle, out bool viewHit);
            m_AtmosphereSharedCache.ResolveIBL(iblKey, cubemapDsc, prefilterDsc, shDsc, out TextureHandle cubemapHandle, out TextureHandle prefilterHandle, out BufferHandle shHandle, out bool iblHit);

            RGTextureHandle transmittanceLUT = m_RGBuilder.ImportTexture(transmittanceHandle);
            RGTextureHandle multiScatteringLUT = m_RGBuilder.ImportTexture(multiScatterHandle);
            RGTextureHandle skyViewLUT = m_RGBuilder.ImportTexture(skyViewHandle);
            RGTextureHandle aerialPerspectiveLUT = m_RGBuilder.ImportTexture(aerialHandle);
            RGTextureHandle atmosphereCubemap = m_RGBuilder.ImportTexture(cubemapHandle);
            RGTextureHandle ggxPrefilter = m_RGBuilder.ImportTexture(prefilterHandle);
            RGBufferHandle sunBuffer = m_RGBuilder.ImportBuffer(sunHandle);
            RGBufferHandle shCoefficients = m_RGBuilder.ImportBuffer(shHandle);

            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, transmittanceLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, multiScatteringLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, skyViewLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, aerialPerspectiveLUT);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereCubemap, atmosphereCubemap);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, ggxPrefilter);
            m_RGScoper.RegisterBuffer(InfinityShaderIDs.AtmosphereSunBuffer, sunBuffer);
            m_RGScoper.RegisterBuffer(InfinityShaderIDs.AtmosphereSkySH, shCoefficients);

            int ggxMipCount = AtmosphericLUTPassUtilityData.GGXMipCount(parameter.cubemapSize);

            bool generateShared = !sharedHit;
            bool generateView = !viewHit;
            bool generateIBL = !iblHit;
            if (!generateShared && !generateView && !generateIBL)
            {
                return;
            }

            int groupsX = kernels.atmosphereCubemap.GroupsX(parameter.cubemapSize);
            int shPartialCount = groupsX * groupsX * 6;
            RGBufferHandle shPartial = default;
            if (generateIBL)
            {
                BufferDescriptor partialDsc = new BufferDescriptor(Mathf.Max(1, shPartialCount * 9), sizeof(float) * 4, ComputeBufferType.Structured);
                partialDsc.name = AtmosphericLUTPassUtilityData.SHPartialName;
                shPartial = m_RGBuilder.CreateBuffer(partialDsc);
            }

            using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<AtmosphericLUTPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericLUT)))
            {
                passRef.SetQueueObserver(new AtmosphereProduction { shared = m_AtmosphereSharedCache,
                    view = m_ActiveFrameState.atmosphereViewCache, generateShared = generateShared,
                    generateView = generateView, generateIBL = generateIBL });
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
                passData.atmosphericLUTShader = shaders.atmosphericLUTShader;
                passData.transmittanceKernel = kernels.transmittanceLUT;
                passData.multiScatteringKernel = kernels.multiScatteringLUT;
                passData.skyViewKernel = kernels.skyViewLUT;
                passData.aerialPerspectiveKernel = kernels.aerialPerspectiveLUT;
                passData.cubemapKernel = kernels.atmosphereCubemap;
                passData.sunBufferKernel = kernels.sunBuffer;
                passData.shProjectKernel = kernels.atmosphereSHProject;
                passData.shReduceKernel = kernels.atmosphereSHReduce;
                passData.filterArrayToArrayKernel = kernels.filterArrayToArray;

                if (generateShared)
                {
                    passData.transmittanceLUT = passRef.WriteTexture(transmittanceLUT);
                    passData.multiScatteringLUT = passRef.WriteTexture(multiScatteringLUT);
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
                }

                if (generateIBL)
                {
                    passData.atmosphereCubemap = passRef.WriteTexture(atmosphereCubemap);
                    passData.ggxPrefilter = passRef.WriteTexture(ggxPrefilter);
                    passData.shCoefficients = passRef.WriteBuffer(shCoefficients);
                    passData.shPartial = passRef.WriteBuffer(shPartial);
                }

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in AtmosphericLUTPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    BindAtmosphereParameters(passData, cmdEncoder);
                    ComputeShader shader = passData.atmosphericLUTShader;

                    if (passData.generateShared != 0)
                    {
                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoTransmittance"));
                        cmdEncoder.SetComputeTextureParam(shader, passData.transmittanceKernel.index, AtmosphericLUTPassUtilityData.UAV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.DispatchCompute(shader, passData.transmittanceKernel.index, passData.transmittanceKernel.GroupsX(passData.parameter.transmittanceLUTWidth), passData.transmittanceKernel.GroupsY(passData.parameter.transmittanceLUTHeight), 1);
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoTransmittance"));

                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoMultiScatter"));
                        cmdEncoder.SetComputeTextureParam(shader, passData.multiScatteringKernel.index, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, passData.multiScatteringKernel.index, AtmosphericLUTPassUtilityData.UAV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.DispatchCompute(shader, passData.multiScatteringKernel.index, passData.multiScatteringKernel.GroupsX(passData.parameter.multiScatteringLUTSize), passData.multiScatteringKernel.GroupsY(passData.parameter.multiScatteringLUTSize), 1);
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoMultiScatter"));
                    }

                    if (passData.generateView != 0)
                    {
                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoSkyView"));
                        cmdEncoder.SetComputeTextureParam(shader, passData.skyViewKernel.index, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, passData.skyViewKernel.index, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.SetComputeTextureParam(shader, passData.skyViewKernel.index, AtmosphericLUTPassUtilityData.UAV_SkyViewLUTID, passData.skyViewLUT);
                        cmdEncoder.DispatchCompute(shader, passData.skyViewKernel.index, passData.skyViewKernel.GroupsX(passData.parameter.skyViewLUTWidth), passData.skyViewKernel.GroupsY(passData.parameter.skyViewLUTHeight), 1);
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoSkyView"));

                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoAerial"));
                        cmdEncoder.SetComputeTextureParam(shader, passData.aerialPerspectiveKernel.index, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, passData.aerialPerspectiveKernel.index, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.SetComputeTextureParam(shader, passData.aerialPerspectiveKernel.index, AtmosphericLUTPassUtilityData.UAV_AerialPerspectiveLUTID, passData.aerialPerspectiveLUT);
                        cmdEncoder.DispatchCompute(shader, passData.aerialPerspectiveKernel.index, passData.aerialPerspectiveKernel.GroupsX(passData.parameter.aerialPerspectiveSize), passData.aerialPerspectiveKernel.GroupsY(passData.parameter.aerialPerspectiveSize), passData.aerialPerspectiveKernel.GroupsZ(passData.parameter.aerialPerspectiveSize));
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoAerial"));

                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoSun"));
                        cmdEncoder.SetComputeTextureParam(shader, passData.sunBufferKernel.index, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeBufferParam(shader, passData.sunBufferKernel.index, AtmosphericLUTPassUtilityData.UAV_SunBufferID, passData.sunBuffer);
                        cmdEncoder.DispatchCompute(shader, passData.sunBufferKernel.index, 1, 1, 1);
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoSun"));
                    }

                    if (passData.generateIBL != 0)
                    {
                        int cubemapGroups = passData.cubemapKernel.GroupsX(passData.parameter.cubemapSize);
                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoCubemap"));
                        cmdEncoder.SetComputeTextureParam(shader, passData.cubemapKernel.index, AtmosphericLUTPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                        cmdEncoder.SetComputeTextureParam(shader, passData.cubemapKernel.index, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUT);
                        cmdEncoder.SetComputeTextureParam(shader, passData.cubemapKernel.index, AtmosphericLUTPassUtilityData.UAV_AtmosphereCubemapID, passData.atmosphereCubemap);
                        cmdEncoder.DispatchCompute(shader, passData.cubemapKernel.index, cubemapGroups, cubemapGroups, 6);
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoCubemap"));

                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoSHProject"));
                        cmdEncoder.SetComputeIntParam(shader, AtmosphericLUTPassUtilityData.Atmo_SHPartialCountID, passData.shPartialCount);
                        cmdEncoder.SetComputeTextureParam(shader, passData.shProjectKernel.index, AtmosphericLUTPassUtilityData.SRV_AtmosphereCubemapID, passData.atmosphereCubemap);
                        cmdEncoder.SetComputeBufferParam(shader, passData.shProjectKernel.index, AtmosphericLUTPassUtilityData.UAV_SHPartialID, passData.shPartial);
                        cmdEncoder.DispatchCompute(shader, passData.shProjectKernel.index, cubemapGroups, cubemapGroups, 6);
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoSHProject"));

                        cmdEncoder.BeginSample(CachedSampler.Get("AtmoSHReduce"));
                        cmdEncoder.SetComputeBufferParam(shader, passData.shReduceKernel.index, AtmosphericLUTPassUtilityData.UAV_SHPartialID, passData.shPartial);
                        cmdEncoder.SetComputeBufferParam(shader, passData.shReduceKernel.index, AtmosphericLUTPassUtilityData.UAV_SHCoefficientsID, passData.shCoefficients);
                        cmdEncoder.DispatchCompute(shader, passData.shReduceKernel.index, 1, 1, 1);
                        cmdEncoder.EndSample(CachedSampler.Get("AtmoSHReduce"));

                        for (int mip = 0; mip < passData.ggxMipCount; ++mip)
                        {
                            ProfilingSampler ggxMarker = AtmosphericLUTPassUtilityData.GgxMipSampler(mip);
                            cmdEncoder.BeginSample(ggxMarker);
                            int mipSize = Mathf.Max(1, passData.parameter.cubemapSize >> mip);
                            float roughness = passData.ggxMipCount > 1 ? mip / (float)(passData.ggxMipCount - 1) : 0.0f;
                            CubeFilterUtilityData.FilterArrayToArray(cmdEncoder, passData.filterArrayToArrayKernel, passData.atmosphereCubemap, passData.ggxPrefilter,
                                passData.parameter.cubemapSize, mip, roughness, 0, 32);
                            cmdEncoder.EndSample(ggxMarker);
                        }
                    }
                });
            }
        }
    }
}
