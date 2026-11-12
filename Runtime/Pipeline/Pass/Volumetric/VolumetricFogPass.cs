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
        internal static readonly int VolFog_MaxDistanceID = Shader.PropertyToID("VolFog_MaxDistance");
        internal static readonly int VolFog_TemporalWeightID = Shader.PropertyToID("VolFog_TemporalWeight");
        internal static readonly int VolFog_FrameIndexID = Shader.PropertyToID("VolFog_FrameIndex");
        internal static readonly int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static readonly int SRV_SkyViewLUTID = Shader.PropertyToID("SRV_SkyViewLUT");
        internal static readonly int SRV_AerialPerspectiveLUTID = Shader.PropertyToID("SRV_AerialPerspectiveLUT");
        internal static readonly int SRV_HistoryVolumetricFogID = Shader.PropertyToID("SRV_HistoryVolumetricFog");
        internal static readonly int VolFog_HasSkyViewID = Shader.PropertyToID("VolFog_HasSkyView");
        internal static readonly int VolFog_HasAerialID = Shader.PropertyToID("VolFog_HasAerial");
        internal static readonly int VolFog_AerialDistanceID = Shader.PropertyToID("VolFog_AerialDistance");
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
            public int hasSkyView;
            public int hasAerial;
            public float aerialDistance;
        }

        static TextureDescriptor CreateVolumetricFogDescriptor(int width, int height, int depthSlices, string name, bool randomWrite)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal, 8);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex3D;
            descriptor.slices = depthSlices;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = randomWrite;
            descriptor.filterMode = FilterMode.Trilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
        }

        void ComputeVolumetricFog(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.VolumetricFog))
            {
                return;
            }

            var volFog = ActiveVolumeStack.GetComponent<VolumetricFog>();
            if (!VolumeComponentActive(volFog))
            {
                return;
            }
            VolumetricFogSettings fog = volFog.Resolve();

            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            int depthSlices = fog.depthSlices;
            int froxelWidth = Mathf.CeilToInt(width / 8.0f);
            int froxelHeight = Mathf.CeilToInt(height / 8.0f);

            TextureDescriptor volumetricFogDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, depthSlices, VolumetricFogPassUtilityData.TextureName, true);
            RGTextureHandle volumetricFogTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricFogBuffer, volumetricFogDsc);

            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            RGTextureHandle historyTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc, out bool historyCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyTexture);

            RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool hasSkyView = m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, out RGTextureHandle skyViewLUT);
            bool hasAerial = m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, out RGTextureHandle aerialPerspectiveLUT);
            float aerialDistance = pipelineAsset.atmosphericalProfile != null
                ? AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile).aerialPerspectiveDistance
                : 0.0f;

            using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<VolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricFog)))
            {
                ref VolumetricFogPassData passData = ref passRef.GetPassData<VolumetricFogPassData>();
                passData.density = fog.density;
                passData.height = fog.height;
                passData.heightFalloff = fog.heightFalloff;
                passData.albedo = fog.albedo;
                passData.anisotropy = fog.anisotropy;
                passData.ambientIntensity = fog.ambientIntensity;
                passData.depthSlices = depthSlices;
                passData.maxDistance = fog.maxDistance;
                passData.temporalWeight = (m_CameraUniform.historyReset || historyCreated) ? 0.0f : fog.temporalWeight;
                passData.frameIndex = Time.frameCount;
                passData.screenSize = new int2(width, height);
                passData.froxelResolution = new int3(froxelWidth, froxelHeight, depthSlices);
                passData.nearPlane = camera.nearClipPlane;
                passData.farPlane = camera.farClipPlane;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.worldSpaceCameraPos = camera.transform.position;
                LightLoopInputs lightLoop = QueryLightLoop(renderContext);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                ShadowInputs shadows = QueryShadowInputs(renderContext);
                shadows.Read(passRef);
                passData.shadows = shadows;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.volumetricFogShader = shaders.volumetricFogShader;
                passData.scatterDensityKernel = kernels.fogScatterDensity;
                passData.integrateKernel = kernels.fogIntegrate;
                passData.temporalKernel = kernels.fogTemporal;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.historyTexture = passRef.ReadTexture(historyTexture);
                passData.volumetricFogTexture = passRef.WriteTexture(volumetricFogTexture);
                passData.hasSkyView = hasSkyView ? 1 : 0;
                passData.hasAerial = hasAerial ? 1 : 0;
                passData.aerialDistance = aerialDistance;
                if (hasSkyView)
                {
                    passData.skyViewLUT = passRef.ReadTexture(skyViewLUT);
                }
                if (hasAerial)
                {
                    passData.aerialPerspectiveLUT = passRef.ReadTexture(aerialPerspectiveLUT);
                }

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
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_MaxDistanceID, passData.maxDistance);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricFogPassUtilityData.VolFog_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_WorldToView"), passData.matrix_WorldToView);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricFogPassUtilityData.VolFog_HasSkyViewID, passData.hasSkyView);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricFogPassUtilityData.VolFog_HasAerialID, passData.hasAerial);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricFogPassUtilityData.VolFog_AerialDistanceID, passData.aerialDistance);

                    int scatterKernel = passData.scatterDensityKernel.index;
                    cmdEncoder.SetComputeBufferParam(shader, scatterKernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    passData.lightLoop.Bind(cmdEncoder, shader, scatterKernel);
                    passData.shadows.Bind(cmdEncoder, shader, scatterKernel);
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, VolumetricFogPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    if (passData.hasSkyView != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, VolumetricFogPassUtilityData.SRV_SkyViewLUTID, passData.skyViewLUT);
                    }
                    if (passData.hasAerial != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, VolumetricFogPassUtilityData.SRV_AerialPerspectiveLUTID, passData.aerialPerspectiveLUT);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.scatterDensityKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.BeginSample(CachedSampler.Get("VolFog_Scatter"));
                    cmdEncoder.DispatchCompute(shader, passData.scatterDensityKernel.index, passData.scatterDensityKernel.GroupsX(passData.froxelResolution.x), passData.scatterDensityKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(CachedSampler.Get("VolFog_Scatter"));

                    cmdEncoder.BeginSample(CachedSampler.Get("VolFog_Integrate"));
                    cmdEncoder.SetComputeTextureParam(shader, passData.integrateKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(shader, passData.integrateKernel.index, passData.integrateKernel.GroupsX(passData.froxelResolution.x), passData.integrateKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(CachedSampler.Get("VolFog_Integrate"));

                    cmdEncoder.BeginSample(CachedSampler.Get("VolFog_Temporal"));
                    cmdEncoder.SetComputeTextureParam(shader, passData.temporalKernel.index, VolumetricFogPassUtilityData.SRV_HistoryVolumetricFogID, passData.historyTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.temporalKernel.index, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(shader, passData.temporalKernel.index, passData.temporalKernel.GroupsX(passData.froxelResolution.x), passData.temporalKernel.GroupsY(passData.froxelResolution.y), 1);
                    cmdEncoder.EndSample(CachedSampler.Get("VolFog_Temporal"));
                });
            }

            MarkFeatureProduced(EFrameFeature.VolumetricFog);
        }

        struct CopyHistoryVolumetricFogPassData
        {
            public RGTextureHandle source;
            public RGTextureHandle history;
        }

        void CopyHistoryVolumetricFog(HistoryCache historyCache, Camera camera)
        {
            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out RGTextureHandle source))
            {
                return;
            }

            var volFog = ActiveVolumeStack.GetComponent<VolumetricFog>();
            int froxelWidth = Mathf.CeilToInt(m_ActiveFrameState.dimensions.internalSize.x / 8.0f);
            int froxelHeight = Mathf.CeilToInt(m_ActiveFrameState.dimensions.internalSize.y / 8.0f);
            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, volFog.Resolve().depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            RGTextureHandle history = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc));
            historyCache.MarkProduced(InfinityShaderIDs.HistoryVolumetricFogBuffer);

            using (RGTransferPassHandle passRef = m_RGBuilder.AddTransferPass<CopyHistoryVolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryVolumetricFog)))
            {
                passRef.ReadTexture(source);
                passRef.WriteTexture(history);
                ref CopyHistoryVolumetricFogPassData passData = ref passRef.GetPassData<CopyHistoryVolumetricFogPassData>();
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
