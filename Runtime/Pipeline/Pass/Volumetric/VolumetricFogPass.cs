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
    internal static class VolumetricFogPassUtilityData
    {
        internal static readonly ProfilingSampler VolFogScatterSampler = new ProfilingSampler("VolFog_Scatter");
        internal static readonly ProfilingSampler VolFogIntegrateSampler = new ProfilingSampler("VolFog_Integrate");
        internal static readonly ProfilingSampler VolFogTemporalSampler = new ProfilingSampler("VolFog_Temporal");
        internal static readonly string TextureName = "VolumetricFogTexture";
        internal static readonly string HistoryTextureName = "HistoryVolumetricFog";
        internal static readonly int VolFog_ResolutionID = Shader.PropertyToID("VolFog_Resolution");
        internal static readonly int VolFog_ScreenSizeID = Shader.PropertyToID("VolFog_ScreenSize");
        internal static readonly int VolFog_DensityID = Shader.PropertyToID("VolFog_Density");
        internal static readonly int VolFog_HeightID = Shader.PropertyToID("VolFog_Height");
        internal static readonly int VolFog_HeightFalloffID = Shader.PropertyToID("VolFog_HeightFalloff");
        internal static readonly int VolFog_AlbedoID = Shader.PropertyToID("VolFog_Albedo");
        internal static readonly int VolFog_AnisotropyID = Shader.PropertyToID("VolFog_Anisotropy");
        internal static readonly int VolFog_AmbientIntensityID = Shader.PropertyToID("VolFog_AmbientIntensity");
        internal static readonly int VolFog_DepthSlicesID = Shader.PropertyToID("VolFog_DepthSlices");
        internal static readonly int VolFog_TemporalWeightID = Shader.PropertyToID("VolFog_TemporalWeight");
        internal static readonly int VolFog_FrameIndexID = Shader.PropertyToID("VolFog_FrameIndex");
        internal static readonly int SRV_HistoryVolumetricFogID = Shader.PropertyToID("SRV_HistoryVolumetricFog");
        internal static readonly int UAV_VolumetricFogTextureID = Shader.PropertyToID("UAV_VolumetricFogTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct VolumetricFogPassData
        {
            public float density;
            public float height;
            public float heightFalloff;
            public Color albedo;
            public float anisotropy;
            public float ambientIntensity;
            public int depthSlices;
            public float maxDistance;
            public float temporalWeight;
            public int frameIndex;
            public int2 screenSize;
            public int3 froxelResolution;
            public float nearPlane;
            public float farPlane;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_WorldToView;
            public Vector4 worldSpaceCameraPos;
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
            public float aerialDistance;
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
                passData.density = fog.density;
                passData.height = fog.height;
                passData.heightFalloff = fog.heightFalloff;
                passData.albedo = fog.albedo;
                passData.anisotropy = fog.anisotropy;
                passData.ambientIntensity = fog.ambientIntensity;
                passData.depthSlices = depthSlices;
                passData.maxDistance = fog.maxDistance;
                passData.temporalWeight = historyRead.valid ? fog.temporalWeight : 0.0f;
                passData.frameIndex = Time.frameCount;
                passData.screenSize = new int2(width, height);
                passData.froxelResolution = new int3(froxelWidth, froxelHeight, depthSlices);
                passData.nearPlane = context.camera.nearClipPlane;
                passData.farPlane = context.camera.farClipPlane;
                passData.matrix_InvViewProj = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_WorldToView = context.cameraUniform.matrix_WorldToView;
                passData.worldSpaceCameraPos = context.camera.transform.position;
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
                passData.aerialDistance = aerialDistance;
                passData.skyViewLUT = passRef.ReadTexture(skyViewLUT);
                passData.aerialPerspectiveLUT = passRef.ReadTexture(aerialPerspectiveLUT);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in VolumetricFogPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.volumetricFogShader;
                    cmdEncoder.SetComputeVectorParam(shader, VolumetricFogPassUtilityData.VolFog_ScreenSizeID, new Vector4(passData.screenSize.x, passData.screenSize.y, 1.0f / passData.screenSize.x, 1.0f / passData.screenSize.y));
                    cmdEncoder.SetComputeVectorParam(shader, VolumetricFogPassUtilityData.VolFog_ResolutionID, new Vector4(passData.froxelResolution.x, passData.froxelResolution.y, passData.froxelResolution.z, 0));
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_DensityID, passData.density);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_HeightID, passData.height);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_HeightFalloffID, passData.heightFalloff);
                    cmdEncoder.SetComputeVectorParam(shader, VolumetricFogPassUtilityData.VolFog_AlbedoID, (Vector4)passData.albedo);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_AnisotropyID, passData.anisotropy);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_AmbientIntensityID, passData.ambientIntensity);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricFogPassUtilityData.VolFog_DepthSlicesID, passData.depthSlices);
                    cmdEncoder.SetComputeFloatParam(shader, PassShaderIDs.VolFog_MaxDistance, passData.maxDistance);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricFogPassUtilityData.VolFog_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvViewProj, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_WorldToView, passData.matrix_WorldToView);
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.WorldSpaceCameraPos, passData.worldSpaceCameraPos);
                    cmdEncoder.SetComputeFloatParam(shader, PassShaderIDs.VolFog_AerialDistance, passData.aerialDistance);

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
