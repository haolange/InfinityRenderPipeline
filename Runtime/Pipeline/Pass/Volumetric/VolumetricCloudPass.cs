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
    internal static class VolumetricCloudPassUtilityData
    {
        internal static readonly ProfilingSampler VolCloudTraceSampler = new ProfilingSampler("VolCloud_Trace");
        internal static readonly string TextureName = "VolumetricCloudTexture";
        internal static readonly string HistoryTextureName = "HistoryVolumetricCloud";
        internal static readonly int VolCloud_ResolutionID = Shader.PropertyToID("VolCloud_Resolution");
        internal static readonly int VolCloud_CloudLayerBottomID = Shader.PropertyToID("VolCloud_CloudLayerBottom");
        internal static readonly int VolCloud_CloudLayerThicknessID = Shader.PropertyToID("VolCloud_CloudLayerThickness");
        internal static readonly int VolCloud_DensityMultiplierID = Shader.PropertyToID("VolCloud_DensityMultiplier");
        internal static readonly int VolCloud_ShapeFactorID = Shader.PropertyToID("VolCloud_ShapeFactor");
        internal static readonly int VolCloud_ErosionFactorID = Shader.PropertyToID("VolCloud_ErosionFactor");
        internal static readonly int VolCloud_AnisotropyID = Shader.PropertyToID("VolCloud_Anisotropy");
        internal static readonly int VolCloud_SilverIntensityID = Shader.PropertyToID("VolCloud_SilverIntensity");
        internal static readonly int VolCloud_SilverSpreadID = Shader.PropertyToID("VolCloud_SilverSpread");
        internal static readonly int VolCloud_AmbientIntensityID = Shader.PropertyToID("VolCloud_AmbientIntensity");
        internal static readonly int VolCloud_NumPrimaryStepsID = Shader.PropertyToID("VolCloud_NumPrimarySteps");
        internal static readonly int VolCloud_NumLightStepsID = Shader.PropertyToID("VolCloud_NumLightSteps");
        internal static readonly int VolCloud_TemporalWeightID = Shader.PropertyToID("VolCloud_TemporalWeight");
        internal static readonly int VolCloud_FrameIndexID = Shader.PropertyToID("VolCloud_FrameIndex");
        internal static readonly int VolCloud_PlanetRadiusID = Shader.PropertyToID("VolCloud_PlanetRadius");
        internal static readonly int VolCloud_AtmosphereHeightID = Shader.PropertyToID("VolCloud_AtmosphereHeight");
        internal static readonly int SRV_HistoryVolumetricCloudID = Shader.PropertyToID("SRV_HistoryVolumetricCloud");
        internal static readonly int UAV_VolumetricCloudTextureID = Shader.PropertyToID("UAV_VolumetricCloudTexture");
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
            public RGBufferHandle lightRecordBuffer;
            public LightLoopInputs lightLoop;
            public ShadowInputs shadows;
            public ComputeShader volumetricCloudShader;
            public ComputeKernel cloudKernel;
            public RGTextureHandle depthTexture;
            public RGTextureHandle transmittanceLUT;
            public RGTextureHandle historyTexture;
            public RGTextureHandle volumetricCloudTexture;
        }

        static TextureDescriptor CreateVolumetricCloudDescriptor(int width, int height, string name, bool randomWrite)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal, 2);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = randomWrite;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
        }

        void ComputeVolumetricCloud(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricCloud))
            {
                return;
            }

            var volCloud = context.volumeStack.GetComponent<VolumetricCloud>();
            if (!VolumeComponentActive(volCloud))
            {
                return;
            }
            VolumetricCloudSettings cloud = volCloud.Resolve();

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            int cloudWidth = Mathf.Max(1, width >> 1);
            int cloudHeight = Mathf.Max(1, height >> 1);

            TextureDescriptor volumetricCloudDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.TextureName, true);
            RGTextureHandle volumetricCloudTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricCloudBuffer, volumetricCloudDsc);

            TextureDescriptor historyDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.HistoryTextureName, false);
            RGTextureHandle historyTexture = m_RGBuilder.ImportTexture(context.historyCache.GetTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyDsc, out bool historyCreated));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyTexture);

            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle transmittanceLUT = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT);
            AtmosphereParameter atmosphereParameter = AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile);
            atmosphereParameter.ThrowIfInvalid();

            using (var passRef = m_RGBuilder.AddComputePass<VolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricCloud)))
            {
                ref VolumetricCloudPassData passData = ref passRef.GetPassData();
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
                passData.temporalWeight = (context.resetsHistory || historyCreated) ? 0.0f : cloud.temporalWeight;
                passData.frameIndex = Time.frameCount;
                passData.resolution = new int2(cloudWidth, cloudHeight);
                passData.nearPlane = context.camera.nearClipPlane;
                passData.farPlane = context.camera.farClipPlane;
                passData.planetRadius = atmosphereParameter.planetRadius;
                passData.atmosphereHeight = atmosphereParameter.atmosphereHeight;
                passData.matrix_InvViewProj = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_WorldToView = context.cameraUniform.matrix_WorldToView;
                passData.worldSpaceCameraPos = context.camera.transform.position;
                LightLoopInputs lightLoop = QueryLightLoop();
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                ShadowInputs shadows = QueryShadowInputs();
                shadows.Read(passRef);
                passData.shadows = shadows;
                passData.lightRecordBuffer = passRef.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.volumetricCloudShader = shaders.volumetricCloudShader;
                passData.cloudKernel = kernels.volumetricCloud;
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.transmittanceLUT = passRef.ReadTexture(transmittanceLUT);
                passData.historyTexture = passRef.ReadTexture(historyTexture);
                passData.volumetricCloudTexture = passRef.WriteTexture(volumetricCloudTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in VolumetricCloudPassData passData, in RGComputeEncoder cmdEncoder) =>
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
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvViewProj, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_WorldToView, passData.matrix_WorldToView);
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.WorldSpaceCameraPos, passData.worldSpaceCameraPos);
                    cmdEncoder.SetComputeBufferParam(shader, passData.cloudKernel.index, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    passData.lightLoop.Bind(cmdEncoder, shader, passData.cloudKernel.index);
                    passData.shadows.Bind(cmdEncoder, shader, passData.cloudKernel.index);

                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, PassShaderIDs.SRV_TransmittanceLUT, passData.transmittanceLUT);
                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, VolumetricCloudPassUtilityData.SRV_HistoryVolumetricCloudID, passData.historyTexture);
                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, VolumetricCloudPassUtilityData.UAV_VolumetricCloudTextureID, passData.volumetricCloudTexture);
                    cmdEncoder.BeginSample(VolumetricCloudPassUtilityData.VolCloudTraceSampler);
                    cmdEncoder.DispatchCompute(shader, passData.cloudKernel.index, passData.cloudKernel.GroupsX(passData.resolution.x), passData.cloudKernel.GroupsY(passData.resolution.y), 1);
                    cmdEncoder.EndSample(VolumetricCloudPassUtilityData.VolCloudTraceSampler);
                });
            }

            context.features.MarkProduced(EFrameFeature.VolumetricCloud);
        }

        struct CopyHistoryVolumetricCloudPassData
        {
            public RGTextureHandle source;
            public RGTextureHandle history;
        }

        void CopyHistoryVolumetricCloud(in CameraRecordContext context)
        {
            if (!m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.VolumetricCloudBuffer, out RGTextureHandle source))
            {
                return;
            }

            int cloudWidth = Mathf.Max(1, context.dimensions.internalSize.x >> 1);
            int cloudHeight = Mathf.Max(1, context.dimensions.internalSize.y >> 1);
            TextureDescriptor historyDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.HistoryTextureName, false);
            RGTextureHandle history = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyDsc));
            context.historyCache.MarkProduced(InfinityShaderIDs.HistoryVolumetricCloudBuffer);

            using (var passRef = m_RGBuilder.AddTransferPass<CopyHistoryVolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryVolumetricCloud)))
            {
                passRef.ReadTexture(source);
                passRef.WriteTexture(history);
                ref CopyHistoryVolumetricCloudPassData passData = ref passRef.GetPassData();
                passData.source = source;
                passData.history = history;
                passRef.SetExecuteFunc((in CopyHistoryVolumetricCloudPassData passData, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(passData.source, passData.history);
                });
            }
        }
    }
}
