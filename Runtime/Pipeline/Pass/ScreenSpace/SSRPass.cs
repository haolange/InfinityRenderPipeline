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
    // GPU-RECORD: CBV_SSRParameters Shaders/Pass/ScreenSpace/Compute_ScreenSpaceReflection.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct SSRParameters
    {
        public Vector4 resolution;
        public Vector4 traceResolution;
        public uint traceScale;
        public uint frameIndex;
        public float maxRoughness;
        public float edgeFade;
        public float mirrorRoughness;
        public float linearRoughness;
        public float brdfBias;
        public uint hiZSteps;
        public uint linearSteps;
        public uint binarySteps;
        public uint traceFlags;
        public uint resolveSamples;
        public uint samples;
        public float resolveRadius;
        public float temporalWeight;
        public float temporalClampScale;
        public float historyValid;
        public int debugMode;
        public int padding0;
        public int padding1;
    }

    // Stochastic SSR (Frostbite): classify -> trace (Hi-Z or linear, indirect per tile class) -> resolve with ray reuse
    // -> temporal -> optional spatial. Kernel contracts are documented at the top of Compute_ScreenSpaceReflection.
    internal static class SSRPassUtilityData
    {
        internal static readonly int CBV_SSRParametersID = Shader.PropertyToID("CBV_SSRParameters");
        internal static readonly int SRV_TileListID = Shader.PropertyToID("SRV_TileList");
        internal static readonly int UAV_TileListID = Shader.PropertyToID("UAV_TileList");
        internal static readonly int SRV_HistoryVarianceID = Shader.PropertyToID("SRV_HistoryVariance");
        internal static readonly int SRV_HitDevID = Shader.PropertyToID("SRV_HitDev");
        internal static readonly int SRV_HitMeanID = Shader.PropertyToID("SRV_HitMean");
        internal static readonly int SRV_VarianceID = Shader.PropertyToID("SRV_Variance");
        internal static readonly int UAV_HitDevID = Shader.PropertyToID("UAV_HitDev");
        internal static readonly int UAV_HitMeanID = Shader.PropertyToID("UAV_HitMean");
        internal static readonly int UAV_VarianceID = Shader.PropertyToID("UAV_Variance");
        internal static readonly ProfilingSampler SSRClassifySampler = new ProfilingSampler("SSR_Classify");
        internal static readonly ProfilingSampler SSRTraceSampler = new ProfilingSampler("SSR_Trace");
        internal static readonly ProfilingSampler SSRResolveSampler = new ProfilingSampler("SSR_Resolve");
        internal static readonly ProfilingSampler SSRTemporalSampler = new ProfilingSampler("SSR_Temporal");
        internal static readonly ProfilingSampler SSRBilateralSampler = new ProfilingSampler("SSR_Bilateral");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal static readonly ProfilingSampler SSRDebugSampler = new ProfilingSampler("SSR_Debug");
#endif
        // Tile classes; also the order of the argument triplets and of the tile list segments.
        internal const int ClassHiZ = 0;
        internal const int ClassLinear = 1;
        internal const int ClassSkip = 2;

        internal static readonly int RankingTileID = Shader.PropertyToID("_Ranking_Tile");
        internal static readonly int ScramblingTileID = Shader.PropertyToID("_Scrambled_Tile");
        internal static readonly int OwenScrambledSobolID = Shader.PropertyToID("_ScrambledOwen_Noise");
        internal static readonly int SRV_HiZFurthestTextureID = Shader.PropertyToID("SRV_HiZFurthestTexture");
        internal static readonly int SRV_GBufferAID = Shader.PropertyToID("SRV_GBufferA");
        internal static readonly int SRV_RayHitID = Shader.PropertyToID("SRV_RayHit");
        internal static readonly int SRV_ResolvedID = Shader.PropertyToID("SRV_Resolved");
        internal static readonly int SRV_HitDistanceID = Shader.PropertyToID("SRV_HitDistance");
        internal static readonly int SRV_TileClassID = Shader.PropertyToID("SRV_TileClass");
        internal static readonly int UAV_TileClassID = Shader.PropertyToID("UAV_TileClass");
        internal static readonly int UAV_SamplesID = Shader.PropertyToID("UAV_Samples");
        internal static readonly int SRV_SamplesID = Shader.PropertyToID("SRV_Samples");
        internal static readonly int UAV_ResolvedID = Shader.PropertyToID("UAV_Resolved");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal static readonly int UAV_DebugID = Shader.PropertyToID("UAV_Debug");
#endif

        internal const string RayHitName = "SSRRayHit";
        internal const string ResolvedName = "SSRResolved";
        internal const string HitDistanceName = "SSRHitDistance";
        internal const string TemporalName = "SSRTemporal";
        internal const string MetaName = "SSRMeta";
        internal const string OutputName = "SSRTexture";
        internal const string HistoryRadianceName = "HistorySSRRadiance";
        internal const string HistoryMetaName = "HistorySSRMeta";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal const string DebugName = "SSRDebug";

        internal static bool IsDebugView(EDebugView view) =>
            view == EDebugView.SSRTileClass || view == EDebugView.SSRRayHit || view == EDebugView.SSRConfidence || view == EDebugView.SSRHistoryAge;
#endif

        internal static TextureDescriptor CreateDescriptor(int width, int height, string name, GraphicsFormat format, FilterMode filter, int divisor = 1)
        {
            TextureDescriptor descriptor = TextureDescriptor.Create2D(name, width, height, format, ETextureSizingDomain.Internal, divisor);
            descriptor.filterMode = filter;
            return descriptor;
        }
    }

    internal sealed class SSRPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;

        internal SSRPass(RGBuilder graph, InfinityRenderPipelineResources resources)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
        }

        struct SSRPassData
        {
            public RGConstantsHandle parametersHandle;
            public int2 tileCount;
            public int clearArgsKernel;
            public int classifyKernel;
            public int traceKernel;
            public int resolveKernel;
            public int temporalKernel;
            public int bilateralKernel;
            public int2 resolveGroups;
            public ComputeShader ssrShader;
            public Texture2D rankingTile;
            public Texture2D scramblingTile;
            public Texture2D owenScrambledSobol;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle hiZTextureHandle;
            public RGTextureHandle hiZFurthestTextureHandle;
            public RGTextureHandle gBufferAHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle surfaceMetadataHandle;
            public TemporalColorView temporalColor;
            public RGTextureHandle motionMetadataHandle;
            public RGTextureHandle motionTextureHandle;
            public RGTextureHandle historyRadianceHandle;
            public RGTextureHandle historyMetaHandle;
            public RGTextureHandle historyVarianceHandle;
            public RGBufferHandle tileArgsHandle;
            public RGBufferHandle tileListHandle;
            public RGTextureHandle tileClassHandle;
            public RGTextureHandle rayHitHandle;
            public RGBufferHandle samplesHandle;
            public RGTextureHandle resolvedHandle;
            public RGTextureHandle hitMeanHandle;
            public RGTextureHandle hitDevHandle;
            public RGTextureHandle hitDistanceHandle;
            public RGTextureHandle varianceHandle;
            public RGTextureHandle temporalHandle;
            public RGTextureHandle metaHandle;
            public RGTextureHandle outputHandle;
            public RGTextureHandle filteredHandle;
            public bool runSpatial;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public RGTextureHandle debugHandle;
            public bool runDebug;
            public int debugKernel;
            public int2 debugGroups;
#endif
        }

        internal void ComputeScreenSpaceReflection(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SSR))
            {
                return;
            }

            SSRSettings settings = context.volumes.screenSpaceReflection.Resolve();
            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            int2 resolution = new int2(width, height);
            int2 traceResolution = ScreenSpaceTraceUtility.TraceSize(resolution, settings.halfResolution);
            int workWidth = width, workHeight = height;
            const int divisor = 1;
            int2 tileCount = ScreenSpaceTraceUtility.TileCount(traceResolution);
            int maxTiles = tileCount.x * tileCount.y;
            // The debug kernel reads SSR_DebugMode only; a release build writes 0.
            int debugMode = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugMode = (int)context.debugView;
            bool runDebug = SSRPassUtilityData.IsDebugView(context.debugView);
