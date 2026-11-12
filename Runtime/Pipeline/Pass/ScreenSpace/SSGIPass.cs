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
    // One 8x8 group traces its own pixels, then two temporal-then-world-space denoise stages, then a full-resolution upsample.
    internal static class SSGIPassUtilityData
    {
        internal static readonly int SRV_CurrentID = Shader.PropertyToID("SRV_Current");
        internal static readonly int SRV_FilteredID = Shader.PropertyToID("SRV_Filtered");
        internal static readonly int SRV_OcclusionID = Shader.PropertyToID("SRV_Occlusion");
        internal static readonly int SSGI_FrameAngleID = Shader.PropertyToID("SSGI_FrameAngle");
        internal static readonly int SSGI_HasAOID = Shader.PropertyToID("SSGI_HasAO");
        internal static readonly int SSGI_PreviousFrameIndexID = Shader.PropertyToID("SSGI_PreviousFrameIndex");
        internal static readonly int SSGI_SpatialPassID = Shader.PropertyToID("SSGI_SpatialPass");
        internal static readonly ProfilingSampler SSGITraceSampler = new ProfilingSampler("SSGI_Trace");
        internal static readonly ProfilingSampler SSGITemporal1Sampler = new ProfilingSampler("SSGI_Temporal1");
        internal static readonly ProfilingSampler SSGISpatialWideSampler = new ProfilingSampler("SSGI_SpatialWide");
        internal static readonly ProfilingSampler SSGITemporal2Sampler = new ProfilingSampler("SSGI_Temporal2");
        internal static readonly ProfilingSampler SSGISpatialNarrowSampler = new ProfilingSampler("SSGI_SpatialNarrow");
        internal static readonly ProfilingSampler SSGIUpsampleSampler = new ProfilingSampler("SSGI_Upsample");
        internal static readonly int SSGI_ResolutionID = Shader.PropertyToID("SSGI_Resolution");
        internal static readonly int SSGI_TraceResolutionID = Shader.PropertyToID("SSGI_TraceResolution");
        internal static readonly int SSGI_TraceScaleID = Shader.PropertyToID("SSGI_TraceScale");
        internal static readonly int SSGI_FrameIndexID = Shader.PropertyToID("SSGI_FrameIndex");
        internal static readonly int SSGI_IntensityID = Shader.PropertyToID("SSGI_Intensity");
        internal static readonly int SSGI_HiZStepsID = Shader.PropertyToID("SSGI_HiZSteps");
        internal static readonly int SSGI_SamplesID = Shader.PropertyToID("SSGI_Samples");
        internal static readonly int SSGI_TraceFlagsID = Shader.PropertyToID("SSGI_TraceFlags");
        internal static readonly int SSGI_TemporalWeightID = Shader.PropertyToID("SSGI_TemporalWeight");
        internal static readonly int SSGI_TemporalClampScaleID = Shader.PropertyToID("SSGI_TemporalClampScale");
        internal static readonly int SSGI_HistoryValidID = Shader.PropertyToID("SSGI_HistoryValid");
        internal static readonly int SSGI_CameraPositionID = Shader.PropertyToID("SSGI_CameraPosition");
        internal static readonly int SSGI_CameraForwardID = Shader.PropertyToID("SSGI_CameraForward");
        internal static readonly int SRV_TraceRadianceID = Shader.PropertyToID("SRV_TraceRadiance");
        internal static readonly int UAV_TraceRadianceID = Shader.PropertyToID("UAV_TraceRadiance");
        internal const string RayHitName = "SSGIRayHit";
        internal const string TraceRadianceName = "SSGITraceRadiance";
        internal const string TemporalName = "SSGITemporal";
        internal const string MetaName = "SSGIMeta";
        internal const string OutputName = "SSGITexture";
        internal const string HistoryRadianceName = "HistorySSGIRadiance";
        internal const string HistoryMetaName = "HistorySSGIMeta";
    }

    public partial class InfinityRenderPipeline
    {
        struct SSGIPassData
        {
            public SSGISettings settings;
            public int2 resolution;
            public int2 traceResolution;
            public int2 tileCount;
            public int traceScale;
            public int traceKernel;
            public int traceDebugKernel;
            public int temporalKernel;
            public int spatialKernel;
            public int upsampleKernel;
            public int2 denoiseGroups;
            public int2 upsampleGroups;
            public int frameIndex;
            public int previousFrameIndex;
            public float historyValid;
            public Vector4 depthParameters;
            public Vector4 cameraPosition;
            public Vector4 cameraForward;
            public Matrix4x4 matrix_Proj;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_InvViewProj;
            public Matrix4x4 matrix_WorldToView;
            public Matrix4x4 matrix_HistoryViewProj;
            public Matrix4x4 matrix_MotionViewProj;
            public Matrix4x4 matrix_LastMotionViewProj;
            public ComputeShader ssgiShader;
            public Texture2D rankingTile;
            public Texture2D scramblingTile;
            public Texture2D owenScrambledSobol;
            public RGTextureHandle depthTexture;
            public RGTextureHandle hiZTexture;
            public RGTextureHandle hiZFurthestTexture;
            public RGTextureHandle gBufferB;
            public RGTextureHandle gBufferC;
            public TemporalColorView temporalColor;
            public RGTextureHandle motionMetadata;
            public RGTextureHandle motionTexture;
            public RGTextureHandle historyRadiance;
            public RGTextureHandle historyNarrow;
            public RGTextureHandle historyMeta;
            public RGTextureHandle occlusion;
            public int hasAO;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public RGTextureHandle rayHit;
            public bool captureRayHit;
            public CaptureStageSnapshot traceSnapshot;
#endif
            public RGTextureHandle traceRadiance;
            public RGTextureHandle hitDistance;
            public RGTextureHandle temporal;
            public RGTextureHandle temporalNarrow;
            public RGTextureHandle combinerWide;
            public RGTextureHandle combinerNarrow;
            public RGTextureHandle meta;
            public RGTextureHandle output;
        }

        void ComputeScreenSpaceIndirect(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SSGI))
            {
                return;
            }

            SSGISettings settings = context.volumeStack.GetComponent<ScreenSpaceIndirectDiffuse>().Resolve();
            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            int2 resolution = new int2(width, height);
            int2 traceResolution = ScreenSpaceTraceUtility.TraceSize(resolution, true);
            int workWidth = traceResolution.x, workHeight = traceResolution.y;
            const int divisor = 2;
            int2 tileCount = ScreenSpaceTraceUtility.TileCount(traceResolution);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var capture = RenderCaptureService.current;
            bool captureRayHit = capture != null && capture.CaptureThisFrame && System.Array.IndexOf(capture.request.buffers, "SSGIRayHit") >= 0;
            RGTextureHandle rayHit = captureRayHit ? m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIRayHitBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.RayHitName, GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point, divisor)) : default;
