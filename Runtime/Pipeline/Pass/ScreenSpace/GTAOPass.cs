using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Volume;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_OcclusionParameters Shaders/Pass/ScreenSpace/Common.hlsl
    [StructLayout(LayoutKind.Sequential)]
    internal struct OcclusionParameters
    {
        public int numRays;
        public int numSteps;
        public float power;
        public float radius;
        public float intensity;
        public float sharpness;
        public float halfProjScale;
        public float temporalOffset;
        public float temporalDirection;
        public float temporalScale;
        public float temporalWeight;
        public int historyValid;
        public Vector4 resolution;
        public Vector4 upsampleSize;
    }

    internal static class GTAOPassUtilityData
    {
        internal static readonly int CBV_OcclusionParametersID = Shader.PropertyToID("CBV_OcclusionParameters");
        internal static readonly int SRV_HalfResDepthTextureID = Shader.PropertyToID("SRV_HalfResDepthTexture");
        internal static readonly int SRV_HalfResNormalTextureID = Shader.PropertyToID("SRV_HalfResNormalTexture");
        internal static readonly ProfilingSampler GTAOTraceSampler = new ProfilingSampler("GTAO_Trace");
        internal static readonly ProfilingSampler GTAOSpatialXSampler = new ProfilingSampler("GTAO_SpatialX");
        internal static readonly ProfilingSampler GTAOSpatialYSampler = new ProfilingSampler("GTAO_SpatialY");
        internal static readonly ProfilingSampler GTAOTemporalSampler = new ProfilingSampler("GTAO_Temporal");
        internal static readonly ProfilingSampler GTAOUpsampleSampler = new ProfilingSampler("GTAO_Upsample");
        internal static readonly string OcclusionTextureName = "OcclusionTexture";
        internal static readonly string OcclusionHalfTextureName = "OcclusionHalfTexture";
        internal static readonly string SpatialTempTextureName = "SpatialTempTexture";
        internal static readonly string HistoryOcclusionTextureName = "HistoryOcclusionTexture";
        internal static readonly int SRV_HistoryTextureID = Shader.PropertyToID("SRV_HistoryTexture");
        internal static readonly int UAV_SpatialTextureID = Shader.PropertyToID("UAV_SpatialTexture");
        internal static readonly int UAV_AccumulationTextureID = Shader.PropertyToID("UAV_AccumulationTexture");
        internal static readonly int PendingHistoryID = Shader.PropertyToID("UAV_OcclusionHistoryTexture");
        internal static readonly int UAV_UpsampleTextureID = Shader.PropertyToID("UAV_UpsampleTexture");

        internal static readonly float[] TemporalOffsets = { 0.0f, 0.5f, 0.25f, 0.75f };
        internal static readonly float[] TemporalDirections = { 0.0f, 0.5f, 0.25f, 0.75f };
    }

    internal sealed class GTAOPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly RenderCapturePass m_RenderCapturePass;
