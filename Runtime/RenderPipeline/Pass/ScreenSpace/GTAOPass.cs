using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class GTAOPassUtilityData
    {
        internal static string OcclusionTextureName = "OcclusionTexture";
        internal static string OcclusionHalfTextureName = "OcclusionHalfTexture";
        internal static string SpatialTempTextureName = "SpatialTempTexture";
        internal static string HistoryOcclusionTextureName = "HistoryOcclusionTexture";
        internal static string HistoryOcclusionDepthTextureName = "HistoryOcclusionDepthTexture";
        internal static int OcclusionTraceKernel;
        internal static int OcclusionSpatialXKernel;
        internal static int OcclusionSpatialYKernel;
        internal static int OcclusionTemporalKernel;
        internal static int OcclusionUpsampleKernel;
        static ComputeShader s_KernelShader;
        internal static void ResolveKernels(ComputeShader shader)
        {
            if (s_KernelShader == shader) return;
            OcclusionTraceKernel = shader.FindKernel("OcclusionTrace");
            OcclusionSpatialXKernel = shader.FindKernel("OcclusionSpatialX");
            OcclusionSpatialYKernel = shader.FindKernel("OcclusionSpatialY");
            OcclusionTemporalKernel = shader.FindKernel("OcclusionTemporal");
            OcclusionUpsampleKernel = shader.FindKernel("OcclusionUpsample");
            s_KernelShader = shader;
        }

        internal static int NumRayID = Shader.PropertyToID("NumRay");
        internal static int NumStepID = Shader.PropertyToID("NumStep");
        internal static int PowerID = Shader.PropertyToID("Power");
        internal static int RadiusID = Shader.PropertyToID("Radius");
        internal static int IntensityID = Shader.PropertyToID("Intensity");
        internal static int SharpenessID = Shader.PropertyToID("Sharpeness");
        internal static int HalfProjScaleID = Shader.PropertyToID("HalfProjScale");
        internal static int TemporalOffsetID = Shader.PropertyToID("TemporalOffset");
        internal static int TemporalDirectionID = Shader.PropertyToID("TemporalDirection");
        internal static int TemporalScaleID = Shader.PropertyToID("TemporalScale");
        internal static int TemporalWeightID = Shader.PropertyToID("TemporalWeight");
        internal static int ResolutionID = Shader.PropertyToID("Resolution");
        internal static int UpsampleSizeID = Shader.PropertyToID("UpsampleSize");
        internal static int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static int Matrix_InvProjID = Shader.PropertyToID("Matrix_InvProj");
        internal static int Matrix_ViewProjID = Shader.PropertyToID("Matrix_ViewProj");
        internal static int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static int Matrix_ViewToWorldID = Shader.PropertyToID("Matrix_ViewToWorld");
        internal static int Matrix_WorldToViewID = Shader.PropertyToID("Matrix_WorldToView");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_NormalTextureID = Shader.PropertyToID("SRV_NormalTexture");
        internal static int SRV_OcclusionTextureID = Shader.PropertyToID("SRV_OcclusionTexture");
        internal static int SRV_HistoryTextureID = Shader.PropertyToID("SRV_HistoryTexture");
        internal static int SRV_HistoryDepthTextureID = Shader.PropertyToID("SRV_HistoryDepthTexture");
        internal static int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static int UAV_OcclusionTextureID = Shader.PropertyToID("UAV_OcclusionTexture");
        internal static int UAV_SpatialTextureID = Shader.PropertyToID("UAV_SpatialTexture");
        internal static int UAV_AccumulationTextureID = Shader.PropertyToID("UAV_AccumulationTexture");
        internal static readonly int HistoryValidID = Shader.PropertyToID("GTAOHistoryValid");
        internal static readonly int MotionMetadataID = Shader.PropertyToID("SRV_MotionMetadataTexture");
        internal static readonly int PendingHistoryID = Shader.PropertyToID("UAV_OcclusionHistoryTexture");
        internal static readonly int PendingDepthID = Shader.PropertyToID("UAV_OcclusionHistoryDepth");
        internal static int UAV_UpsampleTextureID = Shader.PropertyToID("UAV_UpsampleTexture");

        internal static readonly float[] TemporalOffsets = { 0.0f, 0.5f, 0.25f, 0.75f };
        internal static readonly float[] TemporalDirections = { 0.0f, 0.5f, 0.25f, 0.75f };
    }

    public partial class InfinityRenderPipeline
    {
        struct GTAOPassData
        {
            public CaptureStageSnapshot traceSnapshot, spatialXSnapshot, spatialYSnapshot, temporalSnapshot;
            public int numRays;
            public bool historyValid;
            public int traceKernel, spatialXKernel, spatialYKernel, temporalKernel, upsampleKernel;
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
            public RGTextureRef halfResDepthTexture;
            public RGTextureRef halfResNormalTexture;
            public RGTextureRef fullResDepthTexture;
            public RGTextureRef fullResNormalTexture;
            public RGTextureRef motionTexture, motionMetadata, pendingOcclusionTexture, pendingDepthTexture;
            public RGTextureRef historyOcclusionTexture;
            public RGTextureRef historyOcclusionDepthTexture;
            public RGTextureRef occlusionHalfTexture;
            public RGTextureRef spatialTempTexture;
            public RGTextureRef occlusionTexture;
        }

        static TextureDescriptor CreateGTAOHalfDescriptor(int width, int height, string name, GraphicsFormat format)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal, 2);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = format;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = true;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
        }

        static TextureDescriptor CreateGTAOHistoryAODescriptor(int width, int height)
        {
            TextureDescriptor descriptor = CreateGTAOHalfDescriptor(width, height, GTAOPassUtilityData.HistoryOcclusionTextureName, GraphicsFormat.R16G16B16A16_SFloat);
            descriptor.enableRandomWrite = true;
            return descriptor;
        }

        static TextureDescriptor CreateGTAOHistoryDepthDescriptor(int width, int height)
        {
            TextureDescriptor descriptor = CreateGTAOHalfDescriptor(width, height, GTAOPassUtilityData.HistoryOcclusionDepthTextureName, GraphicsFormat.R32_SFloat);
            descriptor.enableRandomWrite = true;
            return descriptor;
        }

        void ComputeGroundTruthOcclusion(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.GTAO))
            {
                return;
            }

            FGTAOSettings ssao = ActiveVolumeStack.GetComponent<ScreenSpaceAmbientOcclusion>().Resolve();
            if (!GraphicsUtility.HasRequiredKernels(shaders.ssaoShader, "OcclusionTrace", "OcclusionSpatialX", "OcclusionSpatialY", "OcclusionTemporal", "OcclusionUpsample"))
            {
                return;
            }

            int fullWidth = m_ActiveFrameState.dimensions.internalSize.x;
            int fullHeight = m_ActiveFrameState.dimensions.internalSize.y;
            int halfWidth = Mathf.Max(1, fullWidth >> 1);
            int halfHeight = Mathf.Max(1, fullHeight >> 1);

            TextureDescriptor occlusionTextureDsc = new TextureDescriptor(fullWidth, fullHeight, ETextureSizingDomain.Internal);
            {
                occlusionTextureDsc.name = GTAOPassUtilityData.OcclusionTextureName;
                occlusionTextureDsc.dimension = TextureDimension.Tex2D;
                occlusionTextureDsc.colorFormat = GraphicsFormat.R8_UNorm;
                occlusionTextureDsc.depthBufferBits = EDepthBits.None;
                occlusionTextureDsc.enableRandomWrite = true;
                occlusionTextureDsc.filterMode = FilterMode.Bilinear;
                occlusionTextureDsc.wrapMode = TextureWrapMode.Clamp;
            }
            RGTextureRef occlusionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionBuffer, occlusionTextureDsc);

            TextureDescriptor occlusionHalfDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.OcclusionHalfTextureName, GraphicsFormat.R16_SFloat);
            RGTextureRef occlusionHalfTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionHalfBuffer, occlusionHalfDsc);

            TextureDescriptor spatialTempDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.SpatialTempTextureName, GraphicsFormat.R16_SFloat);
            RGTextureRef spatialTempTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SpatialTempBuffer, spatialTempDsc);

            TextureDescriptor historyAODsc = CreateGTAOHistoryAODescriptor(halfWidth, halfHeight);
            TextureDescriptor historyDepthDsc = CreateGTAOHistoryDepthDescriptor(halfWidth, halfHeight);
            RGTextureRef historyOcclusionTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc, out bool historyAOCreated));
            RGTextureRef historyOcclusionDepthTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyDepthDsc, out bool historyDepthCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyOcclusionTexture);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyOcclusionDepthTexture);
            var pendingOcclusion = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc));
            var pendingDepth = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyDepthDsc));
            historyCache.MarkProduced(InfinityShaderIDs.HistoryOcclusionBuffer);
            historyCache.MarkProduced(InfinityShaderIDs.HistoryOcclusionDepthBuffer);
            GTAOPassUtilityData.ResolveKernels(shaders.ssaoShader);

            RGTextureRef halfResDepthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HalfResDepthBuffer);
            RGTextureRef halfResNormalTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HalfResNormalBuffer);
            RGTextureRef fullResDepthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef fullResNormalTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<GTAOPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeGroundTruthOcclusion)))
            {
                ref GTAOPassData passData = ref passRef.GetPassData<GTAOPassData>(); passData = default;
                passData.traceKernel = GTAOPassUtilityData.OcclusionTraceKernel;
                passData.spatialXKernel = GTAOPassUtilityData.OcclusionSpatialXKernel;
                passData.spatialYKernel = GTAOPassUtilityData.OcclusionSpatialYKernel;
                passData.temporalKernel = GTAOPassUtilityData.OcclusionTemporalKernel;
                passData.upsampleKernel = GTAOPassUtilityData.OcclusionUpsampleKernel;
                passData.numRays = ssao.numRays;
                passData.numSteps = ssao.numSteps;
                passData.power = ssao.power;
                passData.radius = ssao.radius;
                passData.intensity = ssao.intensity;
                passData.sharpeness = ssao.sharpness;
                passData.temporalScale = ssao.temporalScale;
                bool resetHistory = m_CameraUniform.historyReset || historyAOCreated || historyDepthCreated;
                passData.temporalWeight = ssao.temporalWeight;
                passData.historyValid = m_ActiveFrameState.acceptedGTAO && !resetHistory && historyCache.TextureGeneration(InfinityShaderIDs.HistoryOcclusionBuffer) > 0
                    && historyCache.TextureGeneration(InfinityShaderIDs.HistoryOcclusionDepthBuffer) > 0;
                passData.halfResolution = new int2(halfWidth, halfHeight);
                passData.fullResolution = new int2(fullWidth, fullHeight);

                passData.halfProjScale = m_CameraUniform.matrix_FlipYJitterProj.m11 * halfHeight * 0.5f;

                int temporalIndex = Time.frameCount & 3;
                passData.temporalOffset = GTAOPassUtilityData.TemporalOffsets[temporalIndex];
                passData.temporalDirection = GTAOPassUtilityData.TemporalDirections[temporalIndex];

                passData.historyViewProj = m_CameraUniform.matrix_LastViewFlipYJitterProj;
                passData.motionViewProj = m_CameraUniform.matrix_ViewFlipYProj;
                passData.lastMotionViewProj = m_CameraUniform.matrix_LastViewFlipYProj;
                passData.depthParameters = new Vector4(camera.nearClipPlane, camera.farClipPlane, camera.orthographic ? 1 : 0, SystemInfo.usesReversedZBuffer ? 1 : 0);
                passData.matrix_Proj = m_CameraUniform.matrix_FlipYJitterProj;
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_ViewProj = m_CameraUniform.matrix_ViewFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_ViewToWorld = m_CameraUniform.matrix_ViewToWorld;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.ssaoShader = shaders.ssaoShader;
                passData.halfResDepthTexture = passRef.ReadTexture(halfResDepthTexture);
                passData.halfResNormalTexture = passRef.ReadTexture(halfResNormalTexture);
                passData.fullResDepthTexture = passRef.ReadTexture(fullResDepthTexture);
                passData.fullResNormalTexture = passRef.ReadTexture(fullResNormalTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.motionMetadata = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                passData.pendingOcclusionTexture = passRef.WriteTexture(pendingOcclusion);
                passData.pendingDepthTexture = passRef.WriteTexture(pendingDepth);

                passData.historyOcclusionTexture = passRef.ReadTexture(historyOcclusionTexture);
                passData.historyOcclusionDepthTexture = passRef.ReadTexture(historyOcclusionDepthTexture);
                passData.occlusionHalfTexture = passRef.WriteTexture(occlusionHalfTexture);
                passData.spatialTempTexture = passRef.WriteTexture(spatialTempTexture);
                passData.occlusionTexture = passRef.WriteTexture(occlusionTexture);

                passData.traceSnapshot = PrepareStageSnapshot(passRef, "AOTrace", occlusionHalfTexture);
                passData.spatialXSnapshot = PrepareStageSnapshot(passRef, "AOSpatialX", spatialTempTexture);
                passData.spatialYSnapshot = PrepareStageSnapshot(passRef, "AOSpatialY", occlusionHalfTexture);
                passData.temporalSnapshot = PrepareStageSnapshot(passRef, "AOTemporal", spatialTempTexture);
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in GTAOPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.ssaoShader;
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("ScreenSpaceDepthParams"), passData.depthParameters);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_HistoryViewProj"), passData.historyViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_MotionViewProj"), passData.motionViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_LastMotionViewProj"), passData.lastMotionViewProj);
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
                    cmdEncoder.SetComputeMatrixParam(shader, GTAOPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(shader, GTAOPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, GTAOPassUtilityData.Matrix_ViewProjID, passData.matrix_ViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, GTAOPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, GTAOPassUtilityData.Matrix_ViewToWorldID, passData.matrix_ViewToWorld);
                    cmdEncoder.SetComputeMatrixParam(shader, GTAOPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);

                    int halfGroupsX = math.max(1, Mathf.CeilToInt(halfWidth / 16.0f));
                    int halfGroupsY = math.max(1, Mathf.CeilToInt(halfHeight / 16.0f));
                    int fullGroupsX = math.max(1, Mathf.CeilToInt(fullWidth / 16.0f));
                    int fullGroupsY = math.max(1, Mathf.CeilToInt(fullHeight / 16.0f));

                    int trace = passData.traceKernel;
                    cmdEncoder.BeginSample("GTAO_Trace");
                    cmdEncoder.SetComputeTextureParam(shader, trace, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, GTAOPassUtilityData.SRV_NormalTextureID, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, GTAOPassUtilityData.UAV_OcclusionTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.DispatchCompute(shader, trace, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_Trace");
                    passData.traceSnapshot.Record(cmdEncoder, passData.occlusionHalfTexture);

                    int spatialX = passData.spatialXKernel;
                    cmdEncoder.BeginSample("GTAO_SpatialX");
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.UAV_SpatialTextureID, passData.spatialTempTexture);
                    cmdEncoder.DispatchCompute(shader, spatialX, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_SpatialX");
                    passData.spatialXSnapshot.Record(cmdEncoder, passData.spatialTempTexture);

                    int spatialY = passData.spatialYKernel;
                    cmdEncoder.BeginSample("GTAO_SpatialY");
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.spatialTempTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.UAV_SpatialTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.DispatchCompute(shader, spatialY, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_SpatialY");
                    passData.spatialYSnapshot.Record(cmdEncoder, passData.occlusionHalfTexture);

                    int temporal = passData.temporalKernel;
                    cmdEncoder.BeginSample("GTAO_Temporal");
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_NormalTextureID, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_HistoryTextureID, passData.historyOcclusionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_HistoryDepthTextureID, passData.historyOcclusionDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.UAV_AccumulationTextureID, passData.spatialTempTexture);
                    cmdEncoder.SetComputeIntParam(shader, GTAOPassUtilityData.HistoryValidID, passData.historyValid ? 1 : 0);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.MotionMetadataID, passData.motionMetadata);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.PendingHistoryID, passData.pendingOcclusionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.PendingDepthID, passData.pendingDepthTexture);
                    cmdEncoder.DispatchCompute(shader, temporal, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_Temporal");
                    passData.temporalSnapshot.Record(cmdEncoder, passData.spatialTempTexture);

                    int upsample = passData.upsampleKernel;
                    cmdEncoder.BeginSample("GTAO_Upsample");
                    cmdEncoder.SetComputeTextureParam(shader, upsample, Shader.PropertyToID("SRV_HalfResDepthTexture"), passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, Shader.PropertyToID("SRV_HalfResNormalTexture"), passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.SRV_DepthTextureID, passData.fullResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.SRV_NormalTextureID, passData.fullResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.spatialTempTexture);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, GTAOPassUtilityData.UAV_UpsampleTextureID, passData.occlusionTexture);
                    cmdEncoder.DispatchCompute(shader, upsample, fullGroupsX, fullGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_Upsample");
                });
            }

            MarkFeatureProduced(EFrameFeature.GTAO);
        }

    }
}
