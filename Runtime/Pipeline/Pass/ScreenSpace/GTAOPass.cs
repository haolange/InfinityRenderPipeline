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
    internal static class GTAOPassUtilityData
    {
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
        internal static readonly string HistoryOcclusionDepthTextureName = "HistoryOcclusionDepthTexture";
        internal static readonly int NumRayID = Shader.PropertyToID("NumRay");
        internal static readonly int NumStepID = Shader.PropertyToID("NumStep");
        internal static readonly int PowerID = Shader.PropertyToID("Power");
        internal static readonly int RadiusID = Shader.PropertyToID("Radius");
        internal static readonly int IntensityID = Shader.PropertyToID("Intensity");
        internal static readonly int SharpenessID = Shader.PropertyToID("Sharpeness");
        internal static readonly int HalfProjScaleID = Shader.PropertyToID("HalfProjScale");
        internal static readonly int TemporalOffsetID = Shader.PropertyToID("TemporalOffset");
        internal static readonly int TemporalDirectionID = Shader.PropertyToID("TemporalDirection");
        internal static readonly int TemporalScaleID = Shader.PropertyToID("TemporalScale");
        internal static readonly int TemporalWeightID = Shader.PropertyToID("TemporalWeight");
        internal static readonly int ResolutionID = Shader.PropertyToID("Resolution");
        internal static readonly int UpsampleSizeID = Shader.PropertyToID("UpsampleSize");
        internal static readonly int SRV_HistoryTextureID = Shader.PropertyToID("SRV_HistoryTexture");
        internal static readonly int UAV_SpatialTextureID = Shader.PropertyToID("UAV_SpatialTexture");
        internal static readonly int UAV_AccumulationTextureID = Shader.PropertyToID("UAV_AccumulationTexture");
        internal static readonly int HistoryValidID = Shader.PropertyToID("GTAOHistoryValid");
        internal static readonly int MotionMetadataID = Shader.PropertyToID("SRV_MotionMetadataTexture");
        internal static readonly int PendingHistoryID = Shader.PropertyToID("UAV_OcclusionHistoryTexture");
        internal static readonly int PendingDepthID = Shader.PropertyToID("UAV_OcclusionHistoryDepth");
        internal static readonly int UAV_UpsampleTextureID = Shader.PropertyToID("UAV_UpsampleTexture");

        internal static readonly float[] TemporalOffsets = { 0.0f, 0.5f, 0.25f, 0.75f };
        internal static readonly float[] TemporalDirections = { 0.0f, 0.5f, 0.25f, 0.75f };
    }

    public partial class InfinityRenderPipeline
    {
        struct GTAOPassData
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public CaptureStageSnapshot traceSnapshot, spatialXSnapshot, spatialYSnapshot, temporalSnapshot;
#endif
            public int numRays;
            public bool historyValid;
            public int traceKernel, spatialXKernel, spatialYKernel, temporalKernel, upsampleKernel;
            public int2 halfGroups, fullGroups;
            public int numSteps;
            public float power;
            public float radius;
            public float intensity;
            public float sharpeness;
            public float halfProjScale;
            public float temporalOffset;
            public float temporalDirection;
            public float temporalScale;
            public float temporalWeight;
            public int2 halfResolution;
            public int2 fullResolution;
            public Vector4 depthParameters;
            public Matrix4x4 historyViewProj, motionViewProj, lastMotionViewProj;
            public Matrix4x4 matrix_Proj;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_ViewProj;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_ViewToWorld;
            public Matrix4x4 matrix_WorldToView;
            public ComputeShader ssaoShader;
            public RGTextureHandle halfResDepthTexture;
            public RGTextureHandle halfResNormalTexture;
            public RGTextureHandle fullResDepthTexture;
            public RGTextureHandle fullResNormalTexture;
            public RGTextureHandle motionTexture, motionMetadata, pendingOcclusionTexture, pendingDepthTexture;
            public RGTextureHandle historyOcclusionTexture;
            public RGTextureHandle historyOcclusionDepthTexture;
            public RGTextureHandle occlusionHalfTexture;
            public RGTextureHandle spatialTempTexture;
            public RGTextureHandle occlusionTexture;
        }

        static TextureDescriptor CreateGTAOHalfDescriptor(int width, int height, string name, GraphicsFormat format)
        {
            return TextureDescriptor.Create2D(name, width, height, format, ETextureSizingDomain.Internal, 2);
        }

        void ComputeGroundTruthOcclusion(in CameraRecordContext context)
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
            RGTextureHandle occlusionTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionBuffer, occlusionTextureDsc);

            TextureDescriptor occlusionHalfDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.OcclusionHalfTextureName, GraphicsFormat.R16_SFloat);
            RGTextureHandle occlusionHalfTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionHalfBuffer, occlusionHalfDsc);

            TextureDescriptor spatialTempDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.SpatialTempTextureName, GraphicsFormat.R16_SFloat);
            RGTextureHandle spatialTempTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SpatialTempBuffer, spatialTempDsc);

            TextureDescriptor historyAODsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.HistoryOcclusionTextureName, GraphicsFormat.R16G16B16A16_SFloat);
            TextureDescriptor historyDepthDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.HistoryOcclusionDepthTextureName, GraphicsFormat.R32_SFloat);
            HistoryRead occlusionRead = context.historyCache.Read(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc, context.resetsHistory);
            HistoryRead occlusionDepthRead = context.historyCache.Read(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyDepthDsc, context.resetsHistory);
            RGTextureHandle historyOcclusionTexture = m_RGBuilder.ImportTexture(occlusionRead.texture);
            RGTextureHandle historyOcclusionDepthTexture = m_RGBuilder.ImportTexture(occlusionDepthRead.texture);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyOcclusionTexture);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyOcclusionDepthTexture);
            var pendingOcclusion = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc));
            var pendingDepth = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyDepthDsc));
            context.historyCache.MarkProduced(InfinityShaderIDs.HistoryOcclusionBuffer);
            context.historyCache.MarkProduced(InfinityShaderIDs.HistoryOcclusionDepthBuffer);

            RGTextureHandle halfResDepthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HalfResDepthBuffer);
            RGTextureHandle halfResNormalTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HalfResNormalBuffer);
            RGTextureHandle fullResDepthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle fullResNormalTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle motionTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            using (var passRef = m_RGBuilder.AddComputePass<GTAOPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeGroundTruthOcclusion)))
            {
                ref GTAOPassData passData = ref passRef.GetPassData();
                passData = default;
                passData.traceKernel = kernels.occlusionTrace.index;
                passData.spatialXKernel = kernels.occlusionSpatialX.index;
                passData.spatialYKernel = kernels.occlusionSpatialY.index;
                passData.temporalKernel = kernels.occlusionTemporal.index;
                passData.upsampleKernel = kernels.occlusionUpsample.index;
                passData.halfGroups = new int2(kernels.occlusionTrace.GroupsX(halfWidth), kernels.occlusionTrace.GroupsY(halfHeight));
                passData.fullGroups = new int2(kernels.occlusionUpsample.GroupsX(fullWidth), kernels.occlusionUpsample.GroupsY(fullHeight));
                passData.numRays = ssao.numRays;
                passData.numSteps = ssao.numSteps;
                passData.power = ssao.power;
                passData.radius = ssao.radius;
                passData.intensity = ssao.intensity;
                passData.sharpeness = ssao.sharpness;
                passData.temporalScale = ssao.temporalScale;
                passData.temporalWeight = ssao.temporalWeight;
                passData.historyValid = context.state.acceptedGTAO && occlusionRead.valid && occlusionDepthRead.valid;
                passData.halfResolution = new int2(halfWidth, halfHeight);
                passData.fullResolution = new int2(fullWidth, fullHeight);

                passData.halfProjScale = context.cameraUniform.matrix_FlipYJitterProj.m11 * halfHeight * 0.5f;

                int temporalIndex = Time.frameCount & 3;
                passData.temporalOffset = GTAOPassUtilityData.TemporalOffsets[temporalIndex];
                passData.temporalDirection = GTAOPassUtilityData.TemporalDirections[temporalIndex];

                passData.historyViewProj = context.cameraUniform.matrix_LastViewFlipYJitterProj;
                passData.motionViewProj = context.cameraUniform.matrix_ViewFlipYProj;
                passData.lastMotionViewProj = context.cameraUniform.matrix_LastViewFlipYProj;
                passData.depthParameters = new Vector4(context.camera.nearClipPlane, context.camera.farClipPlane, context.camera.orthographic ? 1 : 0, SystemInfo.usesReversedZBuffer ? 1 : 0);
                passData.matrix_Proj = context.cameraUniform.matrix_FlipYJitterProj;
                passData.matrix_InvProj = context.cameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_ViewProj = context.cameraUniform.matrix_ViewFlipYJitterProj;
                passData.matrix_InvViewProj = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_ViewToWorld = context.cameraUniform.matrix_ViewToWorld;
                passData.matrix_WorldToView = context.cameraUniform.matrix_WorldToView;
                passData.ssaoShader = shaders.ssaoShader;
                passData.halfResDepthTexture = passRef.ReadTexture(halfResDepthTexture);
                passData.halfResNormalTexture = passRef.ReadTexture(halfResNormalTexture);
                passData.fullResDepthTexture = passRef.ReadTexture(fullResDepthTexture);
                passData.fullResNormalTexture = passRef.ReadTexture(fullResNormalTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.motionMetadata = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                passData.pendingOcclusionTexture = passRef.WriteTexture(pendingOcclusion);
                passData.pendingDepthTexture = passRef.WriteTexture(pendingDepth);

                passData.historyOcclusionTexture = passRef.ReadTexture(historyOcclusionTexture);
                passData.historyOcclusionDepthTexture = passRef.ReadTexture(historyOcclusionDepthTexture);
                passData.occlusionHalfTexture = passRef.WriteTexture(occlusionHalfTexture);
                passData.spatialTempTexture = passRef.WriteTexture(spatialTempTexture);
                passData.occlusionTexture = passRef.WriteTexture(occlusionTexture);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.traceSnapshot = PrepareStageSnapshot(passRef, "AOTrace", occlusionHalfTexture);
                passData.spatialXSnapshot = PrepareStageSnapshot(passRef, "AOSpatialX", spatialTempTexture);
                passData.spatialYSnapshot = PrepareStageSnapshot(passRef, "AOSpatialY", occlusionHalfTexture);
                passData.temporalSnapshot = PrepareStageSnapshot(passRef, "AOTemporal", spatialTempTexture);
#endif
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in GTAOPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.ssaoShader;
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.ScreenSpaceDepthParams, passData.depthParameters);
                    cmdEncoder.SetComputeMatrixParam(shader, PassShaderIDs.Matrix_HistoryViewProj, passData.historyViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, PassShaderIDs.Matrix_MotionViewProj, passData.motionViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, PassShaderIDs.Matrix_LastMotionViewProj, passData.lastMotionViewProj);
                    int halfWidth = passData.halfResolution.x;
                    int halfHeight = passData.halfResolution.y;
                    int fullWidth = passData.fullResolution.x;
                    int fullHeight = passData.fullResolution.y;

                    cmdEncoder.SetComputeIntParam(shader, GTAOPassUtilityData.NumRayID, passData.numRays);
                    cmdEncoder.SetComputeIntParam(shader, GTAOPassUtilityData.NumStepID, passData.numSteps);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.PowerID, passData.power);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.RadiusID, passData.radius);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.IntensityID, passData.intensity);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.SharpenessID, passData.sharpeness);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.HalfProjScaleID, passData.halfProjScale);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.TemporalOffsetID, passData.temporalOffset);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.TemporalDirectionID, passData.temporalDirection);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.TemporalScaleID, passData.temporalScale);
                    cmdEncoder.SetComputeFloatParam(shader, GTAOPassUtilityData.TemporalWeightID, passData.temporalWeight);
                    cmdEncoder.SetComputeVectorParam(shader, GTAOPassUtilityData.ResolutionID, new Vector4(halfWidth, halfHeight, 1.0f / halfWidth, 1.0f / halfHeight));
                    cmdEncoder.SetComputeVectorParam(shader, GTAOPassUtilityData.UpsampleSizeID, new Vector4(fullWidth, fullHeight, 1.0f / fullWidth, 1.0f / fullHeight));
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_Proj, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvProj, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_ViewProj, passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvViewProj, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_ViewToWorld, passData.matrix_ViewToWorld);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_WorldToView, passData.matrix_WorldToView);

                    int halfGroupsX = passData.halfGroups.x;
                    int halfGroupsY = passData.halfGroups.y;
                    int fullGroupsX = passData.fullGroups.x;
                    int fullGroupsY = passData.fullGroups.y;

                    int trace = passData.traceKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOTraceSampler);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_NormalTexture, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.UAV_OcclusionTexture, passData.occlusionHalfTexture);
                    cmdEncoder.DispatchCompute(shader, trace, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOTraceSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.traceSnapshot.Record(cmdEncoder, passData.occlusionHalfTexture);
#endif

                    int spatialX = passData.spatialXKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOSpatialXSampler);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionHalfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.UAV_SpatialTextureID, passData.spatialTempTexture);
                    cmdEncoder.DispatchCompute(shader, spatialX, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOSpatialXSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.spatialXSnapshot.Record(cmdEncoder, passData.spatialTempTexture);
#endif

                    int spatialY = passData.spatialYKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOSpatialYSampler);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, PassShaderIDs.SRV_OcclusionTexture, passData.spatialTempTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.UAV_SpatialTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.DispatchCompute(shader, spatialY, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOSpatialYSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.spatialYSnapshot.Record(cmdEncoder, passData.occlusionHalfTexture);
#endif

                    int temporal = passData.temporalKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOTemporalSampler);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_DepthTexture, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_NormalTexture, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionHalfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_HistoryTextureID, passData.historyOcclusionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_HistoryDepthTexture, passData.historyOcclusionDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.UAV_AccumulationTextureID, passData.spatialTempTexture);
                    cmdEncoder.SetComputeIntParam(shader, GTAOPassUtilityData.HistoryValidID, passData.historyValid ? 1 : 0);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.MotionMetadataID, passData.motionMetadata);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.PendingHistoryID, passData.pendingOcclusionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.PendingDepthID, passData.pendingDepthTexture);
                    cmdEncoder.DispatchCompute(shader, temporal, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOTemporalSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.temporalSnapshot.Record(cmdEncoder, passData.spatialTempTexture);
#endif

                    int upsample = passData.upsampleKernel;
                    cmdEncoder.BeginSample(GTAOPassUtilityData.GTAOUpsampleSampler);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.SRV_HalfResDepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.SRV_HalfResNormalTextureID, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_DepthTexture, passData.fullResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_NormalTexture, passData.fullResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_OcclusionTexture, passData.spatialTempTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.UAV_UpsampleTextureID, passData.occlusionTexture);
                    cmdEncoder.DispatchCompute(shader, upsample, fullGroupsX, fullGroupsY, 1);
                    cmdEncoder.EndSample(GTAOPassUtilityData.GTAOUpsampleSampler);
                });
            }

            context.features.MarkProduced(EFrameFeature.GTAO);
        }

    }
}