#endif

        internal GTAOPass(RGBuilder graph, InfinityRenderPipelineResources resources
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            , RenderCapturePass renderCapturePass
#endif
            )
        {
            m_RGBuilder = graph;
            m_Resources = resources;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_RenderCapturePass = renderCapturePass;
#endif
        }

        struct GTAOPassData
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public RenderCapturePass.CaptureStageSnapshot traceSnapshot, spatialXSnapshot, spatialYSnapshot, temporalSnapshot;
#endif
            public RGConstantsHandle parametersHandle;
            public int traceKernel, spatialXKernel, spatialYKernel, temporalKernel, upsampleKernel;
            public int2 halfGroups, fullGroups;
            public ComputeShader ssaoShader;
            public RGTextureHandle halfResDepthTextureHandle;
            public RGTextureHandle halfResNormalTextureHandle;
            public RGTextureHandle fullResDepthTextureHandle;
            public RGTextureHandle fullResNormalTextureHandle;
            public RGTextureHandle motionTextureHandle, pendingOcclusionTextureHandle;
            public RGTextureHandle historyOcclusionTextureHandle;
            public RGTextureHandle occlusionHalfTextureHandle;
            public RGTextureHandle spatialTempTextureHandle;
            public RGTextureHandle occlusionTextureHandle;
        }

        static TextureDescriptor CreateGTAOHalfDescriptor(int width, int height, string name, GraphicsFormat format)
        {
            return TextureDescriptor.Create2D(name, width, height, format, ETextureSizingDomain.Internal, 2);
        }

        internal void ComputeGroundTruthOcclusion(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.GTAO))
            {
                return;
            }

            GTAOSettings ssao = context.volumes.screenSpaceAmbientOcclusion.Resolve();

            int fullWidth = context.dimensions.internalSize.x;
            int fullHeight = context.dimensions.internalSize.y;
            int halfWidth = Mathf.Max(1, fullWidth >> 1);
            int halfHeight = Mathf.Max(1, fullHeight >> 1);

            TextureDescriptor occlusionTextureDsc = new TextureDescriptor(fullWidth, fullHeight, ETextureSizingDomain.Internal);
            {
                occlusionTextureDsc.name = GTAOPassUtilityData.OcclusionTextureName;
                occlusionTextureDsc.dimension = TextureDimension.Tex2D;
                occlusionTextureDsc.colorFormat = GraphicsFormat.R8_UNorm;
                occlusionTextureDsc.enableRandomWrite = true;
                occlusionTextureDsc.filterMode = FilterMode.Bilinear;
                occlusionTextureDsc.wrapMode = TextureWrapMode.Clamp;
            }
            RGTextureHandle occlusionTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionBuffer, occlusionTextureDsc);

            TextureDescriptor occlusionHalfDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.OcclusionHalfTextureName, GraphicsFormat.R16_SFloat);
            RGTextureHandle occlusionHalfTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionHalfBuffer, occlusionHalfDsc);

            TextureDescriptor spatialTempDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.SpatialTempTextureName, GraphicsFormat.R16_SFloat);
            RGTextureHandle spatialTempTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SpatialTempBuffer, spatialTempDsc);

            TextureDescriptor historyAODsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.HistoryOcclusionTextureName, GraphicsFormat.R16G16B16A16_SFloat);
            HistoryRead occlusionRead = context.historyCache.Read(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc, context.resetsHistory);
            RGTextureHandle historyOcclusionTextureHandle = m_RGBuilder.ImportTexture(occlusionRead.textureHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyOcclusionTextureHandle);
            var pendingOcclusion = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc));
            context.historyCache.MarkProduced(InfinityShaderIDs.HistoryOcclusionBuffer);

            RGTextureHandle halfResDepthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HalfResDepthBuffer);
            RGTextureHandle halfResNormalTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HalfResNormalBuffer);
            RGTextureHandle fullResDepthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle fullResNormalTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle motionTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            {
                RGComputePassHandle<GTAOPassData> passHandle = m_RGBuilder.AddComputePass<GTAOPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeGroundTruthOcclusion));
                ref GTAOPassData passData = ref passHandle.GetPassData();
                passData = default;
                passData.traceKernel = m_Resources.kernels.occlusionTrace.index;
                passData.spatialXKernel = m_Resources.kernels.occlusionSpatialX.index;
                passData.spatialYKernel = m_Resources.kernels.occlusionSpatialY.index;
                passData.temporalKernel = m_Resources.kernels.occlusionTemporal.index;
                passData.upsampleKernel = m_Resources.kernels.occlusionUpsample.index;
                passData.halfGroups = new int2(m_Resources.kernels.occlusionTrace.GroupsX(halfWidth), m_Resources.kernels.occlusionTrace.GroupsY(halfHeight));
                passData.fullGroups = new int2(m_Resources.kernels.occlusionUpsample.GroupsX(fullWidth), m_Resources.kernels.occlusionUpsample.GroupsY(fullHeight));
                int temporalIndex = context.frame & 3;
                passData.parametersHandle = passHandle.WriteConstants(new OcclusionParameters
                {
                    numRays = ssao.numRays,
                    numSteps = ssao.numSteps,
                    power = ssao.power,
                    radius = ssao.radius,
                    intensity = ssao.intensity,
                    sharpness = ssao.sharpness,
                    halfProjScale = context.cameraUniform.matrix_FlipYJitterProj.m11 * halfHeight * 0.5f,
                    temporalOffset = GTAOPassUtilityData.TemporalOffsets[temporalIndex],
                    temporalDirection = GTAOPassUtilityData.TemporalDirections[temporalIndex],
                    temporalScale = ssao.temporalScale,
                    temporalWeight = ssao.temporalWeight,
                    historyValid = context.state.GetScreenSpaceState().acceptedGTAO && occlusionRead.valid ? 1 : 0,
                    resolution = new Vector4(halfWidth, halfHeight, 1.0f / halfWidth, 1.0f / halfHeight),
                    upsampleSize = new Vector4(fullWidth, fullHeight, 1.0f / fullWidth, 1.0f / fullHeight)
                });

                passData.ssaoShader = m_Resources.shaders.ssaoShader;
                passData.halfResDepthTextureHandle = passHandle.ReadTexture(halfResDepthTextureHandle);
                passData.halfResNormalTextureHandle = passHandle.ReadTexture(halfResNormalTextureHandle);
                passData.fullResDepthTextureHandle = passHandle.ReadTexture(fullResDepthTextureHandle);
                passData.fullResNormalTextureHandle = passHandle.ReadTexture(fullResNormalTextureHandle);
                passData.motionTextureHandle = passHandle.ReadTexture(motionTextureHandle);
                passData.pendingOcclusionTextureHandle = passHandle.WriteTexture(pendingOcclusion);

                passData.historyOcclusionTextureHandle = passHandle.ReadTexture(historyOcclusionTextureHandle);
                passData.occlusionHalfTextureHandle = passHandle.WriteTexture(occlusionHalfTextureHandle);
                passData.spatialTempTextureHandle = passHandle.WriteTexture(spatialTempTextureHandle);
                passData.occlusionTextureHandle = passHandle.WriteTexture(occlusionTextureHandle);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.traceSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.AOTrace, occlusionHalfTextureHandle);
                passData.spatialXSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.AOSpatialX, spatialTempTextureHandle);
                passData.spatialYSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.AOSpatialY, occlusionHalfTextureHandle);
                passData.temporalSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.AOTemporal, spatialTempTextureHandle);