#endif
            RGTextureHandle traceRadiance = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGITraceRadianceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.TraceRadianceName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle hitDistance = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIHitDistanceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGIHitDistance", GraphicsFormat.R32_SFloat, FilterMode.Point, divisor));
            RGTextureHandle temporal = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGITemporalBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.TemporalName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle combinerWide = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIFilteredBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGICombinerWide", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle meta = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIMetaBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.MetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor));
            RGTextureHandle output = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIBuffer,
                SSRPassUtilityData.CreateDescriptor(width, height, SSGIPassUtilityData.OutputName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SSGIBuffer, output);
            TextureDescriptor historyRadianceDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.HistoryRadianceName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor);
            TextureDescriptor historyMetaDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.HistoryMetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor);
            historyMetaDsc.filterMode = FilterMode.Point;
            RGTextureHandle historyRadiance = m_RGBuilder.ImportTexture(context.historyCache.GetTexture(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadianceDsc, out bool historyRadianceCreated));
            TextureDescriptor historyNarrowDsc = historyRadianceDsc;
            historyNarrowDsc.name = "HistorySSGINarrow";
            RGTextureHandle historyNarrow = m_RGBuilder.ImportTexture(context.historyCache.GetTexture(InfinityShaderIDs.HistorySSGINarrowBuffer, historyNarrowDsc, out bool historyNarrowCreated));
            RGTextureHandle historyMeta = m_RGBuilder.ImportTexture(context.historyCache.GetTexture(InfinityShaderIDs.HistorySSGIMetaBuffer, historyMetaDsc, out bool historyMetaCreated));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadiance);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSGINarrowBuffer, historyNarrow);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSGIMetaBuffer, historyMeta);
            bool historyValid = context.state.acceptedSSGI && !(context.resetsHistory || historyRadianceCreated || historyNarrowCreated || historyMetaCreated)
                && context.historyCache.TextureGeneration(InfinityShaderIDs.HistorySSGIRadianceBuffer) > 0
                && context.historyCache.TextureGeneration(InfinityShaderIDs.HistorySSGINarrowBuffer) > 0
                && context.historyCache.TextureGeneration(InfinityShaderIDs.HistorySSGIMetaBuffer) > 0;
            bool hasAO = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureHandle occlusion);

            InfinityRenderPipelineRuntimeTextures textures = resources.textures;

            using (var passRef = m_RGBuilder.AddComputePass<SSGIPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceIndirect)))
            {
                ref SSGIPassData passData = ref passRef.GetPassData();
                passData.settings = settings;
                passData.resolution = resolution;
                passData.traceResolution = traceResolution;
                passData.tileCount = tileCount;
                passData.traceScale = 2;
                passData.frameIndex = Time.frameCount;
                passData.previousFrameIndex = context.state.acceptedSSGIFrameIndex;
                passData.historyValid = historyValid ? 1 : 0;
                passData.depthParameters = new Vector4(context.camera.nearClipPlane, context.camera.farClipPlane, context.camera.orthographic ? 1 : 0, SystemInfo.usesReversedZBuffer ? 1 : 0);
                Vector3 cameraPosition = context.camera.transform.position;
                passData.cameraPosition = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, context.camera.orthographic ? 1 : 0);
                passData.cameraForward = context.camera.transform.forward;
                passData.matrix_Proj = context.cameraUniform.matrix_FlipYJitterProj;
                passData.matrix_InvProj = context.cameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_InvViewProj = context.cameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_WorldToView = context.cameraUniform.matrix_WorldToView;
                passData.matrix_HistoryViewProj = context.cameraUniform.matrix_LastViewFlipYJitterProj;
                passData.matrix_MotionViewProj = context.cameraUniform.matrix_ViewFlipYProj;
                passData.matrix_LastMotionViewProj = context.cameraUniform.matrix_LastViewFlipYProj;
                passData.ssgiShader = shaders.ssgiShader;
                passData.traceKernel = kernels.ssgiTrace.index;
                passData.traceDebugKernel = kernels.ssgiTraceDebug.index;
                passData.temporalKernel = kernels.ssgiTemporal.index;
                passData.spatialKernel = kernels.ssgiSpatial.index;
                passData.upsampleKernel = kernels.ssgiUpsample.index;
                passData.denoiseGroups = new int2(kernels.ssgiTemporal.GroupsX(traceResolution.x), kernels.ssgiTemporal.GroupsY(traceResolution.y));
                passData.upsampleGroups = new int2(kernels.ssgiUpsample.GroupsX(resolution.x), kernels.ssgiUpsample.GroupsY(resolution.y));
                passData.rankingTile = textures.blueNoiseRankingTile;
                passData.scramblingTile = textures.blueNoiseScramblingTile;
                passData.owenScrambledSobol = textures.blueNoiseOwenScrambledSobol;

                passData.depthTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.hiZTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZClosestBuffer));
                passData.hiZFurthestTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZFurthestBuffer));
                passData.gBufferB = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.gBufferC = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC));
                passData.temporalColor = ReadTemporalColorView(passRef, context.state.temporalColorView);
                passData.motionMetadata = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                passData.motionTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                passData.historyRadiance = passRef.ReadTexture(historyRadiance);
                passData.historyNarrow = passRef.ReadTexture(historyNarrow);
                passData.historyMeta = passRef.ReadTexture(historyMeta);
                passData.hasAO = hasAO ? 1 : 0;
                passData.occlusion = hasAO ? passRef.ReadTexture(occlusion) : default;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.captureRayHit = captureRayHit;
                passData.rayHit = captureRayHit ? passRef.WriteTexture(rayHit) : default;
                passData.traceSnapshot = PrepareStageSnapshot(passRef, "SSGITraceRadiance", traceRadiance);