#endif
            bool runSpatial = settings.spatialPasses > 0;

            RGTextureHandle rayHitHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRRayHitBuffer,
                // Hit UV and depth are geometric data: half precision can move them into another depth texel.
                SSRPassUtilityData.CreateDescriptor(traceResolution.x, traceResolution.y, SSRPassUtilityData.RayHitName, GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point, settings.halfResolution ? 2 : 1));
            RGTextureHandle resolvedHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRResolvedBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.ResolvedName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle hitMeanHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitMeanBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRHitMean", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, divisor));
            RGTextureHandle hitDevHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitDevBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRHitDev", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, divisor));
            RGTextureHandle varianceHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRVarianceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRVariance", GraphicsFormat.R16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle hitDistanceHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitDistanceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRHitDistance", GraphicsFormat.R32_SFloat, FilterMode.Point, divisor));
            RGTextureHandle temporalHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRTemporalBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.TemporalName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle metaHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRMetaBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.MetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor));
            RGTextureHandle filteredHandle = runSpatial
                ? m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRFilteredBuffer,
                    SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRFiltered", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor))
                : temporalHandle;
            if (!runSpatial)
            {
                m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SSRFilteredBuffer, filteredHandle);
            }
            RGTextureHandle outputHandle = filteredHandle;
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SSRBuffer, outputHandle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RGTextureHandle debugHandle = default;
            if (runDebug)
            {
                debugHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRDebugBuffer,
                    SSRPassUtilityData.CreateDescriptor(width, height, SSRPassUtilityData.DebugName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point));
            }