#endif
                passHandle.EnablePassCulling(false);
                passHandle.EnableAsyncCompute(true);
                passHandle.SetExecuteFunc((in GTAOPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.ssaoShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, GTAOPassUtilityData.CBV_OcclusionParametersID, passData.parametersHandle);

                    int halfGroupsX = passData.halfGroups.x;
                    int halfGroupsY = passData.halfGroups.y;
                    int fullGroupsX = passData.fullGroups.x;
                    int fullGroupsY = passData.fullGroups.y;

                    int trace = passData.traceKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOTraceSampler);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_NormalTexture, passData.halfResNormalTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.UAV_OcclusionTexture, passData.occlusionHalfTextureHandle);
                    cmdEncoder.DispatchCompute(shader, trace, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOTraceSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.traceSnapshot.Record(cmdEncoder, passData.occlusionHalfTextureHandle);
#endif

                    int spatialX = passData.spatialXKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOSpatialXSampler);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionHalfTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.UAV_SpatialTextureID, passData.spatialTempTextureHandle);
                    cmdEncoder.DispatchCompute(shader, spatialX, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOSpatialXSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.spatialXSnapshot.Record(cmdEncoder, passData.spatialTempTextureHandle);
#endif

                    int spatialY = passData.spatialYKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOSpatialYSampler);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, PassShaderIDs.SRV_OcclusionTexture, passData.spatialTempTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.UAV_SpatialTextureID, passData.occlusionHalfTextureHandle);
                    cmdEncoder.DispatchCompute(shader, spatialY, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOSpatialYSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.spatialYSnapshot.Record(cmdEncoder, passData.occlusionHalfTextureHandle);
#endif

                    int temporal = passData.temporalKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOTemporalSampler);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_NormalTexture, passData.halfResNormalTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionHalfTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_HistoryTextureID, passData.historyOcclusionTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_MotionTexture, passData.motionTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.UAV_AccumulationTextureID, passData.spatialTempTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.PendingHistoryID, passData.pendingOcclusionTextureHandle);
                    cmdEncoder.DispatchCompute(shader, temporal, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOTemporalSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.temporalSnapshot.Record(cmdEncoder, passData.spatialTempTextureHandle);
#endif

                    int upsample = passData.upsampleKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOUpsampleSampler);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.SRV_HalfResDepthTextureID, passData.halfResDepthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.SRV_HalfResNormalTextureID, passData.halfResNormalTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_DepthTexture, passData.fullResDepthTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_NormalTexture, passData.fullResNormalTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_OcclusionTexture, passData.spatialTempTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.UAV_UpsampleTextureID, passData.occlusionTextureHandle);
                    cmdEncoder.DispatchCompute(shader, upsample, fullGroupsX, fullGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOUpsampleSampler);
                });
            }

            context.features.MarkProduced(EFrameFeature.GTAO);
        }

    }
}
