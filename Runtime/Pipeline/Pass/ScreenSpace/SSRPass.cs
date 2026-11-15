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

    public partial class InfinityRenderPipeline
    {
        struct SSRPassData
        {
            public RGConstantsHandle parameters;
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
            public RGTextureHandle depthTexture;
            public RGTextureHandle hiZTexture;
            public RGTextureHandle hiZFurthestTexture;
            public RGTextureHandle gBufferA;
            public RGTextureHandle gBufferB;
            public RGTextureHandle gBufferC;
            public TemporalColorView temporalColor;
            public RGTextureHandle motionMetadata;
            public RGTextureHandle motionTexture;
            public RGTextureHandle historyRadiance;
            public RGTextureHandle historyMeta;
            public RGTextureHandle historyVariance;
            public RGBufferHandle tileArgs;
            public RGBufferHandle tileList;
            public RGTextureHandle tileClass;
            public RGTextureHandle rayHit;
            public RGBufferHandle samples;
            public RGTextureHandle resolved;
            public RGTextureHandle hitMean;
            public RGTextureHandle hitDev;
            public RGTextureHandle hitDistance;
            public RGTextureHandle variance;
            public RGTextureHandle temporal;
            public RGTextureHandle meta;
            public RGTextureHandle output;
            public RGTextureHandle filtered;
            public bool runSpatial;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public RGTextureHandle debug;
            public bool runDebug;
            public int debugKernel;
            public int2 debugGroups;
#endif
        }

        void ComputeScreenSpaceReflection(in CameraRecordContext context)
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

            RGTextureHandle rayHit = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRRayHitBuffer,
                // Hit UV and depth are geometric data: half precision can move them into another depth texel.
                SSRPassUtilityData.CreateDescriptor(traceResolution.x, traceResolution.y, SSRPassUtilityData.RayHitName, GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point, settings.halfResolution ? 2 : 1));
            RGTextureHandle resolved = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRResolvedBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.ResolvedName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle hitMean = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitMeanBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRHitMean", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, divisor));
            RGTextureHandle hitDev = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitDevBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRHitDev", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, divisor));
            RGTextureHandle variance = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRVarianceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRVariance", GraphicsFormat.R16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle hitDistance = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitDistanceBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRHitDistance", GraphicsFormat.R32_SFloat, FilterMode.Point, divisor));
            RGTextureHandle temporal = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRTemporalBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.TemporalName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor));
            RGTextureHandle meta = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRMetaBuffer,
                SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, SSRPassUtilityData.MetaName, GraphicsFormat.R32G32_UInt, FilterMode.Point, divisor));
            RGTextureHandle filtered = runSpatial
                ? m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRFilteredBuffer,
                    SSRPassUtilityData.CreateDescriptor(workWidth, workHeight, "SSRFiltered", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, divisor))
                : temporal;
            if (!runSpatial)
            {
                m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SSRFilteredBuffer, filtered);
            }
            RGTextureHandle output = filtered;
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.SSRBuffer, output);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RGTextureHandle debug = default;
            if (runDebug)
            {
                debug = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRDebugBuffer,
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
            RGTextureHandle historyRadiance = m_RGBuilder.ImportTexture(radianceRead.texture);
            RGTextureHandle historyMeta = m_RGBuilder.ImportTexture(metaRead.texture);
            RGTextureHandle historyVariance = m_RGBuilder.ImportTexture(varianceRead.texture);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSRRadianceBuffer, historyRadiance);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSRMetaBuffer, historyMeta);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HistorySSRVarianceBuffer, historyVariance);
            bool historyValid = context.state.acceptedSSR && radianceRead.valid && metaRead.valid && varianceRead.valid;

            InfinityRenderPipelineRuntimeTextures textures = resources.textures;

            using (var passRef = m_RGBuilder.AddComputePass<SSRPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceReflection)))
            {
                ref SSRPassData passData = ref passRef.GetPassData();
                passData.parameters = passRef.WriteConstants(new SSRParameters
                {
                    resolution = new Vector4(resolution.x, resolution.y, 1.0f / resolution.x, 1.0f / resolution.y),
                    traceResolution = new Vector4(traceResolution.x, traceResolution.y, 1.0f / traceResolution.x, 1.0f / traceResolution.y),
                    traceScale = (uint)(settings.halfResolution ? 2 : 1),
                    frameIndex = (uint)Time.frameCount,
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
                passData.ssrShader = shaders.ssrShader;
                passData.clearArgsKernel = kernels.ssrClearArgs.index;
                passData.classifyKernel = kernels.ssrClassify.index;
                passData.traceKernel = kernels.ssrTrace.index;
                passData.resolveKernel = kernels.ssrResolve.index;
                passData.temporalKernel = kernels.ssrTemporal.index;
                passData.bilateralKernel = kernels.ssrBilateral.index;
                passData.resolveGroups = new int2(kernels.ssrResolve.GroupsX(width), kernels.ssrResolve.GroupsY(height));
                passData.rankingTile = textures.blueNoiseRankingTile;
                passData.scramblingTile = textures.blueNoiseScramblingTile;
                passData.owenScrambledSobol = textures.blueNoiseOwenScrambledSobol;

                passData.depthTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.hiZTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZClosestBuffer));
                passData.hiZFurthestTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.HiZFurthestBuffer));
                passData.gBufferA = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA));
                passData.gBufferB = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.gBufferC = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC));
                passData.temporalColor = ReadTemporalColorView(passRef, context.state.temporalColorView);
                passData.motionMetadata = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer));
                passData.motionTexture = passRef.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                passData.historyRadiance = passRef.ReadTexture(historyRadiance);
                passData.historyMeta = passRef.ReadTexture(historyMeta);
                passData.historyVariance = passRef.ReadTexture(historyVariance);

                passData.tileArgs = passRef.CreatePassLocalBuffer(new BufferDescriptor(3, sizeof(uint), GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured) { name = "SSRTileArgs" });
                passData.tileList = passRef.CreatePassLocalBuffer(new BufferDescriptor(maxTiles, sizeof(uint), GraphicsBuffer.Target.Structured) { name = "SSRTileList" });
                TextureDescriptor tileClassDescriptor = SSRPassUtilityData.CreateDescriptor(tileCount.x, tileCount.y, "SSRTileClass", GraphicsFormat.R32_UInt, FilterMode.Point, 8 * (settings.halfResolution ? 2 : 1));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                RenderCaptureSession capture = RenderCaptureService.current;
                bool captureTileClass = capture != null && capture.CaptureThisFrame && capture.request.Matches(context.camera)
                    && capture.Captures(ECaptureBuffer.SSRTileClass);
                // A captured classification must outlive this pass for the graph-owned Transfer readback.
                passData.tileClass = captureTileClass
                    ? passRef.WriteTexture(m_RGBuilder.scoper.CreateAndRegisterTexture(PassShaderIDs.Capture_SSRTileClass, tileClassDescriptor))
                    : passRef.CreatePassLocalTexture(tileClassDescriptor);