#endif

            TextureDescriptor historyRadianceDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.HistoryRadianceName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor);
            TextureDescriptor historyMetaDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.HistoryMetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor);
            historyMetaDsc.filterMode = FilterMode.Point;
            TextureDescriptor historyVarianceDsc = SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "HistorySSRVariance", GraphicsFormat.R16_SFloat, FilterMode.Bilinear, divisor);
            HistoryRead radianceRead = context.historyCache.Read(InfinityShaderIDs.HistorySSRRadianceBuffer, historyRadianceDsc, context.resetsHistory);
            HistoryRead metaRead = context.historyCache.Read(InfinityShaderIDs.HistorySSRMetaBuffer, historyMetaDsc, context.resetsHistory);
            HistoryRead varianceRead = context.historyCache.Read(InfinityShaderIDs.HistorySSRVarianceBuffer, historyVarianceDsc, context.resetsHistory);
            RGTextureHandle historyRadianceHandle = m_RGBuilder.ImportTexture(radianceRead.textureHandle);
            RGTextureHandle historyMetaHandle = m_RGBuilder.ImportTexture(metaRead.textureHandle);
            RGTextureHandle historyVarianceHandle = m_RGBuilder.ImportTexture(varianceRead.textureHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSRRadianceBuffer, historyRadianceHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSRMetaBuffer, historyMetaHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSRVarianceBuffer, historyVarianceHandle);
            bool historyValid = context.state.GetScreenSpaceState().acceptedSSR && radianceRead.valid && metaRead.valid && varianceRead.valid;

            InfinityRenderPipelineRuntimeTextures textures = m_Resources.textures;

            {
                RGComputePassHandle<SSRPassData> passHandle = m_RGBuilder.AddComputePass<SSRPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceReflection));
                ref SSRPassData passData = ref passHandle.GetPassData();
                passData.parametersHandle = passHandle.WriteConstants(new SSRParameters
                {
                    resolution = new Vector4(resolution.x, resolution.y, 1.0f / resolution.x, 1.0f / resolution.y),
                    traceResolution = new Vector4(traceResolution.x, traceResolution.y, 1.0f / traceResolution.x, 1.0f / traceResolution.y),
                    traceScale = (uint)(settings.halfResolution ? 2 : 1),
                    frameIndex = (uint)context.frame,
                    maxRoughness = settings.maxRoughness,
                    edgeFade = settings.edgeFade,
                    mirrorRoughness = settings.mirrorRoughness,
                    linearRoughness = settings.linearRoughness,
                    brdfBias = settings.brdfBias,
                    hiZSteps = (uint)math.max(1, settings.hiZSteps),
                    linearSteps = (uint)math.max(1, settings.linearSteps),
                    binarySteps = (uint)math.max(0, settings.binarySteps),
                    traceFlags = (uint)((settings.traceBehind ? 1 : 0) | (settings.traceToward ? 2 : 0)),
                    resolveSamples = (uint)math.max(1, settings.resolveSamples),
                    samples = (uint)math.clamp(settings.samples, 1, 4),
                    resolveRadius = settings.resolveRadius,
                    temporalWeight = settings.temporalWeight,
                    temporalClampScale = settings.temporalClampScale,
                    historyValid = historyValid ? 1 : 0,
                    debugMode = debugMode
                });
                passData.tileCount = tileCount;
                passData.ssrShader = m_Resources.shaders.ssrShader;
                passData.clearArgsKernel = m_Resources.kernels.ssrClearArgs.index;
                passData.classifyKernel = m_Resources.kernels.ssrClassify.index;
                passData.traceKernel = m_Resources.kernels.ssrTrace.index;
                passData.resolveKernel = m_Resources.kernels.ssrResolve.index;
                passData.temporalKernel = m_Resources.kernels.ssrTemporal.index;
                passData.bilateralKernel = m_Resources.kernels.ssrBilateral.index;
                passData.resolveGroups = new int2(m_Resources.kernels.ssrResolve.GroupsX(width), m_Resources.kernels.ssrResolve.GroupsY(height));
                passData.rankingTile = textures.blueNoiseRankingTile;
                passData.scramblingTile = textures.blueNoiseScramblingTile;
                passData.owenScrambledSobol = textures.blueNoiseOwenScrambledSobol;

                passData.depthTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.hiZTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZClosestBuffer));
                passData.hiZFurthestTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZFurthestBuffer));
                passData.gBufferAHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA));
                passData.gBufferBHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.surfaceMetadataHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SurfaceMetadataBuffer));
                passData.temporalColor = TemporalColorSourcePass.ReadTemporalColorView(passHandle, context.state.GetTemporalState().temporalColorView);
                passData.motionMetadataHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                passData.motionTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                passData.historyRadianceHandle = passHandle.ReadTexture(historyRadianceHandle);
                passData.historyMetaHandle = passHandle.ReadTexture(historyMetaHandle);
                passData.historyVarianceHandle = passHandle.ReadTexture(historyVarianceHandle);

                passData.tileArgsHandle = passHandle.CreatePassLocalBuffer(new BufferDescriptor(3, sizeof(uint), GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured) { name = "SSRTileArgs" });
                passData.tileListHandle = passHandle.CreatePassLocalBuffer(new BufferDescriptor(maxTiles, sizeof(uint), GraphicsBuffer.Target.Structured) { name = "SSRTileList" });
                TextureDescriptor tileClassDescriptor = SSRPassUtilityData.CreateDescriptor(tileCount.x, tileCount.y, "SSRTileClass", GraphicsFormat.R32_UInt, FilterMode.Point, 8 * (settings.halfResolution ? 2 : 1));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                RenderCaptureSession capture = RenderCaptureService.current;
                bool captureTileClass = capture != null && capture.CaptureThisFrame && capture.request.Matches(context.camera)
                    && capture.Captures(ECaptureBuffer.SSRTileClass);
                // A captured classification must outlive this pass for the graph-owned Transfer readback.
                passData.tileClassHandle = captureTileClass
                    ? passHandle.WriteTexture(m_RGBuilder.scoper.CreateAndRegisterTexture(PassShaderIDs.Capture_SSRTileClass, tileClassDescriptor))
                    : passHandle.CreatePassLocalTexture(tileClassDescriptor);
