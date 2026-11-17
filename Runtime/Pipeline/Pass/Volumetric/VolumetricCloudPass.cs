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
    // GPU-RECORD: CBV_VolumetricCloudParameters Shaders/Pass/Volumetric/Compute_VolumetricCloud.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct VolumetricCloudParameters
    {
        public Vector4 resolution;
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
        public float planetRadius;
        public float atmosphereHeight;
        public int padding0;
    }

    internal static class VolumetricCloudPassUtilityData
    {
        internal static readonly int CBV_VolumetricCloudParametersID = Shader.PropertyToID("CBV_VolumetricCloudParameters");
        internal static readonly ProfilingSampler VolCloudTraceSampler = new ProfilingSampler("VolCloud_Trace");
        internal static readonly string TextureName = "VolumetricCloudTexture";
        internal static readonly string HistoryTextureName = "HistoryVolumetricCloud";
        internal static readonly int SRV_HistoryVolumetricCloudID = Shader.PropertyToID("SRV_HistoryVolumetricCloud");
        internal static readonly int UAV_VolumetricCloudTextureID = Shader.PropertyToID("UAV_VolumetricCloudTexture");
    }

    internal sealed class VolumetricCloudPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;

        internal VolumetricCloudPass(RGBuilder graph, InfinityRenderPipelineResources resources)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
        }

        struct VolumetricCloudPassData
        {
            public RGConstantsHandle parametersHandle;
            public int2 resolution;
            public RGBufferHandle lightRecordBufferHandle;
            public LightLoopInputs lightLoop;
            public ShadowInputs shadows;
            public ComputeShader volumetricCloudShader;
            public ComputeKernel cloudKernel;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle transmittanceLUTHandle;
            public RGTextureHandle historyTextureHandle;
            public RGTextureHandle volumetricCloudTextureHandle;
        }

        static TextureDescriptor CreateVolumetricCloudDescriptor(int width, int height, string name, bool randomWrite)
        {
            TextureDescriptor descriptor = TextureDescriptor.Create2D(name, width, height, GraphicsFormat.R16G16B16A16_SFloat, ETextureSizingDomain.Internal, 2);
            descriptor.enableRandomWrite = randomWrite;
            return descriptor;
        }

        internal void ComputeVolumetricCloud(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricCloud))
            {
                return;
            }

            var volCloud = context.volumes.volumetricCloud;
            if (!GraphicsUtility.VolumeComponentActive(volCloud))
            {
                return;
            }
            VolumetricCloudSettings cloud = volCloud.Resolve();

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            int cloudWidth = Mathf.Max(1, width >> 1);
            int cloudHeight = Mathf.Max(1, height >> 1);

            TextureDescriptor volumetricCloudDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.TextureName, true);
            RGTextureHandle volumetricCloudTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.VolumetricCloudBuffer, volumetricCloudDsc);

            TextureDescriptor historyDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.HistoryTextureName, false);
            HistoryRead historyRead = context.historyCache.Read(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyDsc, context.resetsHistory);
            RGTextureHandle historyTextureHandle = m_RGBuilder.ImportTexture(historyRead.textureHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyTextureHandle);

            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle transmittanceLUTHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT);
            AtmosphereParameter atmosphereParameter = context.atmosphere;

            {
                RGComputePassHandle<VolumetricCloudPassData> passHandle = m_RGBuilder.AddComputePass<VolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricCloud));
                ref VolumetricCloudPassData passData = ref passHandle.GetPassData();
                passData.resolution = new int2(cloudWidth, cloudHeight);
                passData.parametersHandle = passHandle.WriteConstants(new VolumetricCloudParameters
                {
                    resolution = new Vector4(cloudWidth, cloudHeight, 1.0f / cloudWidth, 1.0f / cloudHeight),
                    cloudLayerBottom = cloud.cloudLayerBottom,
                    cloudLayerThickness = cloud.cloudLayerThickness,
                    densityMultiplier = cloud.densityMultiplier,
                    shapeFactor = cloud.shapeFactor,
                    erosionFactor = cloud.erosionFactor,
                    anisotropy = cloud.anisotropy,
                    silverIntensity = cloud.silverIntensity,
                    silverSpread = cloud.silverSpread,
                    ambientIntensity = cloud.ambientIntensity,
                    numPrimarySteps = cloud.numPrimarySteps,
                    numLightSteps = cloud.numLightSteps,
                    temporalWeight = historyRead.valid ? cloud.temporalWeight : 0.0f,
                    frameIndex = context.frame,
                    planetRadius = atmosphereParameter.planetRadius,
                    atmosphereHeight = atmosphereParameter.atmosphereHeight
                });
                LightLoopInputs lightLoop = LightTileMaskPass.QueryLightLoop(m_RGBuilder, context);
                lightLoop.Read(passHandle);
                passData.lightLoop = lightLoop;
                ShadowInputs shadows = ShadowInputs.Query(m_RGBuilder, context);
                shadows.Read(passHandle);
                passData.shadows = shadows;
                passData.lightRecordBufferHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                passData.volumetricCloudShader = m_Resources.shaders.volumetricCloudShader;
                passData.cloudKernel = m_Resources.kernels.volumetricCloud;
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                passData.transmittanceLUTHandle = passHandle.ReadTexture(transmittanceLUTHandle);
                passData.historyTextureHandle = passHandle.ReadTexture(historyTextureHandle);
                passData.volumetricCloudTextureHandle = passHandle.WriteTexture(volumetricCloudTextureHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in VolumetricCloudPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.volumetricCloudShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, VolumetricCloudPassUtilityData.CBV_VolumetricCloudParametersID, passData.parametersHandle);
                    cmdEncoder.SetComputeBufferParam(shader, passData.cloudKernel.index, LightShaderIDs.LightRecordBuffer, passData.lightRecordBufferHandle);
                    passData.lightLoop.Bind(cmdEncoder, shader, passData.cloudKernel.index);
                    passData.shadows.Bind(cmdEncoder, shader, passData.cloudKernel.index);

                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, PassShaderIDs.SRV_TransmittanceLUT, passData.transmittanceLUTHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, VolumetricCloudPassUtilityData.SRV_HistoryVolumetricCloudID, passData.historyTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.cloudKernel.index, VolumetricCloudPassUtilityData.UAV_VolumetricCloudTextureID, passData.volumetricCloudTextureHandle);
                    cmdEncoder.BeginSample(VolumetricCloudPassUtilityData.VolCloudTraceSampler);
                    cmdEncoder.DispatchCompute(shader, passData.cloudKernel.index, passData.cloudKernel.GroupsX(passData.resolution.x), passData.cloudKernel.GroupsY(passData.resolution.y), 1);
                    cmdEncoder.EndSample(VolumetricCloudPassUtilityData.VolCloudTraceSampler);
                });
            }

            context.features.MarkProduced(EFrameFeature.VolumetricCloud);
        }

        struct CopyHistoryVolumetricCloudPassData
        {
            public RGTextureHandle sourceHandle;
            public RGTextureHandle historyHandle;
        }

        internal void CopyHistoryVolumetricCloud(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricCloud))
            {
                return;
            }
            RGTextureHandle sourceHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricCloudBuffer);

            int cloudWidth = Mathf.Max(1, context.dimensions.internalSize.x >> 1);
            int cloudHeight = Mathf.Max(1, context.dimensions.internalSize.y >> 1);
            TextureDescriptor historyDsc = CreateVolumetricCloudDescriptor(cloudWidth, cloudHeight, VolumetricCloudPassUtilityData.HistoryTextureName, false);
            RGTextureHandle historyHandle = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyDsc));
            context.historyCache.MarkProduced(InfinityShaderIDs.HistoryVolumetricCloudBuffer);

            {
                RGTransferPassHandle<CopyHistoryVolumetricCloudPassData> passHandle = m_RGBuilder.AddTransferPass<CopyHistoryVolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryVolumetricCloud));
                passHandle.ReadTexture(sourceHandle);
                passHandle.WriteTexture(historyHandle);
                ref CopyHistoryVolumetricCloudPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = sourceHandle;
                passData.historyHandle = historyHandle;
                passHandle.SetExecuteFunc((in CopyHistoryVolumetricCloudPassData passData, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(passData.sourceHandle, passData.historyHandle);
                });
            }
        }
    }
}