#else
                passData.tileClass = passRef.CreatePassLocalTexture(tileClassDescriptor);
#endif
                passData.rayHit = passRef.WriteTexture(rayHit);
                passData.samples = passRef.CreatePassLocalBuffer(new BufferDescriptor(traceResolution.x * traceResolution.y * 4, 32, GraphicsBuffer.Target.Structured) { name = "SSRSamples" });
                passData.resolved = passRef.WriteTexture(resolved);
                passData.hitMean = passRef.WriteTexture(hitMean);
                passData.hitDev = passRef.WriteTexture(hitDev);
                passData.hitDistance = passRef.WriteTexture(hitDistance);
                passData.variance = passRef.WriteTexture(variance);
                passData.temporal = passRef.WriteTexture(temporal);
                passData.meta = passRef.WriteTexture(meta);
                passData.runSpatial = runSpatial;
                passData.filtered = runSpatial ? passRef.WriteTexture(filtered) : passData.temporal;
                passData.output = passData.filtered;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                passData.runDebug = runDebug;
                passData.debug = runDebug ? passRef.WriteTexture(debug) : default;
                passData.debugKernel = kernels.ssrDebug.index;
                passData.debugGroups = new int2(kernels.ssrDebug.GroupsX(width), kernels.ssrDebug.GroupsY(height));