#else
                passData.tileClassHandle = passHandle.CreatePassLocalTexture(tileClassDescriptor);
#endif
                passData.rayHitHandle = passHandle.WriteTexture(rayHitHandle);
                passData.samplesHandle = passHandle.CreatePassLocalBuffer(new BufferDescriptor(traceResolution.x * traceResolution.y * 4, 32, GraphicsBuffer.Target.Structured) { name = "SSRSamples" });
                passData.resolvedHandle = passHandle.WriteTexture(resolvedHandle);
                passData.hitMeanHandle = passHandle.WriteTexture(hitMeanHandle);
                passData.hitDevHandle = passHandle.WriteTexture(hitDevHandle);
                passData.hitDistanceHandle = passHandle.WriteTexture(hitDistanceHandle);
                passData.varianceHandle = passHandle.WriteTexture(varianceHandle);
                passData.temporalHandle = passHandle.WriteTexture(temporalHandle);
                passData.metaHandle = passHandle.WriteTexture(metaHandle);
                passData.runSpatial = runSpatial;
                passData.filteredHandle = runSpatial ? passHandle.WriteTexture(filteredHandle) : passData.temporalHandle;
                passData.outputHandle = passData.filteredHandle;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.runDebug = runDebug;
                passData.debugHandle = runDebug ? passHandle.WriteTexture(debugHandle) : default;
                passData.debugKernel = m_Resources.kernels.ssrDebug.index;
                passData.debugGroups = new int2(m_Resources.kernels.ssrDebug.GroupsX(width), m_Resources.kernels.ssrDebug.GroupsY(height));
