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
    internal static class VolumetricCloudPassUtilityData
    {
        internal static string TextureName = "VolumetricCloudTexture";
        internal static string HistoryTextureName = "HistoryVolumetricCloud";
        internal static int VolCloud_ResolutionID = Shader.PropertyToID("VolCloud_Resolution");
        internal static int VolCloud_CloudLayerBottomID = Shader.PropertyToID("VolCloud_CloudLayerBottom");
        internal static int VolCloud_CloudLayerThicknessID = Shader.PropertyToID("VolCloud_CloudLayerThickness");
        internal static int VolCloud_DensityMultiplierID = Shader.PropertyToID("VolCloud_DensityMultiplier");
        internal static int VolCloud_ShapeFactorID = Shader.PropertyToID("VolCloud_ShapeFactor");
        internal static int VolCloud_ErosionFactorID = Shader.PropertyToID("VolCloud_ErosionFactor");
        internal static int VolCloud_AnisotropyID = Shader.PropertyToID("VolCloud_Anisotropy");
        internal static int VolCloud_SilverIntensityID = Shader.PropertyToID("VolCloud_SilverIntensity");
        internal static int VolCloud_SilverSpreadID = Shader.PropertyToID("VolCloud_SilverSpread");
        internal static int VolCloud_AmbientIntensityID = Shader.PropertyToID("VolCloud_AmbientIntensity");
        internal static int VolCloud_NumPrimaryStepsID = Shader.PropertyToID("VolCloud_NumPrimarySteps");
        internal static int VolCloud_NumLightStepsID = Shader.PropertyToID("VolCloud_NumLightSteps");
        internal static int VolCloud_TemporalWeightID = Shader.PropertyToID("VolCloud_TemporalWeight");
        internal static int VolCloud_FrameIndexID = Shader.PropertyToID("VolCloud_FrameIndex");
        internal static int VolCloud_PlanetRadiusID = Shader.PropertyToID("VolCloud_PlanetRadius");
        internal static int VolCloud_AtmosphereHeightID = Shader.PropertyToID("VolCloud_AtmosphereHeight");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static int SRV_CascadeShadowMapID = Shader.PropertyToID("SRV_CascadeShadowMap");
        internal static int SRV_LocalShadowMapID = Shader.PropertyToID("SRV_LocalShadowMap");
        internal static int SRV_HistoryVolumetricCloudID = Shader.PropertyToID("SRV_HistoryVolumetricCloud");
        internal static int UAV_VolumetricCloudTextureID = Shader.PropertyToID("UAV_VolumetricCloudTexture");
        internal static int LocalShadowMapSizeID = Shader.PropertyToID("_LocalShadowMapSize");
        internal static int SRV_LocalShadowMatricesID = Shader.PropertyToID("SRV_LocalShadowMatrices");
        internal static int SRV_LocalShadowRectsID = Shader.PropertyToID("SRV_LocalShadowRects");
    }

    public partial class InfinityRenderPipeline
    {
        struct VolumetricCloudPassData
        {
            public float cloudLayerBottom;
            public float cloudLayerThickness;
            public float densityMultiplier;
            public float shapeFactor;
            public float erosionFactor;
            public float anisotropy;
            public float silverIntensity;
            public float silverSpread;
            public float ambientIntensity;
            public int numPrimarySteps;
            public int numLightSteps;
            public float temporalWeight;
            public int frameIndex;
            public int2 resolution;
            public float nearPlane;
            public float farPlane;
            public float planetRadius;
            public float atmosphereHeight;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_WorldToView;
            public Vector4 worldSpaceCameraPos;
            public int cascadeCount;
            public Matrix4x4[] cascadeMatrices;
            public Vector4[] cascadeSpheres;
            public Vector4 cascadeSplitDistances;
            public Vector4 cascadeShadowMapSize;
            public Vector4 localShadowMapSize;
            public RGBufferRef lightRecordBuffer;
            public FLightLoopInputs lightLoop;
            public RGBufferRef localShadowMatrixBuffer;
            public RGBufferRef localShadowRectBuffer;
            public ComputeShader volumetricCloudShader;
            public RGTextureRef depthTexture;
            public RGTextureRef transmittanceLUT;
            public RGTextureRef cascadeShadowMap;
            public RGTextureRef localShadowMap;
            public RGTextureRef historyTexture;
            public RGTextureRef volumetricCloudTexture;
        }

        static TextureDescriptor CreateVolumetricCloudDescriptor(int width, int height, string name, bool randomWrite)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = randomWrite;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
        }

        void ComputeVolumetricCloud(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.VolumetricCloud))
            {
                return;
            }

            var volCloud = ActiveVolumeStack.GetComponent<VolumetricCloud>();
            if (!VolumeComponentActive(volCloud))
            {
                return;
            }
            if (!GraphicsUtility.HasRequiredKernels(shaders.volumetricCloudShader, "VolumetricCloudCS"))
            {
                return;
            }
            FVolumetricCloudSettings cloud = volCloud.Resolve();

            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            int cloudWidth = Mathf.Max(1, width >> 1);
            int cloudHeight = Mathf.Max(1, height >> 1);

            TextureDescriptor volumetricCloudDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.TextureName, true);
            RGTextureRef volumetricCloudTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricCloudBuffer, volumetricCloudDsc);

            TextureDescriptor historyDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.HistoryTextureName, false);
            RGTextureRef historyTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyDsc, out bool historyCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyTexture);

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef transmittanceLUT = m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT);
            RGTextureRef cascadeShadowMap = m_RGScoper.QueryTexture(InfinityShaderIDs.CascadeShadowMap);
            RGTextureRef localShadowMap = m_RGScoper.QueryTexture(InfinityShaderIDs.LocalShadowMap);
            AtmosphereParameter atmosphereParameter = AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile);
            atmosphereParameter.ThrowIfInvalid();

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<VolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricCloud)))
            {
                ref VolumetricCloudPassData passData = ref passRef.GetPassData<VolumetricCloudPassData>();
                passData.cloudLayerBottom = cloud.cloudLayerBottom;
                passData.cloudLayerThickness = cloud.cloudLayerThickness;
                passData.densityMultiplier = cloud.densityMultiplier;
                passData.shapeFactor = cloud.shapeFactor;
                passData.erosionFactor = cloud.erosionFactor;
                passData.anisotropy = cloud.anisotropy;
                passData.silverIntensity = cloud.silverIntensity;
                passData.silverSpread = cloud.silverSpread;
                passData.ambientIntensity = cloud.ambientIntensity;
                passData.numPrimarySteps = cloud.numPrimarySteps;
                passData.numLightSteps = cloud.numLightSteps;
                passData.temporalWeight = (m_CameraUniform.historyReset || historyCreated) ? 0.0f : cloud.temporalWeight;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(cloudWidth, cloudHeight);
                passData.nearPlane = camera.nearClipPlane;
                passData.farPlane = camera.farClipPlane;
                passData.planetRadius = atmosphereParameter.planetRadius;
                passData.atmosphereHeight = atmosphereParameter.atmosphereHeight;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.worldSpaceCameraPos = camera.transform.position;
                FLightLoopInputs lightLoop = QueryLightLoop(renderContext);
                lightLoop.zBins = passRef.ReadBuffer(lightLoop.zBins);
                lightLoop.tileMasks = passRef.ReadBuffer(lightLoop.tileMasks);
                passData.lightLoop = lightLoop;
                passData.cascadeCount = m_ActiveCascadeCount;
                passData.cascadeMatrices = (Matrix4x4[])m_ActiveCascadeMatrices.Clone();
                passData.cascadeSpheres = (Vector4[])renderContext.lightContext.ShadowAllocator.CascadeSpheres.Clone();
                passData.cascadeSplitDistances = m_ActiveCascadeSplitDistances;
                passData.cascadeShadowMapSize = new Vector4(
                    pipelineAsset.cascadeShadowMapResolution,
                    pipelineAsset.cascadeShadowMapResolution,
                    1.0f / pipelineAsset.cascadeShadowMapResolution,
                    1.0f / pipelineAsset.cascadeShadowMapResolution);
                passData.localShadowMapSize = new Vector4(
                    pipelineAsset.localShadowMapResolution,
                    pipelineAsset.localShadowMapResolution,
                    1.0f / pipelineAsset.localShadowMapResolution,
                    1.0f / pipelineAsset.localShadowMapResolution);
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.localShadowMatrixBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LocalShadowMatrixBuffer));
                passData.localShadowRectBuffer = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LocalShadowRectBuffer));
                passData.volumetricCloudShader = shaders.volumetricCloudShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.transmittanceLUT = passRef.ReadTexture(transmittanceLUT);
                passData.cascadeShadowMap = passRef.ReadTexture(cascadeShadowMap);
                passData.localShadowMap = passRef.ReadTexture(localShadowMap);
                passData.historyTexture = passRef.ReadTexture(historyTexture);
                passData.volumetricCloudTexture = passRef.WriteTexture(volumetricCloudTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in VolumetricCloudPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.volumetricCloudShader;
                    cmdEncoder.SetComputeVectorParam(shader, VolumetricCloudPassUtilityData.VolCloud_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_CloudLayerBottomID, passData.cloudLayerBottom);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_CloudLayerThicknessID, passData.cloudLayerThickness);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_DensityMultiplierID, passData.densityMultiplier);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_ShapeFactorID, passData.shapeFactor);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_ErosionFactorID, passData.erosionFactor);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_AnisotropyID, passData.anisotropy);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_SilverIntensityID, passData.silverIntensity);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_SilverSpreadID, passData.silverSpread);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_AmbientIntensityID, passData.ambientIntensity);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricCloudPassUtilityData.VolCloud_NumPrimaryStepsID, passData.numPrimarySteps);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricCloudPassUtilityData.VolCloud_NumLightStepsID, passData.numLightSteps);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricCloudPassUtilityData.VolCloud_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_PlanetRadiusID, passData.planetRadius);
                    cmdEncoder.SetComputeFloatParam(shader, VolumetricCloudPassUtilityData.VolCloud_AtmosphereHeightID, passData.atmosphereHeight);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_WorldToView"), passData.matrix_WorldToView);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    cmdEncoder.SetComputeIntParam(shader, CascadeShadowPassUtilityData.CascadeCountID, passData.cascadeCount);
                    cmdEncoder.SetComputeVectorArrayParam(shader, CascadeShadowPassUtilityData.CascadeSpheresID, passData.cascadeSpheres);
                    cmdEncoder.SetComputeMatrixArrayParam(shader, CascadeShadowPassUtilityData.CascadeMatricesID, passData.cascadeMatrices);
                    cmdEncoder.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.CascadeSplitDistancesID, passData.cascadeSplitDistances);
                    cmdEncoder.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.CascadeShadowMapSizeID, passData.cascadeShadowMapSize);
                    cmdEncoder.SetComputeVectorParam(shader, VolumetricCloudPassUtilityData.LocalShadowMapSizeID, passData.localShadowMapSize);
                    cmdEncoder.SetComputeBufferParam(shader, 0, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_LocalShadowMatricesID, passData.localShadowMatrixBuffer);
                    cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_LocalShadowRectsID, passData.localShadowRectBuffer);
                    passData.lightLoop.Bind(cmdEncoder, shader, 0);

                    cmdEncoder.SetComputeTextureParam(shader, 0, VolumetricCloudPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, 0, VolumetricCloudPassUtilityData.SRV_TransmittanceLUTID, passData.transmittanceLUT);
                    cmdEncoder.SetComputeTextureParam(shader, 0, VolumetricCloudPassUtilityData.SRV_CascadeShadowMapID, passData.cascadeShadowMap);
                    cmdEncoder.SetComputeTextureParam(shader, 0, VolumetricCloudPassUtilityData.SRV_LocalShadowMapID, passData.localShadowMap);
                    cmdEncoder.SetComputeTextureParam(shader, 0, VolumetricCloudPassUtilityData.SRV_HistoryVolumetricCloudID, passData.historyTexture);
                    cmdEncoder.SetComputeTextureParam(shader, 0, VolumetricCloudPassUtilityData.UAV_VolumetricCloudTextureID, passData.volumetricCloudTexture);
                    cmdEncoder.BeginSample("VolCloud_Trace");
                    cmdEncoder.DispatchCompute(shader, 0, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                    cmdEncoder.EndSample("VolCloud_Trace");
                });
            }

            MarkFeatureProduced(EFrameFeature.VolumetricCloud);
        }

        struct CopyHistoryVolumetricCloudPassData
        {
            public RGTextureRef source;
            public RGTextureRef history;
        }

        void CopyHistoryVolumetricCloud(HistoryCache historyCache, Camera camera)
        {
            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricCloudBuffer, out RGTextureRef source))
            {
                return;
            }

            int cloudWidth = Mathf.Max(1, m_ActiveFrameState.dimensions.internalSize.x >> 1);
            int cloudHeight = Mathf.Max(1, m_ActiveFrameState.dimensions.internalSize.y >> 1);
            TextureDescriptor historyDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.HistoryTextureName, false);
            RGTextureRef history = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyDsc));
            historyCache.MarkProduced(InfinityShaderIDs.HistoryVolumetricCloudBuffer);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryVolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryVolumetricCloud)))
            {
                passRef.ReadTexture(source);
                passRef.WriteTexture(history);
                ref CopyHistoryVolumetricCloudPassData passData = ref passRef.GetPassData<CopyHistoryVolumetricCloudPassData>();
                passData.source = source;
                passData.history = history;
                passRef.SetExecuteFunc((in CopyHistoryVolumetricCloudPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.source, passData.history);
                });
            }
        }
    }
}
