using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Volume;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_VolumetricFogParameters Shaders/Pass/Volumetric/Compute_VolumetricFog.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct VolumetricFogParameters
    {
        public Vector4 screenSize;
        public Vector4 resolution;
        public Vector4 albedo;
        public float density;
        public float height;
        public float heightFalloff;
        public float anisotropy;
        public float ambientIntensity;
        public int depthSlices;
        public float maxDistance;
        public float temporalWeight;
        public int frameIndex;
        public float aerialDistance;
        public int padding0;
        public int padding1;
    }

    internal static class VolumetricFogPassUtilityData
    {
        internal static readonly int CBV_VolumetricFogParametersID = Shader.PropertyToID("CBV_VolumetricFogParameters");
        internal static readonly ProfilingSampler VolFogScatterSampler = new ProfilingSampler("VolFog_Scatter");
        internal static readonly ProfilingSampler VolFogIntegrateSampler = new ProfilingSampler("VolFog_Integrate");
        internal static readonly ProfilingSampler VolFogTemporalSampler = new ProfilingSampler("VolFog_Temporal");
        internal static readonly string TextureName = "VolumetricFogTexture";
        internal static readonly string HistoryTextureName = "HistoryVolumetricFog";
        internal static readonly int SRV_HistoryVolumetricFogID = Shader.PropertyToID("SRV_HistoryVolumetricFog");
        internal static readonly int UAV_VolumetricFogTextureID = Shader.PropertyToID("UAV_VolumetricFogTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct VolumetricFogPassData
        {
            public RGConstantsHandle parametersHandle;
            public int3 froxelResolution;
            public RGBufferHandle lightRecordBufferHandle;
            public LightLoopInputs lightLoop;
            public ShadowInputs shadows;
            public ComputeShader volumetricFogShader;
            public ComputeKernel scatterDensityKernel;
            public ComputeKernel integrateKernel;
            public ComputeKernel temporalKernel;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle volumetricFogTextureHandle;
            public RGTextureHandle historyTextureHandle;
            public RGTextureHandle skyViewLUTHandle;
            public RGTextureHandle aerialPerspectiveLUTHandle;
        }

        static TextureDescriptor CreateVolumetricFogDescriptor(int width, int height, int depthSlices, string name, bool randomWrite)
        {
            TextureDescriptor descriptor = TextureDescriptor.Create2D(name, width, height, GraphicsFormat.R16G16B16A16_SFloat, ETextureSizingDomain.Internal, 8);
            descriptor.dimension = TextureDimension.Tex3D;
            descriptor.slices = depthSlices;
            descriptor.enableRandomWrite = randomWrite;
            descriptor.filterMode = FilterMode.Trilinear;
            return descriptor;
        }

        void ComputeVolumetricFog(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricFog))
            {
                return;
            }

            var volFog = context.volumes.volumetricFog;
            if (!VolumeComponentActive(volFog))
            {
                return;
            }
            VolumetricFogSettings fog = volFog.Resolve();

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            int depthSlices = fog.depthSlices;
            int froxelWidth = Mathf.CeilToInt(width / 8.0f);
            int froxelHeight = Mathf.CeilToInt(height / 8.0f);

            TextureDescriptor volumetricFogDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, depthSlices, VolumetricFogPassUtilityData.TextureName, true);
            RGTextureHandle volumetricFogTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricFogBuffer, volumetricFogDsc);

            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            HistoryRead historyRead = context.historyCache.Read(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc, context.resetsHistory);
            RGTextureHandle historyTextureHandle = m_RGBuilder.ImportTexture(historyRead.textureHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyTextureHandle);

            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle skyViewLUTHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT);
            RGTextureHandle aerialPerspectiveLUTHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT);
            float aerialDistance = context.atmosphere.aerialPerspectiveDistance;

            {
                RGComputePassHandle<VolumetricFogPassData> passHandle = m_RGBuilder.AddComputePass<VolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricFog));
                ref VolumetricFogPassData passData = ref passHandle.GetPassData();
                passData.parametersHandle = passHandle.WriteConstants(new VolumetricFogParameters
                {
                    screenSize = new Vector4(width, height, 1.0f / width, 1.0f / height),
                    resolution = new Vector4(froxelWidth, froxelHeight, depthSlices, 0),
                    albedo = (Vector4)fog.albedo,
                    density = fog.density,
                    height = fog.height,
                    heightFalloff = fog.heightFalloff,
                    anisotropy = fog.anisotropy,
                    ambientIntensity = fog.ambientIntensity,
                    depthSlices = depthSlices,
                    maxDistance = fog.maxDistance,
                    temporalWeight = historyRead.valid ? fog.temporalWeight : 0.0f,
                    frameIndex = context.frame,
                    aerialDistance = aerialDistance
                });
                passData.froxelResolution = new int3(froxelWidth, froxelHeight, depthSlices);
                LightLoopInputs lightLoop = QueryLightLoop(context);
                lightLoop.Read(passHandle);
                passData.lightLoop = lightLoop;
                ShadowInputs shadows = QueryShadowInputs(context);
                shadows.Read(passHandle);
                passData.shadows = shadows;
                passData.lightRecordBufferHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.volumetricFogShader = shaders.volumetricFogShader;
                passData.scatterDensityKernel = kernels.fogScatterDensity;
                passData.integrateKernel = kernels.fogIntegrate;
                passData.temporalKernel = kernels.fogTemporal;
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                passData.historyTextureHandle = passHandle.ReadTexture(historyTextureHandle);
                passData.volumetricFogTextureHandle = passHandle.WriteTexture(volumetricFogTextureHandle);
                passData.skyViewLUTHandle = passHandle.ReadTexture(skyViewLUTHandle);
                passData.aerialPerspectiveLUTHandle = passHandle.ReadTexture(aerialPerspectiveLUTHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in VolumetricFogPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.volumetricFogShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, VolumetricFogPassUtilityData.CBV_VolumetricFogParametersID, passData.parametersHandle);

                    int scatterKernel = passData.scatterDensityKernel.index;
                    cmdEncoder.SetComputeBufferParam(shader, scatterKernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBufferHandle);
                    passData.lightLoop.Bind(cmdEncoder, shader, scatterKernel);
                    passData.shadows.Bind(cmdEncoder, shader, scatterKernel);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, PassShaderIDs.SRV_SkyViewLUT, passData.skyViewLUTHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, PassShaderIDs.SRV_AerialPerspectiveLUT, passData.aerialPerspectiveLUTHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTextureHandle);
                    cmdEncoder.BeginSample(VolumetricFogPassUtilityData.VolFogScatterSampler);
                    cmdEncoder.DispatchCompute(shader, passData.scatterDensityKernel.index, passData.scatterDensityKernel.GroupsX(passData.froxelResolution.x), passData.scatterDensityKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(VolumetricFogPassUtilityData.VolFogScatterSampler);

                    cmdEncoder.BeginSample(VolumetricFogPassUtilityData.VolFogIntegrateSampler);
                    cmdEncoder.SetComputeTextureParam(shader, passData.integrateKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTextureHandle);
                    cmdEncoder.DispatchCompute(shader, passData.integrateKernel.index, passData.integrateKernel.GroupsX(passData.froxelResolution.x), passData.integrateKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(VolumetricFogPassUtilityData.VolFogIntegrateSampler);

                    cmdEncoder.BeginSample(VolumetricFogPassUtilityData.VolFogTemporalSampler);
                    cmdEncoder.SetComputeTextureParam(shader, passData.temporalKernel.index, VolumetricFogPassUtilityData.SRV_HistoryVolumetricFogID, passData.historyTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.temporalKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTextureHandle);
                    cmdEncoder.DispatchCompute(shader, passData.temporalKernel.index, passData.temporalKernel.GroupsX(passData.froxelResolution.x), passData.temporalKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(VolumetricFogPassUtilityData.VolFogTemporalSampler);
                });
            }

            context.features.MarkProduced(EFrameFeature.VolumetricFog);
        }

        struct CopyHistoryVolumetricFogPassData
        {
            public RGTextureHandle sourceHandle;
            public RGTextureHandle historyHandle;
        }

        void CopyHistoryVolumetricFog(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricFog))
            {
                return;
            }
            RGTextureHandle sourceHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricFogBuffer);

            var volFog = context.volumes.volumetricFog;
            int froxelWidth = Mathf.CeilToInt(context.dimensions.internalSize.x / 8.0f);
            int froxelHeight = Mathf.CeilToInt(context.dimensions.internalSize.y / 8.0f);
            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, volFog.Resolve().depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            RGTextureHandle historyHandle = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc));
            context.historyCache.MarkProduced(InfinityShaderIDs.HistoryVolumetricFogBuffer);

            {
                RGTransferPassHandle<CopyHistoryVolumetricFogPassData> passHandle = m_RGBuilder.AddTransferPass<CopyHistoryVolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryVolumetricFog));
                passHandle.ReadTexture(sourceHandle);
                passHandle.WriteTexture(historyHandle);
                ref CopyHistoryVolumetricFogPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = sourceHandle;
                passData.historyHandle = historyHandle;
                passHandle.SetExecuteFunc((in CopyHistoryVolumetricFogPassData passData, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(passData.sourceHandle, passData.historyHandle);
                });
            }
        }
    }
}