#endif

                passHandle.EnablePassCulling(false);
                passHandle.EnableAsyncCompute(true);
                passHandle.SetExecuteFunc((in SSRPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.ssrShader;
                    int2 tileCount = passData.tileCount;
                    cmdEncoder.SetComputeConstantBufferParam(shader, SSRPassUtilityData.CBV_SSRParametersID, passData.parametersHandle);

                    cmdEncoder.BeginSample(SSRPassUtilityData.SSRClassifySampler);
                    int clearArgs = passData.clearArgsKernel;
                    cmdEncoder.SetComputeBufferParam(shader, clearArgs, PassShaderIDs.UAV_TileArgs, passData.tileArgsHandle);
                    cmdEncoder.DispatchCompute(shader, clearArgs, 1, 1, 1);
                    int classify = passData.classifyKernel;
                    BindSSRSurface(cmdEncoder, shader, classify, passData);
                    cmdEncoder.SetComputeBufferParam(shader, classify, PassShaderIDs.UAV_TileArgs, passData.tileArgsHandle);
                    cmdEncoder.SetComputeBufferParam(shader, classify, SSRPassUtilityData.UAV_TileListID, passData.tileListHandle);
                    cmdEncoder.SetComputeTextureParam(shader, classify, SSRPassUtilityData.UAV_TileClassID, passData.tileClassHandle);
                    cmdEncoder.SetComputeTextureParam(shader, classify, PassShaderIDs.UAV_RayHit, passData.rayHitHandle);
                    cmdEncoder.SetComputeBufferParam(shader, classify, SSRPassUtilityData.UAV_SamplesID, passData.samplesHandle);
                    cmdEncoder.DispatchCompute(shader, classify, tileCount.x, tileCount.y, 1);
                    cmdEncoder.EndSample(SSRPassUtilityData.SSRClassifySampler);

                    cmdEncoder.BeginSample(SSRPassUtilityData.SSRTraceSampler);
                    RecordSSRTrace(cmdEncoder, shader, passData.traceKernel, passData);
                    cmdEncoder.EndSample(SSRPassUtilityData.SSRTraceSampler);

                    int groupsX = passData.resolveGroups.x;
                    int groupsY = passData.resolveGroups.y;

                    // Resolve
                    cmdEncoder.BeginSample(SSRPassUtilityData.SSRResolveSampler);
                    int resolve = passData.resolveKernel;
                    BindSSRSurface(cmdEncoder, shader, resolve, passData);
                    cmdEncoder.SetComputeBufferParam(shader, resolve, SSRPassUtilityData.SRV_SamplesID, passData.samplesHandle);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_ResolvedID, passData.resolvedHandle);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_HitMeanID, passData.hitMeanHandle);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_HitDevID, passData.hitDevHandle);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, PassShaderIDs.UAV_HitDistance, passData.hitDistanceHandle);
                    cmdEncoder.DispatchCompute(shader, resolve, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSRPassUtilityData.SSRResolveSampler);

                    // Temporal
                    cmdEncoder.BeginSample(SSRPassUtilityData.SSRTemporalSampler);
                    int temporalKernel = passData.temporalKernel;
                    BindSSRSurface(cmdEncoder, shader, temporalKernel, passData);
                    cmdEncoder.SetComputeTextureParam(shader,temporalKernel,PassShaderIDs.SRV_HitMotionMetadata,passData.motionMetadataHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_ResolvedID, passData.resolvedHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HitMeanID, passData.hitMeanHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HitDevID, passData.hitDevHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HitDistanceID, passData.hitDistanceHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HistoryVarianceID, passData.historyVarianceHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.UAV_VarianceID, passData.varianceHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.SRV_MotionTexture, passData.motionTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.SRV_HistoryRadiance, passData.historyRadianceHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.SRV_HistoryMeta, passData.historyMetaHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.UAV_Temporal, passData.temporalHandle);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.UAV_Meta, passData.metaHandle);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSRPassUtilityData.SSRTemporalSampler);

                    if (passData.runSpatial)
                    {
                        cmdEncoder.BeginSample(SSRPassUtilityData.SSRBilateralSampler);
                        int bilateral = passData.bilateralKernel;
                        BindSSRSurface(cmdEncoder, shader, bilateral, passData);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, PassShaderIDs.SRV_Temporal, passData.temporalHandle);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSRPassUtilityData.SRV_VarianceID, passData.varianceHandle);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, PassShaderIDs.UAV_Output, passData.filteredHandle);
                        cmdEncoder.DispatchCompute(shader, bilateral, groupsX, groupsY, 1);
                        cmdEncoder.EndSample(SSRPassUtilityData.SSRBilateralSampler);
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (passData.runDebug)
                    {
                        cmdEncoder.BeginSample(SSRPassUtilityData.SSRDebugSampler);
                        int debugKernel = passData.debugKernel;
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_TileClassID, passData.tileClassHandle);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_RayHitID, passData.rayHitHandle);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_ResolvedID, passData.resolvedHandle);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, PassShaderIDs.SRV_Meta, passData.metaHandle);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.UAV_DebugID, passData.debugHandle);
                        cmdEncoder.DispatchCompute(shader, debugKernel, passData.debugGroups.x, passData.debugGroups.y, 1);
                        cmdEncoder.EndSample(SSRPassUtilityData.SSRDebugSampler);
                    }
