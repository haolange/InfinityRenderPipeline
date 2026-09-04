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
        internal static int VolCloud_HasTileListID = Shader.PropertyToID("VolCloud_HasTileList");
        internal static int VolCloud_NumTilesXID = Shader.PropertyToID("VolCloud_NumTilesX");
        internal static int VolCloud_NumBinsID = Shader.PropertyToID("VolCloud_NumBins");
        internal static int VolCloud_NearFarID = Shader.PropertyToID("VolCloud_NearFar");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_TransmittanceLUTID = Shader.PropertyToID("SRV_TransmittanceLUT");
        internal static int SRV_CascadeShadowMapID = Shader.PropertyToID("SRV_CascadeShadowMap");
        internal static int SRV_LocalShadowMapID = Shader.PropertyToID("SRV_LocalShadowMap");
        internal static int SRV_HistoryVolumetricCloudID = Shader.PropertyToID("SRV_HistoryVolumetricCloud");
        internal static int UAV_VolumetricCloudTextureID = Shader.PropertyToID("UAV_VolumetricCloudTexture");
        internal static int LocalShadowMapSizeID = Shader.PropertyToID("_LocalShadowMapSize");
        internal static int SRV_TileLightRangeID = Shader.PropertyToID("SRV_TileLightRange");
        internal static int SRV_TileLightListID = Shader.PropertyToID("SRV_TileLightList");
        internal static int SRV_ZBinRangeID = Shader.PropertyToID("SRV_ZBinRange");
        internal static int SRV_ZBinLightListID = Shader.PropertyToID("SRV_ZBinLightList");
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
            public int numTilesX;
            public int hasTileList;
            public float nearPlane;
            public float farPlane;
            public float planetRadius;
            public float atmosphereHeight;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_WorldToView;
            public Vector4 worldSpaceCameraPos;
            public int directionalLightCount;
            public int localLightCount;
            public int cascadeCount;
            public Matrix4x4[] cascadeMatrices;
            public Vector4 cascadeSplitDistances;
            public Vector4 cascadeShadowMapSize;
            public Vector4 localShadowMapSize;
            public GraphicsBuffer lightRecordBuffer;
            public GraphicsBuffer localShadowMatrixBuffer;
            public GraphicsBuffer localShadowRectBuffer;
            public GraphicsBuffer emptyTileRange;
            public GraphicsBuffer emptyTileList;
            public GraphicsBuffer emptyZBinRange;
            public GraphicsBuffer emptyZBinList;
            public ComputeShader volumetricCloudShader;
            public RGTextureRef depthTexture;
            public RGTextureRef transmittanceLUT;
            public RGTextureRef cascadeShadowMap;
            public RGTextureRef localShadowMap;
            public RGTextureRef historyTexture;
            public RGTextureRef volumetricCloudTexture;
            public RGBufferRef tileRange;
            public RGBufferRef tileList;
            public RGBufferRef zBinRange;
            public RGBufferRef zBinList;
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
            if (!GraphicsUtility.VolumeHasOverrides(volCloud))
            {
                return;
            }
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.volumetricCloudShader, "VolumetricCloudCS"))
            {
                return;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
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

            RGBufferRef tileRange = default;
            RGBufferRef tileList = default;
            RGBufferRef zBinRange = default;
            RGBufferRef zBinList = default;
            bool hasTileList =
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.TileLightRangeBuffer, out tileRange) &&
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.TileLightListBuffer, out tileList) &&
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.ZBinRangeBuffer, out zBinRange) &&
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.ZBinLightListBuffer, out zBinList);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<VolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricCloud)))
            {
                ref VolumetricCloudPassData passData = ref passRef.GetPassData<VolumetricCloudPassData>();
                passData.cloudLayerBottom = volCloud.CloudLayerBottom.value;
                passData.cloudLayerThickness = volCloud.CloudLayerThickness.value;
                passData.densityMultiplier = volCloud.DensityMultiplier.value;
                passData.shapeFactor = volCloud.ShapeFactor.value;
                passData.erosionFactor = volCloud.ErosionFactor.value;
                passData.anisotropy = volCloud.Anisotropy.value;
                passData.silverIntensity = volCloud.SilverIntensity.value;
                passData.silverSpread = volCloud.SilverSpread.value;
                passData.ambientIntensity = volCloud.AmbientIntensity.value;
                passData.numPrimarySteps = volCloud.NumPrimarySteps.value;
                passData.numLightSteps = volCloud.NumLightSteps.value;
                passData.temporalWeight = (m_CameraUniform.historyReset || historyCreated) ? 0.0f : volCloud.TemporalWeight.value;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(cloudWidth, cloudHeight);
                passData.numTilesX = Mathf.CeilToInt(width / 16.0f);
                passData.hasTileList = hasTileList ? 1 : 0;
                passData.nearPlane = camera.nearClipPlane;
                passData.farPlane = camera.farClipPlane;
                passData.planetRadius = atmosphereParameter.planetRadius;
                passData.atmosphereHeight = atmosphereParameter.atmosphereHeight;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.directionalLightCount = renderContext.lightContext.DirectionalLightCount;
                passData.localLightCount = renderContext.lightContext.LocalLightCount;
                passData.cascadeCount = m_ActiveCascadeCount;
                passData.cascadeMatrices = m_ActiveCascadeMatrices;
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
                passData.lightRecordBuffer = renderContext.lightContext.LightRecordBuffer;
                passData.localShadowMatrixBuffer = renderContext.lightContext.LocalShadowMatrixBuffer;
                passData.localShadowRectBuffer = renderContext.lightContext.LocalShadowRectBuffer;
                passData.emptyTileRange = renderContext.lightContext.EmptyTileRangeBuffer;
                passData.emptyTileList = renderContext.lightContext.EmptyTileListBuffer;
                passData.emptyZBinRange = renderContext.lightContext.EmptyZBinRangeBuffer;
                passData.emptyZBinList = renderContext.lightContext.EmptyZBinListBuffer;
                passData.volumetricCloudShader = pipelineAsset.volumetricCloudShader;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.transmittanceLUT = passRef.ReadTexture(transmittanceLUT);
                passData.cascadeShadowMap = passRef.ReadTexture(cascadeShadowMap);
                passData.localShadowMap = passRef.ReadTexture(localShadowMap);
                passData.historyTexture = passRef.ReadTexture(historyTexture);
                passData.volumetricCloudTexture = passRef.WriteTexture(volumetricCloudTexture);
                if (hasTileList)
                {
                    passData.tileRange = passRef.ReadBuffer(tileRange);
                    passData.tileList = passRef.ReadBuffer(tileList);
                    passData.zBinRange = passRef.ReadBuffer(zBinRange);
                    passData.zBinList = passRef.ReadBuffer(zBinList);
                }

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
                    cmdEncoder.SetComputeIntParam(shader, VolumetricCloudPassUtilityData.VolCloud_HasTileListID, passData.hasTileList);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricCloudPassUtilityData.VolCloud_NumTilesXID, passData.numTilesX);
                    cmdEncoder.SetComputeIntParam(shader, VolumetricCloudPassUtilityData.VolCloud_NumBinsID, ZBinningPassUtilityData.NumBins);
                    cmdEncoder.SetComputeVectorParam(shader, VolumetricCloudPassUtilityData.VolCloud_NearFarID, new Vector4(passData.nearPlane, passData.farPlane, 0, 0));
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_WorldToView"), passData.matrix_WorldToView);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    cmdEncoder.SetComputeIntParam(shader, LightShaderIDs.DirectionalLightCount, passData.directionalLightCount);
                    cmdEncoder.SetComputeIntParam(shader, LightShaderIDs.LocalLightCount, passData.localLightCount);
                    cmdEncoder.SetComputeIntParam(shader, CascadeShadowPassUtilityData.CascadeCountID, passData.cascadeCount);
                    cmdEncoder.SetComputeMatrixArrayParam(shader, CascadeShadowPassUtilityData.CascadeMatricesID, passData.cascadeMatrices);
                    cmdEncoder.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.CascadeSplitDistancesID, passData.cascadeSplitDistances);
                    cmdEncoder.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.CascadeShadowMapSizeID, passData.cascadeShadowMapSize);
                    cmdEncoder.SetComputeVectorParam(shader, VolumetricCloudPassUtilityData.LocalShadowMapSizeID, passData.localShadowMapSize);
                    cmdEncoder.SetComputeBufferParam(shader, 0, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_LocalShadowMatricesID, passData.localShadowMatrixBuffer);
                    cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_LocalShadowRectsID, passData.localShadowRectBuffer);
                    if (passData.hasTileList != 0)
                    {
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_TileLightRangeID, passData.tileRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_TileLightListID, passData.tileList);
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_ZBinRangeID, passData.zBinRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_ZBinLightListID, passData.zBinList);
                    }
                    else
                    {
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_TileLightRangeID, passData.emptyTileRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_TileLightListID, passData.emptyTileList);
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_ZBinRangeID, passData.emptyZBinRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, VolumetricCloudPassUtilityData.SRV_ZBinLightListID, passData.emptyZBinList);
                    }

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

            int cloudWidth = Mathf.Max(1, camera.pixelWidth >> 1);
            int cloudHeight = Mathf.Max(1, camera.pixelHeight >> 1);
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
