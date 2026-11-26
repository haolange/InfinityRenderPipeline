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
    // GPU-RECORD: CBV_SSGIParameters Shaders/Pass/ScreenSpace/Compute_ScreenSpaceIndirectDiffuse.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct SSGIParameters
    {
        public Vector4 resolution;
        public Vector4 traceResolution;
        public uint traceScale;
        public uint frameIndex;
        public uint previousFrameIndex;
        public float intensity;
        public uint hiZSteps;
        public uint traceFlags;
        public uint samples;
        public float frameAngle;
        public float temporalWeight;
        public float temporalClampScale;
        public float historyValid;
        public uint padding0;
    }

    // One 8x8 group traces its own pixels, then two temporal-then-world-space denoise stages, then a full-resolution upsample.
    internal static class SSGIPassUtilityData
    {
        internal static readonly int CBV_SSGIParametersID = Shader.PropertyToID("CBV_SSGIParameters");
        internal static readonly int SRV_CurrentID = Shader.PropertyToID("SRV_Current");
        internal static readonly int SRV_FilteredID = Shader.PropertyToID("SRV_Filtered");
        internal static readonly int SRV_OcclusionID = Shader.PropertyToID("SRV_Occlusion");
        internal static readonly int SSGI_SpatialPassID = Shader.PropertyToID("SSGI_SpatialPass");
        internal static readonly ProfilingSampler SSGITraceSampler = new ProfilingSampler("SSGI_Trace");
        internal static readonly ProfilingSampler SSGITemporal1Sampler = new ProfilingSampler("SSGI_Temporal1");
        internal static readonly ProfilingSampler SSGISpatialWideSampler = new ProfilingSampler("SSGI_SpatialWide");
        internal static readonly ProfilingSampler SSGITemporal2Sampler = new ProfilingSampler("SSGI_Temporal2");
        internal static readonly ProfilingSampler SSGISpatialNarrowSampler = new ProfilingSampler("SSGI_SpatialNarrow");
        internal static readonly ProfilingSampler SSGIUpsampleSampler = new ProfilingSampler("SSGI_Upsample");
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

    internal sealed class SSGIPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly RenderCapturePass m_RenderCapturePass;
#endif

        internal SSGIPass(RGBuilder graph, InfinityRenderPipelineResources resources
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

        struct SSGIPassData
        {
            public RGConstantsHandle parametersHandle;
            public int2 tileCount;
            public int traceKernel;
            public int temporalKernel;
            public int spatialKernel;
            public int upsampleKernel;
            public int2 denoiseGroups;
            public int2 upsampleGroups;
            public ComputeShader ssgiShader;
            public Texture2D rankingTile;
            public Texture2D scramblingTile;
            public Texture2D owenScrambledSobol;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle hiZTextureHandle;
            public RGTextureHandle hiZFurthestTextureHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle surfaceMetadataHandle;
            public TemporalColorView temporalColor;
            public RGTextureHandle motionMetadataHandle;
            public RGTextureHandle motionTextureHandle;
            public RGTextureHandle historyRadianceHandle;
            public RGTextureHandle historyNarrowHandle;
            public RGTextureHandle historyMetaHandle;
            public RGTextureHandle occlusionHandle;
            public bool hasAO;
            public LocalKeyword occlusionKeyword;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public RGTextureHandle rayHitHandle;
            public bool captureRayHit;
            public int traceDebugKernel;
            public RenderCapturePass.CaptureStageSnapshot traceSnapshot;
#endif
            public RGTextureHandle traceRadianceHandle;
            public RGTextureHandle hitDistanceHandle;
            public RGTextureHandle temporalHandle;
            public RGTextureHandle temporalNarrowHandle;
            public RGTextureHandle combinerWideHandle;
            public RGTextureHandle combinerNarrowHandle;
            public RGTextureHandle metaHandle;
            public RGTextureHandle outputHandle;
        }

        internal void ComputeScreenSpaceIndirect(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SSGI))
            {
                return;
            }

            SSGISettings settings = context.volumes.screenSpaceIndirectDiffuse.Resolve();
            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            int2 resolution = new int2(width, height);
            int2 traceResolution = ScreenSpaceTraceUtility.TraceSize(resolution, true);
            int workWidth = traceResolution.x, workHeight = traceResolution.y;
            const int divisor = 2;
            int2 tileCount = ScreenSpaceTraceUtility.TileCount(traceResolution);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var capture = RenderCaptureService.current;
            bool captureRayHit = capture != null && capture.CaptureThisFrame && capture.Captures(ECaptureBuffer.SSGIRayHit);
            RGTextureHandle rayHitHandle = captureRayHit ? m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIRayHitBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.RayHitName, GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point, divisor)) : default;
