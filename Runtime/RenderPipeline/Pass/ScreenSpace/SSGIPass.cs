using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    // One 8x8 group traces its own pixels, then two temporal-then-world-space denoise stages, then a full-resolution upsample.
    internal static class SSGIPassUtilityData
    {
        internal static readonly string[] RequiredKernels =
        {
            "SSGITrace", "SSGITemporal", "SSGISpatial", "SSGIUpsample", "SSGITraceDebug"
        };

        static ComputeShader s_KernelShader;
        static int[] s_Kernels;
        internal static int[] ResolveKernels(ComputeShader shader)
        {
            if (s_KernelShader != shader || s_Kernels == null)
            {
                var kernels = new int[RequiredKernels.Length];
                for (int i = 0; i < kernels.Length; ++i) kernels[i] = shader.FindKernel(RequiredKernels[i]);
                s_KernelShader = shader; s_Kernels = kernels;
            }
            return s_Kernels;
        }

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

        internal static readonly int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static readonly int SRV_HiZClosestTextureID = Shader.PropertyToID("SRV_HiZClosestTexture");
        internal static readonly int SRV_GBufferBID = Shader.PropertyToID("SRV_GBufferB");
        internal static readonly int SRV_GBufferCID = Shader.PropertyToID("SRV_GBufferC");
        internal static readonly int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static readonly int SRV_HistoryRadianceID = Shader.PropertyToID("SRV_HistoryRadiance");
        internal static readonly int SRV_HistoryMetaID = Shader.PropertyToID("SRV_HistoryMeta");
        internal static readonly int SRV_TraceRadianceID = Shader.PropertyToID("SRV_TraceRadiance");
        internal static readonly int SRV_TemporalID = Shader.PropertyToID("SRV_Temporal");
        internal static readonly int SRV_TileListID = Shader.PropertyToID("SRV_TileList");
        internal static readonly int UAV_TileArgsID = Shader.PropertyToID("UAV_TileArgs");
        internal static readonly int UAV_TileListID = Shader.PropertyToID("UAV_TileList");
        internal static readonly int UAV_RayHitID = Shader.PropertyToID("UAV_RayHit");
        internal static readonly int UAV_TraceRadianceID = Shader.PropertyToID("UAV_TraceRadiance");
        internal static readonly int UAV_TemporalID = Shader.PropertyToID("UAV_Temporal");
        internal static readonly int UAV_MetaID = Shader.PropertyToID("UAV_Meta");
        internal static readonly int UAV_OutputID = Shader.PropertyToID("UAV_Output");

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
            public FSSGISettings settings;
            public int2 resolution;
            public int2 traceResolution;
            public int2 tileCount;
            public int traceScale;
            public int[] kernels;
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
            public FTemporalColorView temporalColor;
            public RGTextureHandle motionMetadata;
            public RGTextureHandle motionTexture;
            public RGTextureHandle historyRadiance;
            public RGTextureHandle historyNarrow;
            public RGTextureHandle historyMeta;
            public RGTextureHandle occlusion;
            public int hasAO;
            public RGTextureHandle rayHit;
            public RGTextureHandle traceRadiance;
            public RGTextureHandle hitDistance;
            public RGTextureHandle temporal;
            public RGTextureHandle temporalNarrow;
            public RGTextureHandle combinerWide;
            public RGTextureHandle combinerNarrow;
            public RGTextureHandle meta;
            public RGTextureHandle output;
            public bool captureRayHit;
            public CaptureStageSnapshot traceSnapshot;
        }

        void ComputeScreenSpaceIndirect(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.SSGI))
            {
                return;
            }

            FSSGISettings settings = ActiveVolumeStack.GetComponent<ScreenSpaceIndirectDiffuse>().Resolve();
            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            int2 resolution = new int2(width, height);
            int2 traceResolution = ScreenSpaceTraceUtility.TraceSize(resolution, true);
            int workWidth = traceResolution.x, workHeight = traceResolution.y;
            const int divisor = 2;
            int2 tileCount = ScreenSpaceTraceUtility.TileCount(traceResolution);

            var capture = RenderCaptureService.current;
            bool captureRayHit = capture != null && capture.CaptureThisFrame && System.Array.IndexOf(capture.request.buffers, "SSGIRayHit") >= 0;
            RGTextureHandle rayHit = captureRayHit ? m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIRayHitBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.RayHitName, GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point, divisor)) : default;
            RGTextureHandle traceRadiance = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGITraceRadianceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.TraceRadianceName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle hitDistance = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIHitDistanceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGIHitDistance", GraphicsFormat.R32_SFloat, FilterMode.Point, divisor));
            RGTextureHandle temporal = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGITemporalBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.TemporalName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle combinerWide = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIFilteredBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGICombinerWide", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle meta = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIMetaBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.MetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor));
            RGTextureHandle output = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIBuffer,
                SSRPassUtilityData.CreateDescriptor(width, height, SSGIPassUtilityData.OutputName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.SSGIBuffer, output);
            TextureDescriptor historyRadianceDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.HistoryRadianceName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor);
            TextureDescriptor historyMetaDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.HistoryMetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor);
            historyMetaDsc.filterMode = FilterMode.Point;
            RGTextureHandle historyRadiance = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadianceDsc, out bool historyRadianceCreated));
            TextureDescriptor historyNarrowDsc = historyRadianceDsc;
            historyNarrowDsc.name = "HistorySSGINarrow";
            RGTextureHandle historyNarrow = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSGINarrowBuffer, historyNarrowDsc, out bool historyNarrowCreated));
            RGTextureHandle historyMeta = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSGIMetaBuffer, historyMetaDsc, out bool historyMetaCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadiance);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSGINarrowBuffer, historyNarrow);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSGIMetaBuffer, historyMeta);
            bool historyValid = m_ActiveFrameState.acceptedSSGI && !(m_CameraUniform.historyReset || historyRadianceCreated || historyNarrowCreated || historyMetaCreated)
                && historyCache.TextureGeneration(InfinityShaderIDs.HistorySSGIRadianceBuffer) > 0
                && historyCache.TextureGeneration(InfinityShaderIDs.HistorySSGINarrowBuffer) > 0
                && historyCache.TextureGeneration(InfinityShaderIDs.HistorySSGIMetaBuffer) > 0;
            bool hasAO = m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureHandle occlusion);

            InfinityRenderPipelineRuntimeTextures textures = resources.textures;

            using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<SSGIPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceIndirect)))
            {
                ref SSGIPassData passData = ref passRef.GetPassData<SSGIPassData>();
                passData.settings = settings;
                passData.resolution = resolution;
                passData.traceResolution = traceResolution;
                passData.tileCount = tileCount;
                passData.traceScale = 2;
                passData.frameIndex = Time.frameCount;
                passData.previousFrameIndex = m_ActiveFrameState.acceptedSSGIFrameIndex;
                passData.historyValid = historyValid ? 1 : 0;
                passData.depthParameters = new Vector4(camera.nearClipPlane, camera.farClipPlane, camera.orthographic ? 1 : 0, SystemInfo.usesReversedZBuffer ? 1 : 0);
                Vector3 cameraPosition = camera.transform.position;
                passData.cameraPosition = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, camera.orthographic ? 1 : 0);
                passData.cameraForward = camera.transform.forward;
                passData.matrix_Proj = m_CameraUniform.matrix_FlipYJitterProj;
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.matrix_WorldToView = m_CameraUniform.matrix_WorldToView;
                passData.matrix_HistoryViewProj = m_CameraUniform.matrix_LastViewFlipYJitterProj;
                passData.matrix_MotionViewProj = m_CameraUniform.matrix_ViewFlipYProj;
                passData.matrix_LastMotionViewProj = m_CameraUniform.matrix_LastViewFlipYProj;
                passData.ssgiShader = shaders.ssgiShader;
                passData.kernels = SSGIPassUtilityData.ResolveKernels(shaders.ssgiShader);
                passData.rankingTile = textures.blueNoiseRankingTile;
                passData.scramblingTile = textures.blueNoiseScramblingTile;
                passData.owenScrambledSobol = textures.blueNoiseOwenScrambledSobol;

                passData.depthTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.hiZTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.HiZClosestBuffer));
                passData.hiZFurthestTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.HiZFurthestBuffer));
                passData.gBufferB = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.gBufferC = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC));
                passData.temporalColor = ReadTemporalColorView(passRef, m_ActiveFrameState.temporalColorView);
                passData.motionMetadata = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                passData.motionTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                passData.historyRadiance = passRef.ReadTexture(historyRadiance);
                passData.historyNarrow = passRef.ReadTexture(historyNarrow);
                passData.historyMeta = passRef.ReadTexture(historyMeta);
                passData.hasAO = hasAO ? 1 : 0;
                passData.occlusion = hasAO ? passRef.ReadTexture(occlusion) : default;
                passData.captureRayHit = captureRayHit;
                passData.rayHit = captureRayHit ? passRef.WriteTexture(rayHit) : default;
                passData.traceRadiance = passRef.WriteTexture(traceRadiance);
                passData.hitDistance = passRef.WriteTexture(hitDistance);
                passData.temporal = passRef.WriteTexture(temporal);
                passData.temporalNarrow = passRef.CreatePassLocalTexture(SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGITemporalNarrow", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
                passData.combinerWide = passRef.WriteTexture(combinerWide);
                passData.combinerNarrow = passRef.WriteTexture(m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGICombinerNarrowBuffer,
                    SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGICombinerNarrow", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor)));
                passData.meta = passRef.WriteTexture(meta);
                passData.traceSnapshot = PrepareStageSnapshot(passRef, "SSGITraceRadiance", traceRadiance);
                passData.output = passRef.WriteTexture(output);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in SSGIPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.ssgiShader;
                    FSSGISettings settings = passData.settings;
                    int2 resolution = passData.resolution;
                    int2 traceResolution = passData.traceResolution;
                    int2 tileCount = passData.tileCount;

                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_ResolutionID, new Vector4(resolution.x, resolution.y, 1.0f / resolution.x, 1.0f / resolution.y));
                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_TraceResolutionID, new Vector4(traceResolution.x, traceResolution.y, 1.0f / traceResolution.x, 1.0f / traceResolution.y));
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_TraceScaleID, passData.traceScale);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeIntParam(shader, Shader.PropertyToID("SSGI_PreviousFrameIndex"), passData.previousFrameIndex);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_IntensityID, settings.intensity);
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_HiZStepsID, math.max(1, settings.hiZSteps));
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_SamplesID, math.clamp(settings.samples, 1, 4));
                    cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_TraceFlagsID, (settings.traceBehind ? 1 : 0) | (settings.traceToward ? 2 : 0));
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_TemporalWeightID, settings.temporalWeight);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_TemporalClampScaleID, settings.temporalClampScale);
                    cmdEncoder.SetComputeFloatParam(shader, SSGIPassUtilityData.SSGI_HistoryValidID, passData.historyValid);
                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_CameraPositionID, passData.cameraPosition);
                    cmdEncoder.SetComputeVectorParam(shader, SSGIPassUtilityData.SSGI_CameraForwardID, passData.cameraForward);
                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.ScreenSpaceDepthParamsID, passData.depthParameters);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_HistoryViewProjID, passData.matrix_HistoryViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_MotionViewProjID, passData.matrix_MotionViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_LastMotionViewProjID, passData.matrix_LastMotionViewProj);

                    cmdEncoder.SetComputeFloatParam(shader, Shader.PropertyToID("SSGI_FrameAngle"), (passData.frameIndex & 63) * 2.39996323f);
                    cmdEncoder.SetComputeIntParam(shader, Shader.PropertyToID("SSGI_HasAO"), passData.hasAO);
                    cmdEncoder.BeginSample("SSGI_Trace");
                    int trace = passData.kernels[passData.captureRayHit ? 4 : 0];
                    BindSSGISurface(cmdEncoder, shader, trace, passData);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSGIPassUtilityData.SRV_HiZClosestTextureID, passData.hiZTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.SRV_HiZFurthestTextureID, passData.hiZFurthestTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.RankingTileID, passData.rankingTile);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.ScramblingTileID, passData.scramblingTile);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.OwenScrambledSobolID, passData.owenScrambledSobol);
                    if (passData.captureRayHit) cmdEncoder.SetComputeTextureParam(shader, trace, SSGIPassUtilityData.UAV_RayHitID, passData.rayHit);
                    BindTemporalColorView(cmdEncoder, shader, trace, passData.temporalColor);
                    cmdEncoder.SetComputeTextureParam(shader, trace, Shader.PropertyToID("SRV_HitMotionMetadata"), passData.motionMetadata);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSGIPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSGIPassUtilityData.UAV_TraceRadianceID, passData.traceRadiance);
                    cmdEncoder.SetComputeTextureParam(shader, trace, Shader.PropertyToID("UAV_HitDistance"), passData.hitDistance);
                    cmdEncoder.DispatchCompute(shader, trace, tileCount.x, tileCount.y, 1);
                    cmdEncoder.EndSample("SSGI_Trace");
                    passData.traceSnapshot.Record(cmdEncoder, passData.traceRadiance);

                    int groupsX = (traceResolution.x + ScreenSpaceTraceUtility.ResolveGroupSize - 1) / ScreenSpaceTraceUtility.ResolveGroupSize;
                    int groupsY = (traceResolution.y + ScreenSpaceTraceUtility.ResolveGroupSize - 1) / ScreenSpaceTraceUtility.ResolveGroupSize;
                    int temporalKernel = passData.kernels[1];
                    int spatialKernel = passData.kernels[2];
                    RecordSSGITemporal(cmdEncoder, shader, temporalKernel, "SSGI_Temporal1", passData.traceRadiance, passData.historyRadiance, passData.temporal, passData);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSGI_Temporal1");
                    RecordSSGISpatial(cmdEncoder, shader, spatialKernel, "SSGI_SpatialWide", 0, passData.temporal, passData.combinerWide, passData);
                    cmdEncoder.DispatchCompute(shader, spatialKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSGI_SpatialWide");
                    RecordSSGITemporal(cmdEncoder, shader, temporalKernel, "SSGI_Temporal2", passData.combinerWide, passData.historyNarrow, passData.temporalNarrow, passData);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSGI_Temporal2");
                    RecordSSGISpatial(cmdEncoder, shader, spatialKernel, "SSGI_SpatialNarrow", 1, passData.temporalNarrow, passData.combinerNarrow, passData);
                    cmdEncoder.DispatchCompute(shader, spatialKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSGI_SpatialNarrow");

                    cmdEncoder.BeginSample("SSGI_Upsample");
                    int upsample = passData.kernels[3];
                    BindSSGISurface(cmdEncoder, shader, upsample, passData);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, Shader.PropertyToID("SRV_Filtered"), passData.combinerNarrow);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, Shader.PropertyToID("SRV_Meta"), passData.meta);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, SSGIPassUtilityData.UAV_OutputID, passData.output);
                    cmdEncoder.DispatchCompute(shader, upsample, (resolution.x + 7) / 8, (resolution.y + 7) / 8, 1);
                    cmdEncoder.EndSample("SSGI_Upsample");



                });
            }

            MarkFeatureProduced(EFrameFeature.SSGI);
        }

        static void RecordSSGITemporal(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, string marker,
            RGTextureHandle current, RGTextureHandle history, RGTextureHandle output, in SSGIPassData passData)
        {
            cmdEncoder.BeginSample(marker);
            BindSSGISurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_HitMotionMetadata"), passData.motionMetadata);
            cmdEncoder.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_Current"), current);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_HistoryRadianceID, history);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_HistoryMetaID, passData.historyMeta);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.UAV_TemporalID, output);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.UAV_MetaID, passData.meta);
        }

        static void RecordSSGISpatial(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, string marker, int pass,
            RGTextureHandle current, RGTextureHandle output, in SSGIPassData passData)
        {
            cmdEncoder.BeginSample(marker);
            BindSSGISurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeIntParam(shader, Shader.PropertyToID("SSGI_SpatialPass"), pass);
            cmdEncoder.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_Temporal"), current);
            cmdEncoder.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_Meta"), passData.meta);
            cmdEncoder.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_Occlusion"), passData.hasAO != 0 ? passData.occlusion : passData.depthTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.UAV_OutputID, output);
        }

        // Depth and GBuffer inputs shared by every kernel that tests or reconstructs the surface.
        static void BindSSGISurface(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, in SSGIPassData passData)
        {
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_GBufferBID, passData.gBufferB);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_GBufferCID, passData.gBufferC);
        }
    }
}