#endif
                passData.traceRadiance = passRef.WriteTexture(traceRadiance);
                passData.hitDistance = passRef.WriteTexture(hitDistance);
                passData.temporal = passRef.WriteTexture(temporal);
                passData.temporalNarrow = passRef.CreatePassLocalTexture(SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGITemporalNarrow", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
                passData.combinerWide = passRef.WriteTexture(combinerWide);
                passData.combinerNarrow = passRef.WriteTexture(m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGICombinerNarrowBuffer,
                    SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGICombinerNarrow", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor)));
                passData.meta = passRef.WriteTexture(meta);
                passData.output = passRef.WriteTexture(output);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in SSGIPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.ssgiShader;
                    SSGISettings settings = passData.settings;
                    int2 resolution = passData.resolution;
                    int2 traceResolution = passData.traceResolution;
                    int2 tileCount = passData.tileCount;

                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_ResolutionID, new Vector4(resolution.x, resolution.y, 1.0f / resolution.x, 1.0f / resolution.y));
                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_TraceResolutionID, new Vector4(traceResolution.x, traceResolution.y, 1.0f / traceResolution.x, 1.0f / traceResolution.y));
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_TraceScaleID, passData.traceScale);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_PreviousFrameIndexID, passData.previousFrameIndex);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_IntensityID, settings.intensity);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_HiZStepsID, math.max(1, settings.hiZSteps));
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_SamplesID, math.clamp(settings.samples, 1, 4));
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_TraceFlagsID, (settings.traceBehind ? 1 : 0) | (settings.traceToward ? 2 : 0));
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_TemporalWeightID, settings.temporalWeight);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_TemporalClampScaleID, settings.temporalClampScale);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_HistoryValidID, passData.historyValid);
                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_CameraPositionID, passData.cameraPosition);
                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_CameraForwardID, passData.cameraForward);
                    cmdEncoder.SetComputeVectorParam(shader, PassShaderIDs.ScreenSpaceDepthParams, passData.depthParameters);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_Proj, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvProj, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_InvViewProj, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, CameraUniform.ID_Matrix_WorldToView, passData.matrix_WorldToView);
                    cmdEncoder.SetComputeMatrixParam(shader, PassShaderIDs.Matrix_HistoryViewProj, passData.matrix_HistoryViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, PassShaderIDs.Matrix_MotionViewProj, passData.matrix_MotionViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, PassShaderIDs.Matrix_LastMotionViewProj, passData.matrix_LastMotionViewProj);

                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_FrameAngleID, (passData.frameIndex & 63) * 2.39996323f);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_HasAOID, passData.hasAO);
                    cmdEncoder.BeginSample(SSGIPassUtilityData.SSGITraceSampler);
                    int trace = passData.traceKernel;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (passData.captureRayHit)
                    {
                        trace = passData.traceDebugKernel;
                    }