#endif
            RGTextureHandle traceRadianceHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGITraceRadianceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.TraceRadianceName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle hitDistanceHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIHitDistanceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGIHitDistance", GraphicsFormat.R32_SFloat, FilterMode.Point, divisor));
            RGTextureHandle temporalHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGITemporalBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.TemporalName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle combinerWideHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIFilteredBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGICombinerWide", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle metaHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIMetaBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.MetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor));
            RGTextureHandle outputHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGIBuffer,
                SSRPassUtilityData.CreateDescriptor(width, height, SSGIPassUtilityData.OutputName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SSGIBuffer, outputHandle);
            TextureDescriptor historyRadianceDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.HistoryRadianceName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor);
            TextureDescriptor historyMetaDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSGIPassUtilityData.HistoryMetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor);
            historyMetaDsc.filterMode = FilterMode.Point;
            TextureDescriptor historyNarrowDsc = historyRadianceDsc;
            historyNarrowDsc.name = "HistorySSGINarrow";
            HistoryRead radianceRead = context.historyCache.Read(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadianceDsc, context.resetsHistory);
            HistoryRead narrowRead = context.historyCache.Read(InfinityShaderIDs.HistorySSGINarrowBuffer, historyNarrowDsc, context.resetsHistory);
            HistoryRead metaRead = context.historyCache.Read(InfinityShaderIDs.HistorySSGIMetaBuffer, historyMetaDsc, context.resetsHistory);
            RGTextureHandle historyRadianceHandle = m_RGBuilder.ImportTexture(radianceRead.textureHandle);
            RGTextureHandle historyNarrowHandle = m_RGBuilder.ImportTexture(narrowRead.textureHandle);
            RGTextureHandle historyMetaHandle = m_RGBuilder.ImportTexture(metaRead.textureHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSGIRadianceBuffer, historyRadianceHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSGINarrowBuffer, historyNarrowHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSGIMetaBuffer, historyMetaHandle);
            ScreenSpaceCameraState ssState = context.state.GetScreenSpaceState();
            bool historyValid = ssState.acceptedSSGI && radianceRead.valid && narrowRead.valid && metaRead.valid;
            bool hasAO = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);
            RGTextureHandle occlusionHandle = hasAO ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer) : default;

            InfinityRenderPipelineRuntimeTextures textures = m_Resources.textures;

            {
                RGComputePassHandle<SSGIPassData> passHandle = m_RGBuilder.AddComputePass<SSGIPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceIndirect));
                ref SSGIPassData passData = ref passHandle.GetPassData();
                passData.parametersHandle = passHandle.WriteConstants(new SSGIParameters
                {
                    resolution = new Vector4(resolution.x, resolution.y, 1.0f / resolution.x, 1.0f / resolution.y),
                    traceResolution = new Vector4(traceResolution.x, traceResolution.y, 1.0f / traceResolution.x, 1.0f / traceResolution.y),
                    traceScale = 2,
                    frameIndex = (uint)context.frame,
                    previousFrameIndex = (uint)ssState.acceptedSSGIFrameIndex,
                    intensity = settings.intensity,
                    hiZSteps = (uint)math.max(1, settings.hiZSteps),
                    traceFlags = (uint)((settings.traceBehind ? 1 : 0) | (settings.traceToward ? 2 : 0)),
                    samples = (uint)math.clamp(settings.samples, 1, 4),
                    frameAngle = (context.frame & 63) * 2.39996323f,
                    temporalWeight = settings.temporalWeight,
                    temporalClampScale = settings.temporalClampScale,
                    historyValid = historyValid ? 1 : 0
                });
                passData.hasAO = hasAO;
                passData.occlusionKeyword = m_Resources.kernels.ssgiOcclusion;
                passData.tileCount = tileCount;
                passData.ssgiShader = m_Resources.shaders.ssgiShader;
                passData.traceKernel = m_Resources.kernels.ssgiTrace.index;
                passData.temporalKernel = m_Resources.kernels.ssgiTemporal.index;
                passData.spatialKernel = m_Resources.kernels.ssgiSpatial.index;
                passData.upsampleKernel = m_Resources.kernels.ssgiUpsample.index;
                passData.denoiseGroups = new int2(m_Resources.kernels.ssgiTemporal.GroupsX(traceResolution.x), m_Resources.kernels.ssgiTemporal.GroupsY(traceResolution.y));
                passData.upsampleGroups = new int2(m_Resources.kernels.ssgiUpsample.GroupsX(resolution.x), m_Resources.kernels.ssgiUpsample.GroupsY(resolution.y));
                passData.rankingTile = textures.blueNoiseRankingTile;
                passData.scramblingTile = textures.blueNoiseScramblingTile;
                passData.owenScrambledSobol = textures.blueNoiseOwenScrambledSobol;

                passData.depthTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.hiZTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZClosestBuffer));
                passData.hiZFurthestTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZFurthestBuffer));
                passData.gBufferBHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.surfaceMetadataHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SurfaceMetadataBuffer));
                passData.temporalColor = TemporalColorSourcePass.ReadTemporalColorView(passHandle, context.state.GetTemporalState().temporalColorView);
                passData.motionMetadataHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                passData.motionTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                passData.historyRadianceHandle = passHandle.ReadTexture(historyRadianceHandle);
                passData.historyNarrowHandle = passHandle.ReadTexture(historyNarrowHandle);
                passData.historyMetaHandle = passHandle.ReadTexture(historyMetaHandle);
                passData.occlusionHandle = hasAO ? passHandle.ReadTexture(occlusionHandle) : default;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.captureRayHit = captureRayHit;
                passData.traceDebugKernel = m_Resources.kernels.ssgiTraceDebug.index;
                passData.rayHitHandle = captureRayHit ? passHandle.WriteTexture(rayHitHandle) : default;
                passData.traceSnapshot = m_RenderCapturePass.PrepareStageSnapshot(passHandle, ECaptureBuffer.SSGITraceRadiance, traceRadianceHandle);
