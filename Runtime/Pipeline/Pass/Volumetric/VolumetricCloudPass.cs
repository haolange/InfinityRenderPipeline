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

    public partial class InfinityRenderPipeline
    {
        struct VolumetricCloudPassData
        {
            public RGConstantsHandle parameters;
            public int2 resolution;
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
            TextureDescriptor descriptor = TextureDescriptor.Create2D(name, width, height, GraphicsFormat.R16G16B16A16_SFloat, ETextureSizingDomain.Internal, 2);
            descriptor.enableRandomWrite = randomWrite;
            return descriptor;
        }

        void ComputeVolumetricCloud(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricCloud))
            {
                return;
            }

            var volCloud = context.volumes.volumetricCloud;
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
            HistoryRead historyRead = context.historyCache.Read(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyDsc, context.resetsHistory);
            RGTextureHandle historyTexture = m_RGBuilder.ImportTexture(historyRead.texture);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryVolumetricCloudBuffer, historyTexture);

            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle transmittanceLUT = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereTransmittanceLUT);
            AtmosphereParameter atmosphereParameter = context.atmosphere;

            using (var passRef = m_RGBuilder.AddComputePass<VolumetricCloudPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeVolumetricCloud)))
            {
                ref VolumetricCloudPassData passData = ref passRef.GetPassData();
                passData.resolution = new int2(cloudWidth, cloudHeight);
                passData.parameters = passRef.WriteConstants(new VolumetricCloudParameters
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
                    frameIndex = Time.frameCount,
                    planetRadius = atmosphereParameter.planetRadius,
                    atmosphereHeight = atmosphereParameter.atmosphereHeight
                });
                LightLoopInputs lightLoop = QueryLightLoop(context);
                lightLoop.Read(passRef);
                passData.lightLoop = lightLoop;
                ShadowInputs shadows = QueryShadowInputs(context);
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
                    cmdEncoder.SetComputeConstantBufferParam(shader, VolumetricCloudPassUtilityData.CBV_VolumetricCloudParametersID, passData.parameters);
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
            if (!context.features.ShouldRecord(EFrameFeature.VolumetricCloud))
            {
                return;
            }
            RGTextureHandle source = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricCloudBuffer);

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
