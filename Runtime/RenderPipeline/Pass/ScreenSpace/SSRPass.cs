using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    // Stochastic SSR (Frostbite): classify -> trace (Hi-Z or linear, indirect per tile class) -> resolve with ray reuse
    // -> temporal -> optional spatial. Kernel contracts are documented at the top of Compute_ScreenSpaceReflection.
    internal static class SSRPassUtilityData
    {
        internal const string ClassifyKernelName = "SSRClassify";
        internal const string ClearSkippedKernelName = "SSRClearSkipped";
        internal const string TraceHiZKernelName = "SSRTraceHiZ";
        internal const string TraceLinearKernelName = "SSRTraceLinear";
        internal const string ResolveKernelName = "SSRResolve";
        internal const string TemporalKernelName = "SSRTemporal";
        internal const string SpatialKernelName = "SSRSpatial";
        internal const string DebugKernelName = "SSRDebug";

        internal static readonly string[] RequiredKernels =
        {
            ClassifyKernelName, ClearSkippedKernelName, TraceHiZKernelName, TraceLinearKernelName,
            ResolveKernelName, TemporalKernelName, SpatialKernelName, DebugKernelName
        };

        // Tile classes; also the order of the argument triplets and of the tile list segments.
        internal const int ClassHiZ = 0;
        internal const int ClassLinear = 1;
        internal const int ClassSkip = 2;
        internal const int ClassCount = 3;

        internal static readonly int SSR_ResolutionID = Shader.PropertyToID("SSR_Resolution");
        internal static readonly int SSR_TraceResolutionID = Shader.PropertyToID("SSR_TraceResolution");
        internal static readonly int SSR_TraceScaleID = Shader.PropertyToID("SSR_TraceScale");
        internal static readonly int SSR_FrameIndexID = Shader.PropertyToID("SSR_FrameIndex");
        internal static readonly int SSR_MaxTilesID = Shader.PropertyToID("SSR_MaxTiles");
        internal static readonly int SSR_TileListOffsetID = Shader.PropertyToID("SSR_TileListOffset");
        internal static readonly int SSR_MaxDistanceID = Shader.PropertyToID("SSR_MaxDistance");
        internal static readonly int SSR_ThicknessID = Shader.PropertyToID("SSR_Thickness");
        internal static readonly int SSR_MaxRoughnessID = Shader.PropertyToID("SSR_MaxRoughness");
        internal static readonly int SSR_EdgeFadeID = Shader.PropertyToID("SSR_EdgeFade");
        internal static readonly int SSR_MirrorRoughnessID = Shader.PropertyToID("SSR_MirrorRoughness");
        internal static readonly int SSR_LinearRoughnessID = Shader.PropertyToID("SSR_LinearRoughness");
        internal static readonly int SSR_BRDFBiasID = Shader.PropertyToID("SSR_BRDFBias");
        internal static readonly int SSR_HiZStepsID = Shader.PropertyToID("SSR_HiZSteps");
        internal static readonly int SSR_LinearStepsID = Shader.PropertyToID("SSR_LinearSteps");
        internal static readonly int SSR_ResolveSamplesID = Shader.PropertyToID("SSR_ResolveSamples");
        internal static readonly int SSR_ResolveRadiusID = Shader.PropertyToID("SSR_ResolveRadius");
        internal static readonly int SSR_TemporalWeightID = Shader.PropertyToID("SSR_TemporalWeight");
        internal static readonly int SSR_TemporalClampScaleID = Shader.PropertyToID("SSR_TemporalClampScale");
        internal static readonly int SSR_HistoryValidID = Shader.PropertyToID("SSR_HistoryValid");
        internal static readonly int SSR_DebugModeID = Shader.PropertyToID("SSR_DebugMode");
        internal static readonly int SSR_CameraPositionID = Shader.PropertyToID("SSR_CameraPosition");
        internal static readonly int SSR_CameraForwardID = Shader.PropertyToID("SSR_CameraForward");
        internal static readonly int ScreenSpaceDepthParamsID = Shader.PropertyToID("ScreenSpaceDepthParams");
        internal static readonly int Matrix_ProjID = Shader.PropertyToID("Matrix_Proj");
        internal static readonly int Matrix_InvProjID = Shader.PropertyToID("Matrix_InvProj");
        internal static readonly int Matrix_InvViewProjID = Shader.PropertyToID("Matrix_InvViewProj");
        internal static readonly int Matrix_WorldToViewID = Shader.PropertyToID("Matrix_WorldToView");
        internal static readonly int Matrix_HistoryViewProjID = Shader.PropertyToID("Matrix_HistoryViewProj");
        internal static readonly int Matrix_MotionViewProjID = Shader.PropertyToID("Matrix_MotionViewProj");
        internal static readonly int Matrix_LastMotionViewProjID = Shader.PropertyToID("Matrix_LastMotionViewProj");
        internal static readonly int RankingTileID = Shader.PropertyToID("_Ranking_Tile");
        internal static readonly int ScramblingTileID = Shader.PropertyToID("_Scrambled_Tile");
        internal static readonly int OwenScrambledSobolID = Shader.PropertyToID("_ScrambledOwen_Noise");

        internal static readonly int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static readonly int SRV_HiZTextureID = Shader.PropertyToID("SRV_HiZTexture");
        internal static readonly int SRV_GBufferAID = Shader.PropertyToID("SRV_GBufferA");
        internal static readonly int SRV_GBufferBID = Shader.PropertyToID("SRV_GBufferB");
        internal static readonly int SRV_GBufferCID = Shader.PropertyToID("SRV_GBufferC");
        internal static readonly int SRV_LightingPyramidID = Shader.PropertyToID("SRV_LightingPyramid");
        internal static readonly int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static readonly int SRV_HistoryRadianceID = Shader.PropertyToID("SRV_HistoryRadiance");
        internal static readonly int SRV_HistoryMetaID = Shader.PropertyToID("SRV_HistoryMeta");
        internal static readonly int SRV_RayHitID = Shader.PropertyToID("SRV_RayHit");
        internal static readonly int SRV_ResolvedID = Shader.PropertyToID("SRV_Resolved");
        internal static readonly int SRV_HitDistanceID = Shader.PropertyToID("SRV_HitDistance");
        internal static readonly int SRV_TemporalID = Shader.PropertyToID("SRV_Temporal");
        internal static readonly int SRV_MetaID = Shader.PropertyToID("SRV_Meta");
        internal static readonly int SRV_TileClassID = Shader.PropertyToID("SRV_TileClass");
        internal static readonly int SRV_TileListID = Shader.PropertyToID("SRV_TileList");
        internal static readonly int UAV_TileArgsID = Shader.PropertyToID("UAV_TileArgs");
        internal static readonly int UAV_TileListID = Shader.PropertyToID("UAV_TileList");
        internal static readonly int UAV_TileClassID = Shader.PropertyToID("UAV_TileClass");
        internal static readonly int UAV_RayHitID = Shader.PropertyToID("UAV_RayHit");
        internal static readonly int UAV_ResolvedID = Shader.PropertyToID("UAV_Resolved");
        internal static readonly int UAV_HitDistanceID = Shader.PropertyToID("UAV_HitDistance");
        internal static readonly int UAV_TemporalID = Shader.PropertyToID("UAV_Temporal");
        internal static readonly int UAV_MetaID = Shader.PropertyToID("UAV_Meta");
        internal static readonly int UAV_OutputID = Shader.PropertyToID("UAV_Output");
        internal static readonly int UAV_DebugID = Shader.PropertyToID("UAV_Debug");

        internal const string RayHitName = "SSRRayHit";
        internal const string ResolvedName = "SSRResolved";
        internal const string HitDistanceName = "SSRHitDistance";
        internal const string TemporalName = "SSRTemporal";
        internal const string MetaName = "SSRMeta";
        internal const string OutputName = "SSRTexture";
        internal const string DebugName = "SSRDebug";
        internal const string HistoryRadianceName = "HistorySSRRadiance";
        internal const string HistoryMetaName = "HistorySSRMeta";

        internal static bool IsDebugView(EDebugView view) =>
            view == EDebugView.SSRTileClass || view == EDebugView.SSRRayHit || view == EDebugView.SSRConfidence || view == EDebugView.SSRHistoryAge;

        internal static TextureDescriptor CreateDescriptor(int width, int height, string name, GraphicsFormat format, FilterMode filter)
        {
            TextureDescriptor descriptor = ScreenSpaceHistoryUtility.CreateFilterDescriptor(width, height, name, format, true);
            descriptor.filterMode = filter;
            return descriptor;
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct SSRPassData
        {
            public FSSRSettings settings;
            public int2 resolution;
            public int2 traceResolution;
            public int2 tileCount;
            public int traceScale;
            public int frameIndex;
            public float historyValid;
            public int debugMode;
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
            public ComputeShader ssrShader;
            public ComputeShader utilityShader;
            public Texture2D rankingTile;
            public Texture2D scramblingTile;
            public Texture2D owenScrambledSobol;
            public RGTextureRef depthTexture;
            public RGTextureRef hiZTexture;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef lightingPyramid;
            public RGTextureRef motionTexture;
            public RGTextureRef historyRadiance;
            public RGTextureRef historyMeta;
            public RGBufferRef tileArgs;
            public RGBufferRef tileList;
            public RGTextureRef tileClass;
            public RGTextureRef rayHit;
            public RGTextureRef resolved;
            public RGTextureRef hitDistance;
            public RGTextureRef temporal;
            public RGTextureRef meta;
            public RGTextureRef output;
            public RGTextureRef debug;
            public bool runSpatial;
            public bool runDebug;
        }

        void ComputeScreenSpaceReflection(RenderContext renderContext, Camera camera, HistoryCache historyCache)
        {
            if (!ShouldRecordFeature(EFrameFeature.SSR))
            {
                return;
            }

            FSSRSettings settings = ActiveVolumeStack.GetComponent<ScreenSpaceReflection>().Resolve();
            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            int2 resolution = new int2(width, height);
            int2 traceResolution = ScreenSpaceTraceUtility.TraceSize(resolution, settings.halfResolution);
            int2 tileCount = ScreenSpaceTraceUtility.TileCount(traceResolution);
            int maxTiles = tileCount.x * tileCount.y;
            EDebugView debugView = m_ActiveFrameState.debugView;
            bool runDebug = SSRPassUtilityData.IsDebugView(debugView);
            bool runSpatial = settings.spatialPasses > 0;

            RGTextureRef rayHit = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRRayHitBuffer,
                // Hit UV and depth are geometric data: half precision can move them into another depth texel.
                SSRPassUtilityData.CreateDescriptor(traceResolution.x, traceResolution.y, SSRPassUtilityData.RayHitName, GraphicsFormat.R32G32B32A32_SFloat, FilterMode.Point));
            RGTextureRef resolved = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRResolvedBuffer,
                SSRPassUtilityData.CreateDescriptor(width, height, SSRPassUtilityData.ResolvedName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear));
            RGTextureRef hitDistance = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRHitDistanceBuffer,
                SSRPassUtilityData.CreateDescriptor(width, height, SSRPassUtilityData.HitDistanceName, GraphicsFormat.R16_SFloat, FilterMode.Point));
            RGTextureRef temporal = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRTemporalBuffer,
                SSRPassUtilityData.CreateDescriptor(width, height, SSRPassUtilityData.TemporalName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear));
            RGTextureRef meta = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRMetaBuffer,
                SSRPassUtilityData.CreateDescriptor(width, height, SSRPassUtilityData.MetaName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point));
            RGTextureRef output;
            if (runSpatial)
            {
                output = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRBuffer,
                    SSRPassUtilityData.CreateDescriptor(width, height, SSRPassUtilityData.OutputName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear));
            }
            else
            {
                m_RGScoper.RegisterTexture(InfinityShaderIDs.SSRBuffer, temporal);
                output = temporal;
            }
            RGTextureRef debug = default;
            if (runDebug)
            {
                debug = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SSRDebugBuffer,
                    SSRPassUtilityData.CreateDescriptor(width, height, SSRPassUtilityData.DebugName, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point));
            }

            TextureDescriptor historyRadianceDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSRPassUtilityData.HistoryRadianceName, GraphicsFormat.R16G16B16A16_SFloat);
            TextureDescriptor historyMetaDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, SSRPassUtilityData.HistoryMetaName, GraphicsFormat.R16G16B16A16_SFloat);
            historyMetaDsc.filterMode = FilterMode.Point;
            RGTextureRef historyRadiance = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSRRadianceBuffer, historyRadianceDsc, out bool historyRadianceCreated));
            RGTextureRef historyMeta = m_RGBuilder.ImportTexture(historyCache.GetTexture(InfinityShaderIDs.HistorySSRMetaBuffer, historyMetaDsc, out bool historyMetaCreated));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSRRadianceBuffer, historyRadiance);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HistorySSRMetaBuffer, historyMeta);
            bool historyValid = !(m_CameraUniform.historyReset || historyRadianceCreated || historyMetaCreated);

            InfinityRenderPipelineRuntimeTextures textures = resources.textures;

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SSRPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeScreenSpaceReflection)))
            {
                ref SSRPassData passData = ref passRef.GetPassData<SSRPassData>();
                passData.settings = settings;
                passData.resolution = resolution;
                passData.traceResolution = traceResolution;
                passData.tileCount = tileCount;
                passData.traceScale = settings.halfResolution ? 2 : 1;
                passData.frameIndex = Time.frameCount;
                passData.historyValid = historyValid ? 1 : 0;
                passData.debugMode = (int)debugView;
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
                passData.ssrShader = shaders.ssrShader;
                passData.utilityShader = shaders.utilityShader;
                passData.rankingTile = textures.blueNoiseRankingTile;
                passData.scramblingTile = textures.blueNoiseScramblingTile;
                passData.owenScrambledSobol = textures.blueNoiseOwenScrambledSobol;

                passData.depthTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.hiZTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.HiZBuffer));
                passData.gBufferA = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA));
                passData.gBufferB = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.gBufferC = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC));
                passData.lightingPyramid = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.OpaqueLightingPyramidBuffer));
                passData.motionTexture = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer));
                passData.historyRadiance = passRef.ReadTexture(historyRadiance);
                passData.historyMeta = passRef.ReadTexture(historyMeta);

                passData.tileArgs = passRef.CreateTemporaryBuffer(new BufferDescriptor(SSRPassUtilityData.ClassCount * 3, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "SSRTileArgs" });
                passData.tileList = passRef.CreateTemporaryBuffer(new BufferDescriptor(SSRPassUtilityData.ClassCount * maxTiles, sizeof(uint), ComputeBufferType.Structured) { name = "SSRTileList" });
                TextureDescriptor tileClassDescriptor = SSRPassUtilityData.CreateDescriptor(tileCount.x, tileCount.y, "SSRTileClass", GraphicsFormat.R32_UInt, FilterMode.Point);
                RenderCaptureSession capture = RenderCaptureService.current;
                bool captureTileClass = capture != null && capture.CaptureThisFrame && capture.request.Matches(camera)
                    && System.Array.IndexOf(capture.request.buffers, "SSRTileClass") >= 0;
                // A captured classification must outlive this pass for the graph-owned Transfer readback.
                passData.tileClass = captureTileClass
                    ? passRef.WriteTexture(m_RGScoper.CreateAndRegisterTexture(Shader.PropertyToID("Capture_SSRTileClass"), tileClassDescriptor))
                    : passRef.CreateTemporaryTexture(tileClassDescriptor);
                passData.rayHit = passRef.WriteTexture(rayHit);
                passData.resolved = passRef.WriteTexture(resolved);
                passData.hitDistance = passRef.WriteTexture(hitDistance);
                passData.temporal = passRef.WriteTexture(temporal);
                passData.meta = passRef.WriteTexture(meta);
                passData.runSpatial = runSpatial;
                passData.output = runSpatial ? passRef.WriteTexture(output) : passData.temporal;
                passData.runDebug = runDebug;
                passData.debug = runDebug ? passRef.WriteTexture(debug) : default;

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in SSRPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.ssrShader;
                    FSSRSettings settings = passData.settings;
                    int2 resolution = passData.resolution;
                    int2 traceResolution = passData.traceResolution;
                    int2 tileCount = passData.tileCount;
                    int maxTiles = tileCount.x * tileCount.y;

                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.SSR_ResolutionID, new Vector4(resolution.x, resolution.y, 1.0f / resolution.x, 1.0f / resolution.y));
                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.SSR_TraceResolutionID, new Vector4(traceResolution.x, traceResolution.y, 1.0f / traceResolution.x, 1.0f / traceResolution.y));
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_TraceScaleID, passData.traceScale);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_FrameIndexID, passData.frameIndex);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_MaxTilesID, maxTiles);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_MaxDistanceID, settings.maxDistance);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_ThicknessID, settings.thickness);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_MaxRoughnessID, settings.maxRoughness);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_EdgeFadeID, settings.edgeFade);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_MirrorRoughnessID, settings.mirrorRoughness);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_LinearRoughnessID, settings.linearRoughness);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_BRDFBiasID, settings.brdfBias);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_HiZStepsID, math.max(1, settings.hiZSteps));
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_LinearStepsID, math.max(1, settings.linearSteps));
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_ResolveSamplesID, math.max(1, settings.resolveSamples));
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_ResolveRadiusID, settings.resolveRadius);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_TemporalWeightID, settings.temporalWeight);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_TemporalClampScaleID, settings.temporalClampScale);
                    cmdEncoder.SetComputeFloatParam(shader, SSRPassUtilityData.SSR_HistoryValidID, passData.historyValid);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_DebugModeID, passData.debugMode);
                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.SSR_CameraPositionID, passData.cameraPosition);
                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.SSR_CameraForwardID, passData.cameraForward);
                    cmdEncoder.SetComputeVectorParam(shader, SSRPassUtilityData.ScreenSpaceDepthParamsID, passData.depthParameters);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_ProjID, passData.matrix_Proj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_InvProjID, passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_InvViewProjID, passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_WorldToViewID, passData.matrix_WorldToView);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_HistoryViewProjID, passData.matrix_HistoryViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_MotionViewProjID, passData.matrix_MotionViewProj);
                    cmdEncoder.SetComputeMatrixParam(shader, SSRPassUtilityData.Matrix_LastMotionViewProjID, passData.matrix_LastMotionViewProj);

                    // Classify
                    cmdEncoder.BeginSample("SSR_Classify");
                    ComputeUtility.ClearIndirectDispatchArgs(cmdEncoder, passData.utilityShader, passData.tileArgs, SSRPassUtilityData.ClassCount);
                    int classify = shader.FindKernel(SSRPassUtilityData.ClassifyKernelName);
                    BindSSRSurface(cmdEncoder, shader, classify, passData);
                    cmdEncoder.SetComputeBufferParam(shader, classify, SSRPassUtilityData.UAV_TileArgsID, passData.tileArgs);
                    cmdEncoder.SetComputeBufferParam(shader, classify, SSRPassUtilityData.UAV_TileListID, passData.tileList);
                    cmdEncoder.SetComputeTextureParam(shader, classify, SSRPassUtilityData.UAV_TileClassID, passData.tileClass);
                    cmdEncoder.DispatchCompute(shader, classify, tileCount.x, tileCount.y, 1);
                    cmdEncoder.EndSample("SSR_Classify");

                    // Trace: one indirect dispatch per tile class, one group per tile.
                    cmdEncoder.BeginSample("SSR_Trace");
                    int clearSkipped = shader.FindKernel(SSRPassUtilityData.ClearSkippedKernelName);
                    cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_TileListOffsetID, SSRPassUtilityData.ClassSkip * maxTiles);
                    cmdEncoder.SetComputeBufferParam(shader, clearSkipped, SSRPassUtilityData.SRV_TileListID, passData.tileList);
                    cmdEncoder.SetComputeTextureParam(shader, clearSkipped, SSRPassUtilityData.UAV_RayHitID, passData.rayHit);
                    cmdEncoder.DispatchComputeIndirect(shader, clearSkipped, passData.tileArgs, (uint)(SSRPassUtilityData.ClassSkip * ComputeUtility.IndirectDispatchArgsStride));
                    RecordSSRTrace(cmdEncoder, shader, SSRPassUtilityData.TraceHiZKernelName, SSRPassUtilityData.ClassHiZ, maxTiles, passData);
                    RecordSSRTrace(cmdEncoder, shader, SSRPassUtilityData.TraceLinearKernelName, SSRPassUtilityData.ClassLinear, maxTiles, passData);
                    cmdEncoder.EndSample("SSR_Trace");

                    int groupsX = (resolution.x + ScreenSpaceTraceUtility.ResolveGroupSize - 1) / ScreenSpaceTraceUtility.ResolveGroupSize;
                    int groupsY = (resolution.y + ScreenSpaceTraceUtility.ResolveGroupSize - 1) / ScreenSpaceTraceUtility.ResolveGroupSize;

                    // Resolve
                    cmdEncoder.BeginSample("SSR_Resolve");
                    int resolve = shader.FindKernel(SSRPassUtilityData.ResolveKernelName);
                    BindSSRSurface(cmdEncoder, shader, resolve, passData);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.SRV_RayHitID, passData.rayHit);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.SRV_LightingPyramidID, passData.lightingPyramid);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_ResolvedID, passData.resolved);
                    cmdEncoder.SetComputeTextureParam(shader, resolve, SSRPassUtilityData.UAV_HitDistanceID, passData.hitDistance);
                    cmdEncoder.DispatchCompute(shader, resolve, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSR_Resolve");

                    // Temporal
                    cmdEncoder.BeginSample("SSR_Temporal");
                    int temporalKernel = shader.FindKernel(SSRPassUtilityData.TemporalKernelName);
                    BindSSRSurface(cmdEncoder, shader, temporalKernel, passData);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_ResolvedID, passData.resolved);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HitDistanceID, passData.hitDistance);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HistoryRadianceID, passData.historyRadiance);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.SRV_HistoryMetaID, passData.historyMeta);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.UAV_TemporalID, passData.temporal);
                    cmdEncoder.SetComputeTextureParam(shader, temporalKernel, SSRPassUtilityData.UAV_MetaID, passData.meta);
                    cmdEncoder.DispatchCompute(shader, temporalKernel, groupsX, groupsY, 1);
                    cmdEncoder.EndSample("SSR_Temporal");

                    // Spatial
                    if (passData.runSpatial)
                    {
                        cmdEncoder.BeginSample("SSR_Spatial");
                        int spatial = shader.FindKernel(SSRPassUtilityData.SpatialKernelName);
                        BindSSRSurface(cmdEncoder, shader, spatial, passData);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.SRV_TemporalID, passData.temporal);
                        cmdEncoder.SetComputeTextureParam(shader, spatial, SSRPassUtilityData.UAV_OutputID, passData.output);
                        cmdEncoder.DispatchCompute(shader, spatial, groupsX, groupsY, 1);
                        cmdEncoder.EndSample("SSR_Spatial");
                    }

                    if (passData.runDebug)
                    {
                        int debugKernel = shader.FindKernel(SSRPassUtilityData.DebugKernelName);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_TileClassID, passData.tileClass);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_RayHitID, passData.rayHit);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_ResolvedID, passData.resolved);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.SRV_MetaID, passData.meta);
                        cmdEncoder.SetComputeTextureParam(shader, debugKernel, SSRPassUtilityData.UAV_DebugID, passData.debug);
                        cmdEncoder.DispatchCompute(shader, debugKernel, groupsX, groupsY, 1);
                    }
                });
            }

            MarkFeatureProduced(EFrameFeature.SSR);
        }

        // Depth and GBuffer inputs shared by every kernel that tests or reconstructs the surface.
        static void BindSSRSurface(in RGComputeEncoder cmdEncoder, ComputeShader shader, int kernel, in SSRPassData passData)
        {
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_GBufferAID, passData.gBufferA);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_GBufferBID, passData.gBufferB);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_GBufferCID, passData.gBufferC);
        }

        static void RecordSSRTrace(in RGComputeEncoder cmdEncoder, ComputeShader shader, string kernelName, int tileClass, int maxTiles, in SSRPassData passData)
        {
            int kernel = shader.FindKernel(kernelName);
            BindSSRSurface(cmdEncoder, shader, kernel, passData);
            cmdEncoder.SetComputeIntParam(shader, SSRPassUtilityData.SSR_TileListOffsetID, tileClass * maxTiles);
            cmdEncoder.SetComputeBufferParam(shader, kernel, SSRPassUtilityData.SRV_TileListID, passData.tileList);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.SRV_HiZTextureID, passData.hiZTexture);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.RankingTileID, passData.rankingTile);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.ScramblingTileID, passData.scramblingTile);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.OwenScrambledSobolID, passData.owenScrambledSobol);
            cmdEncoder.SetComputeTextureParam(shader, kernel, SSRPassUtilityData.UAV_RayHitID, passData.rayHit);
            cmdEncoder.DispatchComputeIndirect(shader, kernel, passData.tileArgs, (uint)(tileClass * ComputeUtility.IndirectDispatchArgsStride));
        }
    }
}