#endif
                passData.traceRadianceHandle = passHandle.WriteTexture(traceRadianceHandle);
                passData.hitDistanceHandle = passHandle.WriteTexture(hitDistanceHandle);
                passData.temporalHandle = passHandle.WriteTexture(temporalHandle);
                passData.temporalNarrowHandle = passHandle.CreatePassLocalTexture(SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGITemporalNarrow", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
                passData.combinerWideHandle = passHandle.WriteTexture(combinerWideHandle);
                passData.combinerNarrowHandle = passHandle.WriteTexture(m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSGICombinerNarrowBuffer,
                    SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSGICombinerNarrow", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor)));
                passData.metaHandle = passHandle.WriteTexture(metaHandle);
                passData.outputHandle = passHandle.WriteTexture(outputHandle);

                passHandle.EnablePassCulling(false);
                passHandle.EnableAsyncCompute(true);
                passHandle.SetExecuteFunc((in SSGIPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.ssgiShader;
                    int2 tileCount = passData.tileCount;
                    cmdEncoder.SetComputeConstantBufferParam(shader, SSGIPassUtilityData.CBV_SSGIParametersID, passData.parametersHandle);

                    cmdEncoder.BeginSample(SSGIPassUtilityData.SSGITraceSampler);
                    int trace = passData.traceKernel;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (passData.captureRayHit)
                    {
                        trace = passData.traceDebugKernel;
                    }
#endif
                    BindSSGISurface(cmdEncoder, shader, trace, passData);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_HiZClosestTexture, passData.hiZTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.SRV_HiZFurthestTextureID, passData.hiZFurthestTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.RankingTileID, passData.rankingTile, TextureView.Whole(passData.rankingTile));
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.ScramblingTileID, passData.scramblingTile, TextureView.Whole(passData.scramblingTile));
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSRPassUtilityData.OwenScrambledSobolID, passData.owenScrambledSobol, TextureView.Whole(passData.owenScrambledSobol));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (passData.captureRayHit)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.UAV_RayHit, passData.rayHitHandle);
                    }