#endif
                    BindSSGISurface(cmdEncoder, shader, trace, passData);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_HiZClosestTexture, passData.hiZTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.SRV_HiZFurthestTextureID, passData.hiZFurthestTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.RankingTileID, passData.rankingTile, TextureView.Whole(passData.rankingTile));
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.ScramblingTileID, passData.scramblingTile, TextureView.Whole(passData.scramblingTile));
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.OwenScrambledSobolID, passData.owenScrambledSobol, TextureView.Whole(passData.owenScrambledSobol));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (passData.captureRayHit)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.UAV_RayHit, passData.rayHit);
                    }
#endif
                    BindTemporalColorView(cmdEncoder, shader, trace, passData.temporalColor);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_HitMotionMetadata, passData.motionMetadata);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSGIPassUtilityData.UAV_TraceRadianceID, passData.traceRadiance);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.UAV_HitDistance, passData.hitDistance);
                    cmdEncoder.DispatchCompute(shader, trace, tileCount.x, tileCount.y, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGITraceSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.traceSnapshot.Record(cmdEncoder, passData.traceRadiance);
#endif

                    int groupsX = passData.denoiseGroups.x;
                    int groupsY = passData.denoiseGroups.y;
                    int temporalKernel = passData.temporalKernel;
                    int spatialKernel = passData.spatialKernel;
                    RecordSSGITemporal(cmdEncoder, shader, temporalKernel, SSGIPassUtilityData.SSGITemporal1Sampler, passData.traceRadiance, passData.historyRadiance, passData.temporal, passData);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGITemporal1Sampler);
                    RecordSSGISpatial(cmdEncoder, shader, spatialKernel, SSGIPassUtilityData.SSGISpatialWideSampler, 0, passData.temporal, passData.combinerWide, passData);
                    cmdEncoder.DispatchCompute(shader, spatialKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGISpatialWideSampler);
                    RecordSSGITemporal(cmdEncoder, shader, temporalKernel, SSGIPassUtilityData.SSGITemporal2Sampler, passData.combinerWide, passData.historyNarrow, passData.temporalNarrow, passData);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGITemporal2Sampler);
                    RecordSSGISpatial(cmdEncoder, shader, spatialKernel, SSGIPassUtilityData.SSGISpatialNarrowSampler, 1, passData.temporalNarrow, passData.combinerNarrow, passData);
                    cmdEncoder.DispatchCompute(shader, spatialKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGISpatialNarrowSampler);

                    cmdEncoder.BeginSample(SSGIPassUtilityData.SSGIUpsampleSampler);
                    int upsample = passData.upsampleKernel;
                    BindSSGISurface(cmdEncoder, shader, upsample, passData);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, SSGIPassUtilityData.SRV_FilteredID, passData.combinerNarrow);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_Meta, passData.meta);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.UAV_Output, passData.output);
                    cmdEncoder.DispatchCompute(shader, upsample, passData.upsampleGroups.x, passData.upsampleGroups.y, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGIUpsampleSampler);



                });
            }

            context.features.MarkProduced(EFrameFeature.SSGI);
        }

        static void RecordSSGITemporal(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, ProfilingSampler marker,
            RGTextureHandle current, RGTextureHandle history, RGTextureHandle output, in SSGIPassData passData)
        {
            cmdEncoder.BeginSample(marker);
            BindSSGISurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HitMotionMetadata, passData.motionMetadata);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_CurrentID, current);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HistoryRadiance, history);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HistoryMeta, passData.historyMeta);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_Temporal, output);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_Meta, passData.meta);
        }

        static void RecordSSGISpatial(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, ProfilingSampler marker, int pass,
            RGTextureHandle current, RGTextureHandle output, in SSGIPassData passData)
        {
            cmdEncoder.BeginSample(marker);
            BindSSGISurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_SpatialPassID, pass);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_Temporal, current);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_Meta, passData.meta);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_OcclusionID, passData.hasAO != 0 ? passData.occlusion : passData.depthTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_Output, output);
        }

        // Depth and GBuffer inputs shared by every kernel that tests or reconstructs the surface.
        static void BindSSGISurface(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, in SSGIPassData passData)
        {
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferB, passData.gBufferB);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferC, passData.gBufferC);
        }
    }
}