#endif

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in SSRPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.ssrShader;
                    int2 tileCount = passData.tileCount;
                    cmdEncoder.SetComputeConstantBufferParam(shader, SSRPassUtilityData.CBV_SSRParametersID, passData.parameters);

                    cmdEncoder.BeginSample(SSRPassUtilityData.SSRClassifySampler);
                    int clearArgs = passData.clearArgsKernel;
                    cmdEncoder.SetComputeBufferParam(shader, clearArgs, PassShaderIDs.UAV_TileArgs, passData.tileArgs);
                    cmdEncoder.DispatchCompute(shader, clearArgs, 1, 1, 1);
                    int classify = passData.classifyKernel;
                    BindSSRSurface(cmdEncoder, shader, classify, passData);
                    cmdEncoder.SetComputeBufferParam(shader, classify, PassShaderIDs.UAV_TileArgs, passData.tileArgs);
                    cmdEncoder.SetComputeBufferParam(shader, classify, SSRPassUtilityData.UAV_TileListID, passData.tileList);
                    cmdEncoder.SetComputeTextureParam(shader, classify, SSRPassUtilityData.UAV_TileClassID, passData.tileClass);
                    cmdEncoder.SetComputeTextureParam(shader, classify, PassShaderIDs.UAV_RayHit, passData.rayHit);
                    cmdEncoder.SetComputeBufferParam(shader, classify, SSRPassUtilityData.UAV_SamplesID, passData.samples);
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
                    cmdEncoder.SetComputeBufferParam(shader, resolve, SSRPassUtilityData.SRV_SamplesID, passData.samples);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_ResolvedID, passData.resolved);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_HitMeanID, passData.hitMean);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_HitDevID, passData.hitDev);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, PassShaderIDs.UAV_HitDistance, passData.hitDistance);
                    cmdEncoder.DispatchCompute(shader, resolve, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSRPassUtilityData.SSRResolveSampler);

                    // Temporal
                    cmdEncoder.BeginSample(SSRPassUtilityData.SSRTemporalSampler);
                    int temporalKernel = passData.temporalKernel;
                    BindSSRSurface(cmdEncoder, shader, temporalKernel, passData);
                    cmdEncoder.SetComputeTextureParam(shader,temporalKernel,PassShaderIDs.SRV_HitMotionMetadata,passData.motionMetadata);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_ResolvedID, passData.resolved);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HitMeanID, passData.hitMean);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HitDevID, passData.hitDev);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HitDistanceID, passData.hitDistance);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HistoryVarianceID, passData.historyVariance);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.UAV_VarianceID, passData.variance);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.SRV_HistoryRadiance, passData.historyRadiance);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.SRV_HistoryMeta, passData.historyMeta);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.UAV_Temporal, passData.temporal);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, PassShaderIDs.UAV_Meta, passData.meta);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample(SSRPassUtilityData.SSRTemporalSampler);

                    if (passData.runSpatial)
                    {
                        cmdEncoder.BeginSample(SSRPassUtilityData.SSRBilateralSampler);
                        int bilateral = passData.bilateralKernel;
                        BindSSRSurface(cmdEncoder, shader, bilateral, passData);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, PassShaderIDs.SRV_Temporal, passData.temporal);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, SSRPassUtilityData.SRV_VarianceID, passData.variance);
                        cmdEncoder.SetComputeTextureParam(shader, bilateral, PassShaderIDs.UAV_Output, passData.filtered);
                        cmdEncoder.DispatchCompute(shader, bilateral, groupsX, groupsY, 1);
                        cmdEncoder.EndSample(SSRPassUtilityData.SSRBilateralSampler);
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (passData.runDebug)
                    {
                        cmdEncoder.BeginSample(SSRPassUtilityData.SSRDebugSampler);
                        int debugKernel = passData.debugKernel;
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_TileClassID, passData.tileClass);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_RayHitID, passData.rayHit);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_ResolvedID, passData.resolved);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, PassShaderIDs.SRV_Meta, passData.meta);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.UAV_DebugID, passData.debug);
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
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_GBufferAID, passData.gBufferA);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferB, passData.gBufferB);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferC, passData.gBufferC);
        }

        static void RecordSSRTrace(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, in SSRPassData passData)
        {
            BindSSRSurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeBufferParam(shader, kernel, SSRPassUtilityData.SRV_TileListID, passData.tileList);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_TileClassID, passData.tileClass);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HiZClosestTexture, passData.hiZTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_HiZFurthestTextureID, passData.hiZFurthestTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.RankingTileID, passData.rankingTile, TextureView.Whole(passData.rankingTile));
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.ScramblingTileID, passData.scramblingTile, TextureView.Whole(passData.scramblingTile));
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.OwenScrambledSobolID, passData.owenScrambledSobol, TextureView.Whole(passData.owenScrambledSobol));
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.UAV_RayHit, passData.rayHit);
            cmdEncoder.SetComputeBufferParam(shader, kernel, SSRPassUtilityData.UAV_SamplesID, passData.samples);
            BindTemporalColorView(cmdEncoder, shader, kernel, passData.temporalColor);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_HitMotionMetadata, passData.motionMetadata);
            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
            cmdEncoder.DispatchComputeIndirect(shader, kernel, passData.tileArgs, 0);
        }
    }
}