#endif
                    TemporalColorSourcePass.BindTemporalColorView(cmdEncoder, shader, trace, passData.temporalColor);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_HitMotionMetadata, passData.motionMetadataHandle);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.SRV_MotionTexture, passData.motionTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, trace, SSGIPassUtilityData.UAV_TraceRadianceID, passData.traceRadianceHandle);
                    cmdEncoder.SetComputeTextureParam(shader, trace, PassShaderIDs.UAV_HitDistance, passData.hitDistanceHandle);
                    cmdEncoder.DispatchCompute(shader, trace, tileCount.x, tileCount.y, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGITraceSampler);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    passData.traceSnapshot.Record(cmdEncoder, passData.traceRadianceHandle);
#endif

                    int groupsX = passData.denoiseGroups.x;
                    int groupsY = passData.denoiseGroups.y;
                    int temporalKernel = passData.temporalKernel;
                    int spatialKernel = passData.spatialKernel;
                    RecordSSGITemporal(cmdEncoder, shader, temporalKernel, SSGIPassUtilityData.SSGITemporal1Sampler, passData.traceRadianceHandle, passData.historyRadianceHandle, passData.temporalHandle, passData);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGITemporal1Sampler);
                    RecordSSGISpatial(cmdEncoder, shader, spatialKernel, SSGIPassUtilityData.SSGISpatialWideSampler, 0, passData.temporalHandle, passData.combinerWideHandle, passData);
                    cmdEncoder.DispatchCompute(shader, spatialKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGISpatialWideSampler);
                    RecordSSGITemporal(cmdEncoder, shader, temporalKernel, SSGIPassUtilityData.SSGITemporal2Sampler, passData.combinerWideHandle, passData.historyNarrowHandle, passData.temporalNarrowHandle, passData);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGITemporal2Sampler);
                    RecordSSGISpatial(cmdEncoder, shader, spatialKernel, SSGIPassUtilityData.SSGISpatialNarrowSampler, 1, passData.temporalNarrowHandle, passData.combinerNarrowHandle, passData);
                    cmdEncoder.DispatchCompute(shader, spatialKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGISpatialNarrowSampler);

                    cmdEncoder.BeginSample(SSGIPassUtilityData.SSGIUpsampleSampler);
                    int upsample = passData.upsampleKernel;
                    BindSSGISurface(cmdEncoder, shader, upsample, passData);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, SSGIPassUtilityData.SRV_FilteredID, passData.combinerNarrowHandle);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.SRV_Meta, passData.metaHandle);
                    cmdEncoder.SetComputeTextureParam(shader, upsample, PassShaderIDs.UAV_Output, passData.outputHandle);
                    cmdEncoder.DispatchCompute(shader, upsample, passData.upsampleGroups.x, passData.upsampleGroups.y, 1);
                    cmdEncoder.EndSample(SSGIPassUtilityData.SSGIUpsampleSampler);



                });
            }

            context.features.MarkProduced(EFrameFeature.SSGI);
        }

        static void RecordSSGITemporal(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, ProfilingSampler marker,
            RGTextureHandle currentHandle, RGTextureHandle historyHandle, RGTextureHandle outputHandle, in SSGIPassData passData)
        {
            cmdEncoder.BeginSample(marker);
            BindSSGISurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HitMotionMetadata, passData.motionMetadataHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_CurrentID, currentHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTextureHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HistoryRadiance, historyHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HistoryMeta, passData.historyMetaHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_Temporal, outputHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_Meta, passData.metaHandle);
        }

        static void RecordSSGISpatial(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, ProfilingSampler marker, int pass,
            RGTextureHandle currentHandle, RGTextureHandle outputHandle, in SSGIPassData passData)
        {
            cmdEncoder.BeginSample(marker);
            BindSSGISurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeIntParam(shader, SSGIPassUtilityData.SSGI_SpatialPassID, pass);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_Temporal, currentHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_Meta, passData.metaHandle);
            cmdEncoder.SetKeyword(shader, passData.occlusionKeyword, passData.hasAO);
            if (passData.hasAO)
            {
                cmdEncoder.SetComputeTextureParam(shader, kernel, SSGIPassUtilityData.SRV_OcclusionID, passData.occlusionHandle);
            }
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_Output, outputHandle);
        }

        // Depth and GBuffer inputs shared by every kernel that tests or reconstructs the surface.
        static void BindSSGISurface(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, in SSGIPassData passData)
        {
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferB, passData.gBufferBHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_SurfaceMetadata, passData.surfaceMetadataHandle);
        }
    }
}
