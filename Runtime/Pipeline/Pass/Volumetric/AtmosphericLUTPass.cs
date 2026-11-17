using System;
using System.Runtime.InteropServices;
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
    // GPU-RECORD: CBV_AtmosphereParameters Shaders/Pass/Volumetric/Compute_AtmosphericLUT.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct AtmosphereConstants
    {
        public Vector4 rayleighScattering;
        public Vector4 ozoneAbsorption;
        public Vector4 groundAlbedo;
        public Vector4 sunDirection;
        public Vector4 sunIlluminance;
        public float planetRadius;
        public float atmosphereHeight;
        public float rayleighHeight;
        public float mieScattering;
        public float mieAbsorption;
        public float mieHeight;
        public float mieAnisotropy;
        public float ozoneLayerCenter;
        public float ozoneLayerWidth;
        public float brightness;
        public float multiScatterStrength;
        public float drawGround;
        public float farDepth;
        public float aerialPerspectiveDistance;
        public float sunAngle;
        public int cubemapSize;
        public int shPartialCount;
        public int padding0;
        public int padding1;
        public int padding2;
    }

    internal static class AtmosphericLUTPassUtilityData
    {
        internal static readonly ProfilingSampler AtmoTransmittanceSampler = new ProfilingSampler("AtmoTransmittance");
        internal static readonly ProfilingSampler AtmoMultiScatterSampler = new ProfilingSampler("AtmoMultiScatter");
        internal static readonly ProfilingSampler AtmoSkyViewSampler = new ProfilingSampler("AtmoSkyView");
        internal static readonly ProfilingSampler AtmoAerialSampler = new ProfilingSampler("AtmoAerial");
        internal static readonly ProfilingSampler AtmoSunSampler = new ProfilingSampler("AtmoSun");
        internal static readonly ProfilingSampler AtmoCubemapSampler = new ProfilingSampler("AtmoCubemap");
        internal static readonly ProfilingSampler AtmoSHProjectSampler = new ProfilingSampler("AtmoSHProject");
        internal static readonly ProfilingSampler AtmoSHReduceSampler = new ProfilingSampler("AtmoSHReduce");
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
        internal static readonly int CBV_AtmosphereParametersID = Shader.PropertyToID("CBV_AtmosphereParameters");
        internal static readonly int UAV_TransmittanceLUTID = Shader.PropertyToID("UAV_TransmittanceLUT");
        internal static readonly int UAV_MultiScatteringLUTID = Shader.PropertyToID("UAV_MultiScatteringLUT");
        internal static readonly int UAV_SkyViewLUTID = Shader.PropertyToID("UAV_SkyViewLUT");
        internal static readonly int UAV_AerialPerspectiveLUTID = Shader.PropertyToID("UAV_AerialPerspectiveLUT");
        internal static readonly int UAV_AtmosphereCubemapID = Shader.PropertyToID("UAV_AtmosphereCubemap");
        internal static readonly int UAV_SunBufferID = Shader.PropertyToID("UAV_SunBuffer");
        internal static readonly int UAV_SHPartialID = Shader.PropertyToID("UAV_SHPartial");
        internal static readonly int UAV_SHCoefficientsID = Shader.PropertyToID("UAV_SHCoefficients");
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
            public RGConstantsHandle parametersHandle;
            public int generateShared;
            public int generateView;
            public int generateIBL;
            public int ggxMipCount;
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
            public RGTextureHandle transmittanceLUTHandle;
            public RGTextureHandle multiScatteringLUTHandle;
            public RGTextureHandle skyViewLUTHandle;
            public RGTextureHandle aerialPerspectiveLUTHandle;
            public RGTextureHandle atmosphereCubemapHandle;
            public RGTextureHandle ggxPrefilterHandle;
            public RGBufferHandle sunBufferHandle;
            public RGBufferHandle shCoefficientsHandle;
            public RGBufferHandle shPartialHandle;
        }

        static TextureDescriptor CreateAtmosphereLUTDescriptor(int width, int height, string name)
            => TextureDescriptor.Create2D(name, width, height, GraphicsFormat.R16G16B16A16_SFloat, ETextureSizingDomain.Exact);

        static TextureDescriptor CreateAtmosphereCubemapDescriptor(int size, string name, bool useMips)
        {
            TextureDescriptor descriptor = TextureDescriptor.Create2D(name, size, size, GraphicsFormat.R16G16B16A16_SFloat, ETextureSizingDomain.Exact);
            descriptor.dimension = TextureDimension.Tex2DArray;
            descriptor.slices = 6;
            descriptor.useMipMap = useMips;
            descriptor.filterMode = useMips ? FilterMode.Trilinear : FilterMode.Bilinear;
            return descriptor;
        }

        static AtmosphereConstants CreateAtmosphereConstants(AtmosphereParameter parameter, Vector4 sunDirection, Vector4 sunIlluminance)
        {
            return new AtmosphereConstants
            {
                rayleighScattering = parameter.RayleighScatteringPerMeter,
                ozoneAbsorption = parameter.OzoneAbsorptionPerMeter,
                groundAlbedo = (Vector4)parameter.groundAlbedo,
                sunDirection = sunDirection,
                sunIlluminance = sunIlluminance,
                planetRadius = parameter.planetRadius,
                atmosphereHeight = parameter.atmosphereHeight,
                rayleighHeight = parameter.rayleighHeight,
                mieScattering = parameter.MieScatteringPerMeter,
                mieAbsorption = parameter.MieAbsorptionPerMeter,
                mieHeight = parameter.mieHeight,
                mieAnisotropy = parameter.mieAnisotropy,
                ozoneLayerCenter = parameter.ozoneLayerCenter,
                ozoneLayerWidth = parameter.ozoneLayerWidth,
                brightness = parameter.brightness,
                multiScatterStrength = parameter.multiScatterStrength,
                drawGround = parameter.drawGround ? 1.0f : 0.0f,
                farDepth = GraphicsConventions.SampledFarDepth,
                aerialPerspectiveDistance = parameter.aerialPerspectiveDistance,
                sunAngle = parameter.sunAngle,
                cubemapSize = parameter.cubemapSize
            };
        }

        // One observer for the pipeline's lifetime; each record rewrites its fields before the graph queues it.
        readonly AtmosphereProduction m_AtmosphereProduction = new AtmosphereProduction();

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

        void ComputeAtmosphericLUT(in CameraRecordContext context)
        {
            AtmosphereParameter parameter = context.atmosphere;

            LightContext.ResolveSun(renderContext.sceneWorld.lights.context, out Vector4 sunDirection, out Vector4 sunIlluminance);
            // The key quantizes the position the view LUT kernels read from ViewUniform.
            AtmosphereViewKey viewKey = AtmosphereViewKey.Create(parameter, (Vector3)sunDirection, (Vector3)sunIlluminance, context.cameraUniform.matrix_ViewToWorld.GetColumn(3),
                context.cameraUniform.matrix_WorldToView, context.cameraUniform.matrix_FlipYProj);
            AtmosphereIBLKey iblKey = AtmosphereIBLKey.Create(parameter, (Vector3)sunDirection, (Vector3)sunIlluminance);

            TextureDescriptor transmittanceDsc = CreateAtmosphereLUTDescriptor(parameter.transmittanceLUTWidth, parameter.transmittanceLUTHeight, AtmosphericLUTPassUtilityData.TransmittanceName);
            TextureDescriptor multiScatterDsc = CreateAtmosphereLUTDescriptor(parameter.multiScatteringLUTSize, parameter.multiScatteringLUTSize, AtmosphericLUTPassUtilityData.MultiScatteringName);
            TextureDescriptor skyViewDsc = CreateAtmosphereLUTDescriptor(parameter.skyViewLUTWidth, parameter.skyViewLUTHeight, AtmosphericLUTPassUtilityData.SkyViewName);
            TextureDescriptor aerialDsc = new TextureDescriptor(parameter.aerialPerspectiveSize, parameter.aerialPerspectiveSize, parameter.aerialPerspectiveSize);
            aerialDsc.name = AtmosphericLUTPassUtilityData.AerialPerspectiveName;
            aerialDsc.dimension = TextureDimension.Tex3D;
            aerialDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            aerialDsc.enableRandomWrite = true;
            aerialDsc.filterMode = FilterMode.Trilinear;
            aerialDsc.wrapMode = TextureWrapMode.Clamp;
            TextureDescriptor cubemapDsc = CreateAtmosphereCubemapDescriptor(parameter.cubemapSize, AtmosphericLUTPassUtilityData.CubemapName, false);
            TextureDescriptor prefilterDsc = CreateAtmosphereCubemapDescriptor(parameter.cubemapSize, AtmosphericLUTPassUtilityData.GGXPrefilterName, true);
            BufferDescriptor sunDsc = new BufferDescriptor(1, sizeof(float) * 4, GraphicsBuffer.Target.Structured);
            sunDsc.name = AtmosphericLUTPassUtilityData.SunBufferName;
            BufferDescriptor shDsc = new BufferDescriptor(9, sizeof(float) * 4, GraphicsBuffer.Target.Structured);
            shDsc.name = AtmosphericLUTPassUtilityData.SkySHName;

            m_AtmosphereSharedCache.ResolveShared(parameter, transmittanceDsc, multiScatterDsc, out TextureHandle transmittanceHandle, out TextureHandle multiScatterHandle, out bool sharedHit);
            context.state.atmosphereViewCache.Resolve(viewKey, skyViewDsc, aerialDsc, sunDsc, out TextureHandle skyViewHandle, out TextureHandle aerialHandle, out BufferHandle sunHandle, out bool viewHit);
            m_AtmosphereSharedCache.ResolveIBL(iblKey, cubemapDsc, prefilterDsc, shDsc, out TextureHandle cubemapHandle, out TextureHandle prefilterHandle, out BufferHandle shHandle, out bool iblHit);

            RGTextureHandle transmittanceLUTHandle = m_RGBuilder.ImportTexture(transmittanceHandle);
            RGTextureHandle multiScatteringLUTHandle = m_RGBuilder.ImportTexture(multiScatterHandle);
            RGTextureHandle skyViewLUTHandle = m_RGBuilder.ImportTexture(skyViewHandle);
            RGTextureHandle aerialPerspectiveLUTHandle = m_RGBuilder.ImportTexture(aerialHandle);
            RGTextureHandle atmosphereCubemapHandle = m_RGBuilder.ImportTexture(cubemapHandle);
            RGTextureHandle ggxPrefilterHandle = m_RGBuilder.ImportTexture(prefilterHandle);
            RGBufferHandle sunBufferHandle = m_RGBuilder.ImportBuffer(sunHandle);
            RGBufferHandle shCoefficientsHandle = m_RGBuilder.ImportBuffer(shHandle);

            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT, transmittanceLUTHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.AtmosphereMultiScatteringLUT, multiScatteringLUTHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, skyViewLUTHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, aerialPerspectiveLUTHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.AtmosphereCubemap, atmosphereCubemapHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, ggxPrefilterHandle);
            m_RGBuilder.scoper.RegisterBuffer(InfinityShaderIDs.AtmosphereSunBuffer, sunBufferHandle);
            m_RGBuilder.scoper.RegisterBuffer(InfinityShaderIDs.AtmosphereSkySH, shCoefficientsHandle);

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
            RGBufferHandle shPartialHandle = default;
            if (generateIBL)
            {
                BufferDescriptor partialDsc = new BufferDescriptor(Mathf.Max(1, shPartialCount * 9), sizeof(float) * 4, GraphicsBuffer.Target.Structured);
                partialDsc.name = AtmosphericLUTPassUtilityData.SHPartialName;
                shPartialHandle = m_RGBuilder.CreateBuffer(partialDsc);
            }

            {
                RGComputePassHandle<AtmosphericLUTPassData> passHandle = m_RGBuilder.AddComputePass<AtmosphericLUTPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeAtmosphericLUT));
                m_AtmosphereProduction.shared = m_AtmosphereSharedCache;
                m_AtmosphereProduction.view = context.state.atmosphereViewCache;
                m_AtmosphereProduction.generateShared = generateShared;
                m_AtmosphereProduction.generateView = generateView;
                m_AtmosphereProduction.generateIBL = generateIBL;
                passHandle.SetQueueObserver(m_AtmosphereProduction);
                ref AtmosphericLUTPassData passData = ref passHandle.GetPassData();
                passData.parameter = parameter;
                AtmosphereConstants constants = CreateAtmosphereConstants(parameter, sunDirection, sunIlluminance);
                constants.shPartialCount = shPartialCount;
                passData.parametersHandle = passHandle.WriteConstants(constants);
                passData.generateShared = generateShared ? 1 : 0;
                passData.generateView = generateView ? 1 : 0;
                passData.generateIBL = generateIBL ? 1 : 0;
                passData.ggxMipCount = ggxMipCount;
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
                    passData.transmittanceLUTHandle = passHandle.WriteTexture(transmittanceLUTHandle);
                    passData.multiScatteringLUTHandle = passHandle.WriteTexture(multiScatteringLUTHandle);
                }
                else
                {
                    passData.transmittanceLUTHandle = passHandle.ReadTexture(transmittanceLUTHandle);
                    passData.multiScatteringLUTHandle = passHandle.ReadTexture(multiScatteringLUTHandle);
                }

                if (generateView)
                {
                    passData.skyViewLUTHandle = passHandle.WriteTexture(skyViewLUTHandle);
                    passData.aerialPerspectiveLUTHandle = passHandle.WriteTexture(aerialPerspectiveLUTHandle);
                    passData.sunBufferHandle = passHandle.WriteBuffer(sunBufferHandle);
                }

                if (generateIBL)
                {
                    passData.atmosphereCubemapHandle = passHandle.WriteTexture(atmosphereCubemapHandle);
                    passData.ggxPrefilterHandle = passHandle.WriteTexture(ggxPrefilterHandle);
                    passData.shCoefficientsHandle = passHandle.WriteBuffer(shCoefficientsHandle);
                    passData.shPartialHandle = passHandle.WriteBuffer(shPartialHandle);
                }

                passHandle.EnablePassCulling(false);
                passHandle.EnableAsyncCompute(true);
                passHandle.SetExecuteFunc((in AtmosphericLUTPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.atmosphericLUTShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, AtmosphericLUTPassUtilityData.CBV_AtmosphereParametersID, passData.parametersHandle);

                    if (passData.generateShared != 0)
                    {
                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoTransmittanceSampler);
                        cmdEncoder.SetComputeTextureParam(shader, passData.transmittanceKernel.index, AtmosphericLUTPassUtilityData.UAV_TransmittanceLUTID, passData.transmittanceLUTHandle);
                        cmdEncoder.DispatchCompute(shader, passData.transmittanceKernel.index, passData.transmittanceKernel.GroupsX(passData.parameter.transmittanceLUTWidth), passData.transmittanceKernel.GroupsY(passData.parameter.transmittanceLUTHeight), 1);
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoTransmittanceSampler);

                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoMultiScatterSampler);
                        cmdEncoder.SetComputeTextureParam(shader, passData.multiScatteringKernel.index, PassShaderIDs.SRV_TransmittanceLUT, passData.transmittanceLUTHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.multiScatteringKernel.index, AtmosphericLUTPassUtilityData.UAV_MultiScatteringLUTID, passData.multiScatteringLUTHandle);
                        cmdEncoder.DispatchCompute(shader, passData.multiScatteringKernel.index, passData.multiScatteringKernel.GroupsX(passData.parameter.multiScatteringLUTSize), passData.multiScatteringKernel.GroupsY(passData.parameter.multiScatteringLUTSize), 1);
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoMultiScatterSampler);
                    }

                    if (passData.generateView != 0)
                    {
                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoSkyViewSampler);
                        cmdEncoder.SetComputeTextureParam(shader, passData.skyViewKernel.index, PassShaderIDs.SRV_TransmittanceLUT, passData.transmittanceLUTHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.skyViewKernel.index, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUTHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.skyViewKernel.index, AtmosphericLUTPassUtilityData.UAV_SkyViewLUTID, passData.skyViewLUTHandle);
                        cmdEncoder.DispatchCompute(shader, passData.skyViewKernel.index, passData.skyViewKernel.GroupsX(passData.parameter.skyViewLUTWidth), passData.skyViewKernel.GroupsY(passData.parameter.skyViewLUTHeight), 1);
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoSkyViewSampler);

                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoAerialSampler);
                        cmdEncoder.SetComputeTextureParam(shader, passData.aerialPerspectiveKernel.index, PassShaderIDs.SRV_TransmittanceLUT, passData.transmittanceLUTHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.aerialPerspectiveKernel.index, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUTHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.aerialPerspectiveKernel.index, AtmosphericLUTPassUtilityData.UAV_AerialPerspectiveLUTID, passData.aerialPerspectiveLUTHandle);
                        cmdEncoder.DispatchCompute(shader, passData.aerialPerspectiveKernel.index, passData.aerialPerspectiveKernel.GroupsX(passData.parameter.aerialPerspectiveSize), passData.aerialPerspectiveKernel.GroupsY(passData.parameter.aerialPerspectiveSize), passData.aerialPerspectiveKernel.GroupsZ(passData.parameter.aerialPerspectiveSize));
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoAerialSampler);

                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoSunSampler);
                        cmdEncoder.SetComputeTextureParam(shader, passData.sunBufferKernel.index, PassShaderIDs.SRV_TransmittanceLUT, passData.transmittanceLUTHandle);
                        cmdEncoder.SetComputeBufferParam(shader, passData.sunBufferKernel.index, AtmosphericLUTPassUtilityData.UAV_SunBufferID, passData.sunBufferHandle);
                        cmdEncoder.DispatchCompute(shader, passData.sunBufferKernel.index, 1, 1, 1);
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoSunSampler);
                    }

                    if (passData.generateIBL != 0)
                    {
                        int cubemapGroups = passData.cubemapKernel.GroupsX(passData.parameter.cubemapSize);
                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoCubemapSampler);
                        cmdEncoder.SetComputeTextureParam(shader, passData.cubemapKernel.index, PassShaderIDs.SRV_TransmittanceLUT, passData.transmittanceLUTHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.cubemapKernel.index, AtmosphericLUTPassUtilityData.SRV_MultiScatteringLUTID, passData.multiScatteringLUTHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.cubemapKernel.index, AtmosphericLUTPassUtilityData.UAV_AtmosphereCubemapID, passData.atmosphereCubemapHandle);
                        cmdEncoder.DispatchCompute(shader, passData.cubemapKernel.index, cubemapGroups, cubemapGroups, 6);
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoCubemapSampler);

                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoSHProjectSampler);
                        cmdEncoder.SetComputeTextureParam(shader, passData.shProjectKernel.index, AtmosphericLUTPassUtilityData.SRV_AtmosphereCubemapID, passData.atmosphereCubemapHandle);
                        cmdEncoder.SetComputeBufferParam(shader, passData.shProjectKernel.index, AtmosphericLUTPassUtilityData.UAV_SHPartialID, passData.shPartialHandle);
                        cmdEncoder.DispatchCompute(shader, passData.shProjectKernel.index, cubemapGroups, cubemapGroups, 6);
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoSHProjectSampler);

                        cmdEncoder.BeginSample(AtmosphericLUTPassUtilityData.AtmoSHReduceSampler);
                        cmdEncoder.SetComputeBufferParam(shader, passData.shReduceKernel.index, AtmosphericLUTPassUtilityData.UAV_SHPartialID, passData.shPartialHandle);
                        cmdEncoder.SetComputeBufferParam(shader, passData.shReduceKernel.index, AtmosphericLUTPassUtilityData.UAV_SHCoefficientsID, passData.shCoefficientsHandle);
                        cmdEncoder.DispatchCompute(shader, passData.shReduceKernel.index, 1, 1, 1);
                        cmdEncoder.EndSample(AtmosphericLUTPassUtilityData.AtmoSHReduceSampler);

                        for (int mip = 0; mip < passData.ggxMipCount; ++mip)
                        {
                            ProfilingSampler ggxMarker = AtmosphericLUTPassUtilityData.GgxMipSampler(mip);
                            cmdEncoder.BeginSample(ggxMarker);
                            int mipSize = Mathf.Max(1, passData.parameter.cubemapSize >> mip);
                            float roughness = passData.ggxMipCount > 1 ? mip / (float)(passData.ggxMipCount - 1) : 0.0f;
                            CubeFilterUtilityData.FilterArrayToArray(cmdEncoder, passData.filterArrayToArrayKernel, passData.atmosphereCubemapHandle, passData.ggxPrefilterHandle,
                                passData.parameter.cubemapSize, mip, roughness, 0, 32);
                            cmdEncoder.EndSample(ggxMarker);
                        }
                    }
                });
            }
        }
    }
}