#endif
                });
            }

            context.features.MarkProduced(EFrameFeature.SSR);
        }

        // Depth and GBuffer inputs shared by every kernel that tests or reconstructs the surface.
        static void BindSSRSurface(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, in SSRPassData passData)
        {
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_GBufferAID, passData.gBufferAHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferB, passData.gBufferBHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_SurfaceMetadata, passData.surfaceMetadataHandle);
        }

        static void RecordSSRTrace(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, in SSRPassData passData)
        {
            BindSSRSurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeBufferParam(shader, kernel, SSRPassUtilityData.SRV_TileListID, passData.tileListHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_TileClassID, passData.tileClassHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HiZClosestTexture, passData.hiZTextureHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_HiZFurthestTextureID, passData.hiZFurthestTextureHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.RankingTileID, passData.rankingTile, TextureView.Whole(passData.rankingTile));
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.ScramblingTileID, passData.scramblingTile, TextureView.Whole(passData.scramblingTile));
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.OwenScrambledSobolID, passData.owenScrambledSobol, TextureView.Whole(passData.owenScrambledSobol));
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_RayHit, passData.rayHitHandle);
            cmdEncoder.SetComputeBufferParam(shader, kernel, SSRPassUtilityData.UAV_SamplesID, passData.samplesHandle);
            TemporalColorSourcePass.BindTemporalColorView(cmdEncoder, shader, kernel, passData.temporalColor);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HitMotionMetadata, passData.motionMetadataHandle);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTextureHandle);
            cmdEncoder.DispatchComputeIndirect(shader, kernel, passData.tileArgsHandle, 0);
        }
    }
}
