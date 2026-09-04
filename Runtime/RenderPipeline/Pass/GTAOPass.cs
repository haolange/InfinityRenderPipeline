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
        internal static int OcclusionTraceKernel = 0;
        internal static int OcclusionSpatialXKernel = 1;
        internal static int OcclusionSpatialYKernel = 2;
        internal static int OcclusionTemporalKernel = 3;
        internal static int OcclusionUpsampleKernel = 4;

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
        internal static int UAV_AccmulateTextureID = Shader.PropertyToID("UAV_AccmulateTexture");
        internal static int UAV_UpsampleTextureID = Shader.PropertyToID("UAV_UpsampleTexture");

        internal static readonly float[] TemporalOffsets = { 0.0f, 0.5f, 0.25f, 0.75f };
        internal static readonly float[] TemporalDirections = { 0.0f, 0.5f, 0.25f, 0.75f };
    }

    public partial class InfinityRenderPipeline
    {
        struct GTAOPassData
        {
            public int numRays;
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
            public RGTextureRef motionTexture;
            public RGTextureRef historyOcclusionTexture;
            public RGTextureRef historyOcclusionDepthTexture;
            public RGTextureRef occlusionHalfTexture;
            public RGTextureRef spatialTempTexture;
            public RGTextureRef occlusionTexture;
        }

        static TextureDescriptor CreateGTAOHalfDescriptor(int width, int height, string name, GraphicsFormat format)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height);
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
            TextureDescriptor descriptor = CreateGTAOHalfDescriptor(width, height, GTAOPassUtilityData.HistoryOcclusionTextureName, GraphicsFormat.R8_UNorm);
            descriptor.enableRandomWrite = false;
            return descriptor;
        }

        static TextureDescriptor CreateGTAOHistoryDepthDescriptor(int width, int height)
        {
            TextureDescriptor descriptor = CreateGTAOHalfDescriptor(width, height, GTAOPassUtilityData.HistoryOcclusionDepthTextureName, GraphicsFormat.R32_SFloat);
            descriptor.enableRandomWrite = false;
            return descriptor;
        }

        void ComputeGroundTruthOcclusion(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.GTAO))
            {
                return;
            }

            var ssao = ActiveVolumeStack.GetComponent<ScreenSpaceAmbientOcclusion>();
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.ssaoShader, "OcclusionTrace", "OcclusionSpatialX", "OcclusionSpatialY", "OcclusionTemporal", "OcclusionUpsample"))
            {
                return;
            }

            int fullWidth = camera.pixelWidth;
            int fullHeight = camera.pixelHeight;
            int halfWidth = Mathf.Max(1, fullWidth >> 1);
            int halfHeight = Mathf.Max(1, fullHeight >> 1);

            TextureDescriptor occlusionTextureDsc = new TextureDescriptor(fullWidth, fullHeight);
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

            TextureDescriptor occlusionHalfDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.OcclusionHalfTextureName, GraphicsFormat.R8_UNorm);
            RGTextureRef occlusionHalfTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.OcclusionHalfBuffer, occlusionHalfDsc);

            TextureDescriptor spatialTempDsc = CreateGTAOHalfDescriptor(halfWidth, halfHeight, GTAOPassUtilityData.SpatialTempTextureName, GraphicsFormat.R8_UNorm);
            RGTextureRef spatialTempTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SpatialTempBuffer, spatialTempDsc);

            TextureDescriptor historyAODsc = CreateGTAOHistoryAODescriptor(halfWidth, halfHeight);
            TextureDescriptor historyDepthDsc = CreateGTAOHistoryDepthDescriptor(halfWidth, halfHeight);
            RGTextureRef historyOcclusionTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc, out bool historyAOCreated));
            RGTextureRef historyOcclusionDepthTexture = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyDepthDsc, out bool historyDepthCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyOcclusionTexture);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyOcclusionDepthTexture);

            RGTextureRef halfResDepthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HalfResDepthBuffer);
            RGTextureRef halfResNormalTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.HalfResNormalBuffer);
            RGTextureRef fullResDepthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef fullResNormalTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<GTAOPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeGroundTruthOcclusion)))
            {
                ref GTAOPassData passData = ref passRef.GetPassData<GTAOPassData>();
                passData.numRays = ssao.NumRays.value;
                passData.numSteps = ssao.NumSteps.value;
                passData.power = ssao.Power.value;
                passData.radius = ssao.Radius.value;
                passData.intensity = ssao.Intensity.value;
                passData.sharpeness = ssao.Sharpeness.value;
                passData.temporalScale = ssao.TemporalScale.value;
                bool resetHistory = m_CameraUniform.historyReset || historyAOCreated || historyDepthCreated;
                passData.temporalWeight = ScreenSpaceHistoryUtility.RampTemporalWeight(ssao.TemporalWeight.value, ref m_ActiveFrameState.gtaoValidFrames, resetHistory);
                passData.halfResolution = new int2(halfWidth, halfHeight);
                passData.fullResolution = new int2(fullWidth, fullHeight);

                passData.halfProjScale = m_CameraUniform.matrix_FlipYJitterProj.m11 * halfHeight * 0.5f;

                int temporalIndex = Time.frameCount & 3;
                passData.temporalOffset = GTAOPassUtilityData.TemporalOffsets[temporalIndex];
                passData.temporalDirection = GTAOPassUtilityData.TemporalDirections[temporalIndex];

                passData.matrix_Proj = m_CameraUniform.matrix_FlipYJitterProj;
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_ViewProj = m_CameraUniform.matrix_ViewFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_ViewToWorld = m_CameraUniform.matrix_ViewToWorld;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.ssaoShader = pipelineAsset.ssaoShader;
                passData.halfResDepthTexture = passRef.ReadTexture(halfResDepthTexture);
                passData.halfResNormalTexture = passRef.ReadTexture(halfResNormalTexture);
                passData.fullResDepthTexture = passRef.ReadTexture(fullResDepthTexture);
                passData.fullResNormalTexture = passRef.ReadTexture(fullResNormalTexture);
                passData.motionTexture = passRef.ReadTexture(motionTexture);
                passData.historyOcclusionTexture = passRef.ReadTexture(historyOcclusionTexture);
                passData.historyOcclusionDepthTexture = passRef.ReadTexture(historyOcclusionDepthTexture);
                passData.occlusionHalfTexture = passRef.WriteTexture(occlusionHalfTexture);
                passData.spatialTempTexture = passRef.WriteTexture(spatialTempTexture);
                passData.occlusionTexture = passRef.WriteTexture(occlusionTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in GTAOPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.ssaoShader;
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

                    int trace = GTAOPassUtilityData.OcclusionTraceKernel;
                    cmdEncoder.BeginSample("GTAO_Trace");
                    cmdEncoder.SetComputeTextureParam(shader, trace, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, GTAOPassUtilityData.SRV_NormalTextureID, passData.halfResNormalTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, GTAOPassUtilityData.UAV_OcclusionTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.DispatchCompute(shader, trace, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_Trace");

                    int spatialX = GTAOPassUtilityData.OcclusionSpatialXKernel;
                    cmdEncoder.BeginSample("GTAO_SpatialX");
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialX, GTAOPassUtilityData.UAV_SpatialTextureID, passData.spatialTempTexture);
                    cmdEncoder.DispatchCompute(shader, spatialX, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_SpatialX");

                    int spatialY = GTAOPassUtilityData.OcclusionSpatialYKernel;
                    cmdEncoder.BeginSample("GTAO_SpatialY");
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.spatialTempTexture);
                    cmdEncoder.SetComputeTextureParam(shader, spatialY, GTAOPassUtilityData.UAV_SpatialTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.DispatchCompute(shader, spatialY, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_SpatialY");

                    int temporal = GTAOPassUtilityData.OcclusionTemporalKernel;
                    cmdEncoder.BeginSample("GTAO_Temporal");
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_DepthTextureID, passData.halfResDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_OcclusionTextureID, passData.occlusionHalfTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_HistoryTextureID, passData.historyOcclusionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_HistoryDepthTextureID, passData.historyOcclusionDepthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporal, GTAOPassUtilityData.UAV_AccmulateTextureID, passData.spatialTempTexture);
                    cmdEncoder.DispatchCompute(shader, temporal, halfGroupsX, halfGroupsY, 1);
                    cmdEncoder.EndSample("GTAO_Temporal");

                    int upsample = GTAOPassUtilityData.OcclusionUpsampleKernel;
                    cmdEncoder.BeginSample("GTAO_Upsample");
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

        struct CopyHistoryOcclusionPassData
        {
            public RGTextureRef sourceTexture;
            public RGTextureRef historyTexture;
        }

        void CopyHistoryOcclusion(RenderContext renderContext, HistoryCache historyCache, Camera camera)
        {
            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.SpatialTempBuffer, out RGTextureRef temporalAOTexture))
            {
                return;
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.HalfResDepthBuffer, out RGTextureRef halfResDepthTexture))
            {
                return;
            }

            int halfWidth = Mathf.Max(1, camera.pixelWidth >> 1);
            int halfHeight = Mathf.Max(1, camera.pixelHeight >> 1);

            TextureDescriptor historyAODsc = CreateGTAOHistoryAODescriptor(halfWidth, halfHeight);
            RGTextureRef historyAOTexture = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryOcclusionBuffer, historyAODsc));
            historyCache.MarkProduced(InfinityShaderIDs.HistoryOcclusionBuffer);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryOcclusionPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryOcclusion)))
            {
                passRef.ReadTexture(temporalAOTexture);
                passRef.WriteTexture(historyAOTexture);

                ref CopyHistoryOcclusionPassData passData = ref passRef.GetPassData<CopyHistoryOcclusionPassData>();
                passData.sourceTexture = temporalAOTexture;
                passData.historyTexture = historyAOTexture;

                passRef.SetExecuteFunc((in CopyHistoryOcclusionPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.sourceTexture, passData.historyTexture);
                });
            }

            TextureDescriptor historyDepthDsc = CreateGTAOHistoryDepthDescriptor(halfWidth, halfHeight);
            RGTextureRef historyDepthTexture = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(InfinityShaderIDs.HistoryOcclusionDepthBuffer, historyDepthDsc));
            historyCache.MarkProduced(InfinityShaderIDs.HistoryOcclusionDepthBuffer);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryOcclusionPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistoryOcclusionDepth)))
            {
                passRef.ReadTexture(halfResDepthTexture);
                passRef.WriteTexture(historyDepthTexture);

                ref CopyHistoryOcclusionPassData passData = ref passRef.GetPassData<CopyHistoryOcclusionPassData>();
                passData.sourceTexture = halfResDepthTexture;
                passData.historyTexture = historyDepthTexture;

                passRef.SetExecuteFunc((in CopyHistoryOcclusionPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.sourceTexture, passData.historyTexture);
                });
            }
        }
    }
}
