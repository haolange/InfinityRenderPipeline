using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class VolumetricFogPassUtilityData
    {
        internal static string TextureName = "VolumetricFogTexture";
        internal static string HistoryTextureName = "HistoryVolumetricFog";
        internal static int VolFog_ResolutionID = Shader.PropertyToID("VolFog_Resolution");
        internal static int VolFog_ScreenSizeID = Shader.PropertyToID("VolFog_ScreenSize");
        internal static int VolFog_DensityID = Shader.PropertyToID("VolFog_Density");
        internal static int VolFog_HeightID = Shader.PropertyToID("VolFog_Height");
        internal static int VolFog_HeightFalloffID = Shader.PropertyToID("VolFog_HeightFalloff");
        internal static int VolFog_AlbedoID = Shader.PropertyToID("VolFog_Albedo");
        internal static int VolFog_AnisotropyID = Shader.PropertyToID("VolFog_Anisotropy");
        internal static int VolFog_AmbientIntensityID = Shader.PropertyToID("VolFog_AmbientIntensity");
        internal static int VolFog_DepthSlicesID = Shader.PropertyToID("VolFog_DepthSlices");
        internal static int VolFog_MaxDistanceID = Shader.PropertyToID("VolFog_MaxDistance");
        internal static int VolFog_TemporalWeightID = Shader.PropertyToID("VolFog_TemporalWeight");
        internal static int VolFog_FrameIndexID = Shader.PropertyToID("VolFog_FrameIndex");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_SkyViewLUTID = Shader.PropertyToID("SRV_SkyViewLUT");
        internal static int SRV_AerialPerspectiveLUTID = Shader.PropertyToID("SRV_AerialPerspectiveLUT");
        internal static int SRV_HistoryVolumetricFogID = Shader.PropertyToID("SRV_HistoryVolumetricFog");
        internal static int VolFog_HasSkyViewID = Shader.PropertyToID("VolFog_HasSkyView");
        internal static int VolFog_HasAerialID = Shader.PropertyToID("VolFog_HasAerial");
        internal static int VolFog_AerialDistanceID = Shader.PropertyToID("VolFog_AerialDistance");
        internal static int UAV_VolumetricFogTextureID = Shader.PropertyToID("UAV_VolumetricFogTexture");
        internal static int KernelScatterDensity = 0;
        internal static int KernelIntegrate = 1;
        internal static int KernelTemporal = 2;
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
            public RGBufferRef lightRecordBuffer;
            public FLightLoopInputs lightLoop;
            public FShadowInputs shadows;
            public ComputeShader volumetricFogShader;
            public RGTextureRef depthTexture;
            public RGTextureRef volumetricFogTexture;
            public RGTextureRef historyTexture;
            public RGTextureRef skyViewLUT;
            public RGTextureRef aerialPerspectiveLUT;
            public int hasSkyView;
            public int hasAerial;
            public float aerialDistance;
        }

        static TextureDescriptor CreateVolumetricFogDescriptor(int width, int height, int depthSlices, string name, bool randomWrite)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height);
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
            if (!GraphicsUtility.HasRequiredKernels(shaders.volumetricFogShader, "ScatterDensity", "Integrate", "Temporal"))
            {
                return;
            }
            FVolumetricFogSettings fog = volFog.Resolve();

            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            int depthSlices = fog.depthSlices;
            int froxelWidth = Mathf.CeilToInt(width / 8.0f);
            int froxelHeight = Mathf.CeilToInt(height / 8.0f);

            TextureDescriptor volumetricFogDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, depthSlices, VolumetricFogPassUtilityData.TextureName, true);
            RGTextureRef volumetricFogTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricFogBuffer, volumetricFogDsc);

            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            RGTextureRef historyTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc, out bool historyCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyTexture);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool hasSkyView = m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereSkyViewLUT, out RGTextureRef skyViewLUT);
            bool hasAerial = m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, out RGTextureRef aerialPerspectiveLUT);
            float aerialDistance = pipelineAsset.atmosphericalProfile != null
                ? AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile).aerialPerspectiveDistance
                : 0.0f;

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<VolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricFog)))
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
                FLightLoopInputs lightLoop = QueryLightLoop(renderContext);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                FShadowInputs shadows = QueryShadowInputs(renderContext);
                shadows.Read(passRef);
                passData.shadows = shadows;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.volumetricFogShader = shaders.volumetricFogShader;
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
                passRef.SetExecuteFunc((in VolumetricFogPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
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

                    int scatterKernel = VolumetricFogPassUtilityData.KernelScatterDensity;
                    cmdEncoder.SetComputeBufferParam(shader, scatterKernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    passData.lightLoop.Bind(cmdEncoder, shader, scatterKernel);
                    passData.shadows.Bind(cmdEncoder, shader, scatterKernel);
                    cmdEncoder.SetComputeTextureParam(shader, VolumetricFogPassUtilityData.KernelScatterDensity, VolumetricFogPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    if (passData.hasSkyView != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, VolumetricFogPassUtilityData.KernelScatterDensity, VolumetricFogPassUtilityData.SRV_SkyViewLUTID, passData.skyViewLUT);
                    }
                    if (passData.hasAerial != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, VolumetricFogPassUtilityData.KernelScatterDensity, VolumetricFogPassUtilityData.SRV_AerialPerspectiveLUTID, passData.aerialPerspectiveLUT);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, VolumetricFogPassUtilityData.KernelScatterDensity, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.BeginSample("VolFog_Scatter");
                    cmdEncoder.DispatchCompute(shader, VolumetricFogPassUtilityData.KernelScatterDensity, Mathf.CeilToInt(passData.froxelResolution.x / 8.0f), Mathf.CeilToInt(passData.froxelResolution.y / 8.0f), 1);
                    cmdEncoder.EndSample("VolFog_Scatter");

                    cmdEncoder.BeginSample("VolFog_Integrate");
                    cmdEncoder.SetComputeTextureParam(shader, VolumetricFogPassUtilityData.KernelIntegrate, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(shader, VolumetricFogPassUtilityData.KernelIntegrate, Mathf.CeilToInt(passData.froxelResolution.x / 8.0f), Mathf.CeilToInt(passData.froxelResolution.y / 8.0f), 1);
                    cmdEncoder.EndSample("VolFog_Integrate");

                    cmdEncoder.BeginSample("VolFog_Temporal");
                    cmdEncoder.SetComputeTextureParam(shader, VolumetricFogPassUtilityData.KernelTemporal, VolumetricFogPassUtilityData.SRV_HistoryVolumetricFogID, passData.historyTexture);
                    cmdEncoder.SetComputeTextureParam(shader, VolumetricFogPassUtilityData.KernelTemporal, VolumetricFogPassUtilityData.UAV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.DispatchCompute(shader, VolumetricFogPassUtilityData.KernelTemporal, Mathf.CeilToInt(passData.froxelResolution.x / 8.0f), Mathf.CeilToInt(passData.froxelResolution.y / 8.0f), 1);
                    cmdEncoder.EndSample("VolFog_Temporal");
                });
            }

            MarkFeatureProduced(EFrameFeature.VolumetricFog);
        }

        struct CopyHistoryVolumetricFogPassData
        {
            public RGTextureRef source;
            public RGTextureRef history;
        }

        void CopyHistoryVolumetricFog(HistoryCache historyCache, Camera camera)
        {
            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out RGTextureRef source))
            {
                return;
            }

            var volFog = ActiveVolumeStack.GetComponent<VolumetricFog>();
            int froxelWidth = Mathf.CeilToInt(m_ActiveFrameState.dimensions.internalSize.x / 8.0f);
            int froxelHeight = Mathf.CeilToInt(m_ActiveFrameState.dimensions.internalSize.y / 8.0f);
            TextureDescriptor historyDsc = CreateVolumetricFogDescriptor(froxelWidth, froxelHeight, volFog.Resolve().depthSlices, VolumetricFogPassUtilityData.HistoryTextureName, false);
            RGTextureRef history = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryVolumetricFogBuffer, historyDsc));
            historyCache.MarkProduced(InfinityShaderIDs.HistoryVolumetricFogBuffer);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryVolumetricFogPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryVolumetricFog)))
            {
                passRef.ReadTexture(source);
                passRef.WriteTexture(history);
                ref CopyHistoryVolumetricFogPassData passData = ref passRef.GetPassData<CopyHistoryVolumetricFogPassData>();
                passData.source = source;
                passData.history = history;
                passRef.SetExecuteFunc((in CopyHistoryVolumetricFogPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.source, passData.history);
                });
            }
        }
    }
}
