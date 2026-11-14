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
            public RGConstantsHandle parameters;
            public int3 froxelResolution;
            public RGBufferHandle lightRecordBuffer;
            public LightLoopInputs lightLoop;
            public ShadowInputs shadows;
            public ComputeShader volumetricFogShader;
            public ComputeKernel scatterDensityKernel;
            public ComputeKernel integrateKernel;
            public ComputeKernel temporalKernel;
            public RGTextureHandle depthTexture;
            public RGTextureHandle volumetricFogTexture;
            public RGTextureHandle historyTexture;
            public RGTextureHandle skyViewLUT;
            public RGTextureHandle aerialPerspectiveLUT;
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

        void ComputeVolumetricFog(in CameraRecordContext context)
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
            RGTextureHandle volumetricFogTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricFogBuffer, volumetricFogDsc);

            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            HistoryRead historyRead = context.historyCache.Read(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc, context.resetsHistory);
            RGTextureHandle historyTexture = m_RGBuilder.ImportTexture(historyRead.texture);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyTexture);

            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle skyViewLUT = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT);
            RGTextureHandle aerialPerspectiveLUT = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT);
            float aerialDistance = context.atmosphere.aerialPerspectiveDistance;

            using (var passRef = m_RGBuilder.AddComputePass<VolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricFog)))
            {
                ref VolumetricFogPassData passData = ref passRef.GetPassData();
                passData.parameters = passRef.WriteConstants(new VolumetricFogParameters
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
                    frameIndex = Time.frameCount,
                    aerialDistance = aerialDistance
                });
                passData.froxelResolution = new int3(froxelWidth, froxelHeight, depthSlices);
                LightLoopInputs lightLoop = QueryLightLoop(context);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                ShadowInputs shadows = QueryShadowInputs(context);
                shadows.Read(passRef);
                passData.shadows = shadows;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.volumetricFogShader = shaders.volumetricFogShader;
                passData.scatterDensityKernel = kernels.fogScatterDensity;
                passData.integrateKernel = kernels.fogIntegrate;
                passData.temporalKernel = kernels.fogTemporal;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.historyTexture = passRef.ReadTexture(historyTexture);
                passData.volumetricFogTexture = passRef.WriteTexture(volumetricFogTexture);
                passData.skyViewLUT = passRef.ReadTexture(skyViewLUT);
                passData.aerialPerspectiveLUT = passRef.ReadTexture(aerialPerspectiveLUT);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in VolumetricFogPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.volumetricFogShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, VolumetricFogPassUtilityData.CBV_VolumetricFogParametersID, passData.parameters);

                    int scatterKernel = passData.scatterDensityKernel.index;
                    cmdEncoder.SetComputeBufferParam(shader, scatterKernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    passData.lightLoop.Bind(cmdEncoder, shader, scatterKernel);
                    passData.shadows.Bind(cmdEncoder, shader, scatterKernel);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, PassShaderIDs.SRV_SkyViewLUT, passData.skyViewLUT);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, PassShaderIDs.SRV_AerialPerspectiveLUT, passData.aerialPerspectiveLUT);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.BeginSample(VolumetricFogPassUtilityData.VolFogScatterSampler);
                    cmdEncoder.DispatchCompute(shader, passData.scatterDensityKernel.index, passData.scatterDensityKernel.GroupsX(passData.froxelResolution.x), passData.scatterDensityKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(VolumetricFogPassUtilityData.VolFogScatterSampler);

                    cmdEncoder.BeginSample(VolumetricFogPassUtilityData.VolFogIntegrateSampler);
                    cmdEncoder.SetComputeTextureParam(shader, passData.integrateKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(shader, passData.integrateKernel.index, passData.integrateKernel.GroupsX(passData.froxelResolution.x), passData.integrateKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(VolumetricFogPassUtilityData.VolFogIntegrateSampler);

                    cmdEncoder.BeginSample(VolumetricFogPassUtilityData.VolFogTemporalSampler);
                    cmdEncoder.SetComputeTextureParam(shader, passData.temporalKernel.index, VolumetricFogPassUtilityData.SRV_HistoryVolumetricFogID, passData.historyTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.temporalKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(shader, passData.temporalKernel.index, passData.temporalKernel.GroupsX(passData.froxelResolution.x), passData.temporalKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(VolumetricFogPassUtilityData.VolFogTemporalSampler);
                });
            }

            context.features.MarkProduced(EFrameFeature.VolumetricFog);
        }

        struct CopyHistoryVolumetricFogPassData
        {
            public RGTextureHandle source;
            public RGTextureHandle history;
        }

        void CopyHistoryVolumetricFog(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricFog))
            {
                return;
            }
            RGTextureHandle source = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricFogBuffer);

            var volFog = context.volumes.volumetricFog;
            int froxelWidth = Mathf.CeilToInt(context.dimensions.internalSize.x / 8.0f);
            int froxelHeight = Mathf.CeilToInt(context.dimensions.internalSize.y / 8.0f);
            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, volFog.Resolve().depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            RGTextureHandle history = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc));
            context.historyCache.MarkProduced(InfinityShaderIDs.HistoryVolumetricFogBuffer);

            using (var passRef = m_RGBuilder.AddTransferPass<CopyHistoryVolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryVolumetricFog)))
            {
                passRef.ReadTexture(source);
                passRef.WriteTexture(history);
                ref CopyHistoryVolumetricFogPassData passData = ref passRef.GetPassData();
                passData.source = source;
                passData.history = history;
                passRef.SetExecuteFunc((in CopyHistoryVolumetricFogPassData passData, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(passData.source, passData.history);
                });
            }
        }
    }
}
