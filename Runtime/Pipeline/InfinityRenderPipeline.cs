using System;
using UnityEngine;
using UnityEngine.VFX;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Component;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.Volume;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.TerrainPipeline;





#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    internal enum EPipelineProfileId
    {
        SetupCamera,
        CulllingScene,
        ProcessLOD,
        ProcessLight,
        FrameRendering,
        ProxyUpdate,
        RecordRG,
        ExecuteRG
    }

    internal class CameraUniform
    {
        internal static readonly int ID_FrameIndex = Shader.PropertyToID("FrameIndex");
        internal static readonly int ID_TAAJitter = Shader.PropertyToID("TAAJitter");
        internal static readonly int ID_Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");
        internal static readonly int ID_Matrix_ViewToWorld = Shader.PropertyToID("Matrix_ViewToWorld");
        internal static readonly int ID_Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        internal static readonly int ID_Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        internal static readonly int ID_Matrix_JitterProj = Shader.PropertyToID("Matrix_JitterProj");
        internal static readonly int ID_Matrix_InvJitterProj = Shader.PropertyToID("Matrix_InvJitterProj");
        internal static readonly int ID_Matrix_FlipYProj = Shader.PropertyToID("Matrix_FlipYProj");
        internal static readonly int ID_Matrix_InvFlipYProj = Shader.PropertyToID("Matrix_InvFlipYProj");
        internal static readonly int ID_Matrix_FlipYJitterProj = Shader.PropertyToID("Matrix_FlipYJitterProj");
        internal static readonly int ID_Matrix_InvFlipYJitterProj = Shader.PropertyToID("Matrix_InvFlipYJitterProj");
        internal static readonly int ID_Matrix_ViewProj = Shader.PropertyToID("Matrix_ViewProj");
        internal static readonly int ID_Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        internal static readonly int ID_Matrix_ViewFlipYProj = Shader.PropertyToID("Matrix_ViewFlipYProj");
        internal static readonly int ID_Matrix_InvViewFlipYProj = Shader.PropertyToID("Matrix_InvViewFlipYProj");
        internal static readonly int ID_Matrix_ViewJitterProj = Shader.PropertyToID("Matrix_ViewJitterProj");
        internal static readonly int ID_Matrix_InvViewJitterProj = Shader.PropertyToID("Matrix_InvViewJitterProj");
        internal static readonly int ID_Matrix_ViewFlipYJitterProj = Shader.PropertyToID("Matrix_ViewFlipYJitterProj");
        internal static readonly int ID_Matrix_InvViewFlipYJitterProj = Shader.PropertyToID("Matrix_InvViewFlipYJitterProj");
        internal static readonly int ID_LastFrameIndex = Shader.PropertyToID("Prev_FrameIndex");
        internal static readonly int ID_Matrix_LastViewProj = Shader.PropertyToID("Matrix_LastViewProj");
        internal static readonly int ID_Matrix_LastViewFlipYProj = Shader.PropertyToID("Matrix_LastViewFlipYProj");
        internal static readonly int ID_Matrix_LastViewJitterProj = Shader.PropertyToID("Matrix_LastViewJitterProj");
        internal static readonly int ID_Matrix_LastViewFlipYJitterProj = Shader.PropertyToID("Matrix_LastViewFlipYJitterProj");

        public int frameIndex;
        public int lastFrameIndex;
        public float2 jitter;
        public float2 lastJitter;
        public Matrix4x4 matrix_WorldToView;
        public Matrix4x4 matrix_ViewToWorld;
        public Matrix4x4 matrix_Proj;
        public Matrix4x4 matrix_InvProj;
        public Matrix4x4 matrix_JitterProj;
        public Matrix4x4 matrix_InvJitterProj;
        public Matrix4x4 matrix_FlipYProj;
        public Matrix4x4 matrix_InvFlipYProj;
        public Matrix4x4 matrix_FlipYJitterProj;
        public Matrix4x4 matrix_InvFlipYJitterProj;
        public Matrix4x4 matrix_ViewProj;
        public Matrix4x4 matrix_InvViewProj;
        public Matrix4x4 matrix_ViewFlipYProj;
        public Matrix4x4 matrix_InvViewFlipYProj;
        public Matrix4x4 matrix_ViewJitterProj;
        public Matrix4x4 matrix_InvViewJitterProj;
        public Matrix4x4 matrix_ViewFlipYJitterProj;
        public Matrix4x4 matrix_InvViewFlipYJitterProj;
        public Matrix4x4 matrix_LastViewProj;
        public Matrix4x4 matrix_LastViewFlipYProj;
        public Matrix4x4 matrix_LastViewJitterProj;
        public Matrix4x4 matrix_LastViewFlipYJitterProj;
        public Vector2 previousJitterUV;
        private Vector3 m_PreparedPosition;
        private Quaternion m_PreparedRotation;
        private Matrix4x4 m_PreparedProjection, m_LastProjection;
        private int m_CommittedFrames;
        private bool m_HasPreparedView;

        private bool m_HasCommittedView;
        private Vector3 m_LastCameraPosition;
        private Quaternion m_LastCameraRotation;
        private const float HistoryCutPosition = 2.0f;
        private const float HistoryCutAngle = 20.0f;

        internal bool hasCommittedView => m_HasCommittedView;

        // A jump beyond the cut thresholds or a projection change since the last committed frame.
        internal bool IsCut(Camera camera)
        {
            return Vector3.Distance(camera.transform.position, m_LastCameraPosition) > HistoryCutPosition
                || Quaternion.Angle(camera.transform.rotation, m_LastCameraRotation) > HistoryCutAngle
                || camera.projectionMatrix != m_LastProjection;
        }

        internal void UpdateCurrFrameData(Camera camera, int2 renderSize, EHistoryReset historyReset)
        {
            bool resetHistory = historyReset != EHistoryReset.None;
            m_HasPreparedView = true;
            m_PreparedPosition = camera.transform.position;
            m_PreparedRotation = camera.transform.rotation;
            m_PreparedProjection = camera.projectionMatrix;
            frameIndex = m_CommittedFrames % 8;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (historyReset == EHistoryReset.Locked) frameIndex = 0;
#endif
            matrix_WorldToView = camera.worldToCameraMatrix;
            matrix_ViewToWorld = matrix_WorldToView.inverse;
            matrix_Proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            matrix_FlipYProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            // A non-temporal camera always resets, so it never jitters.
            TemporalJitter.CalculateProjectionMatrix(camera, renderSize, 0.75f, frameIndex, ref jitter, ref matrix_JitterProj, ref matrix_FlipYJitterProj, applyJitter: !resetHistory);
            matrix_InvProj = matrix_Proj.inverse;
            matrix_InvJitterProj = matrix_JitterProj.inverse;
            matrix_InvFlipYProj = matrix_FlipYProj.inverse;
            matrix_InvFlipYJitterProj = matrix_FlipYJitterProj.inverse;
            matrix_ViewProj = matrix_Proj * matrix_WorldToView;
            matrix_InvViewProj = matrix_ViewProj.inverse;
            matrix_ViewFlipYProj = matrix_FlipYProj * matrix_WorldToView;
            matrix_InvViewFlipYProj = matrix_ViewFlipYProj.inverse;
            matrix_ViewJitterProj = matrix_JitterProj * matrix_WorldToView;
            matrix_InvViewJitterProj = matrix_ViewJitterProj.inverse;
            matrix_ViewFlipYJitterProj = matrix_FlipYJitterProj * matrix_WorldToView;
            matrix_InvViewFlipYJitterProj = matrix_ViewFlipYJitterProj.inverse;
            if (resetHistory)
            {
                // No previous camera sample exists after a reset. Seed the upload without
                // committing last-camera state; successful frame completion still owns that.
                lastJitter = jitter; lastFrameIndex = frameIndex;
                previousJitterUV = TemporalJitter.ProjectionJitterUV(matrix_FlipYJitterProj, matrix_FlipYProj);
                matrix_LastViewProj = matrix_ViewProj;
                matrix_LastViewFlipYProj = matrix_ViewFlipYProj;
                matrix_LastViewJitterProj = matrix_ViewJitterProj;
                matrix_LastViewFlipYJitterProj = matrix_ViewFlipYJitterProj;
            }
        }

        internal void Commit()
        {
            if (!m_HasPreparedView) return;
            m_HasPreparedView = false;
            lastJitter = jitter;
            lastFrameIndex = frameIndex;
            matrix_LastViewProj = matrix_ViewProj;
            matrix_LastViewFlipYProj = matrix_ViewFlipYProj;
            matrix_LastViewJitterProj = matrix_ViewJitterProj;
            matrix_LastViewFlipYJitterProj = matrix_ViewFlipYJitterProj;
            m_LastCameraPosition = m_PreparedPosition;
            m_LastCameraRotation = m_PreparedRotation;
            m_LastProjection = m_PreparedProjection;
            previousJitterUV = TemporalJitter.ProjectionJitterUV(matrix_FlipYJitterProj, matrix_FlipYProj);
            m_CommittedFrames++;
            m_HasCommittedView = true;
        }

        public void SetUniformData(CommandBuffer cmdBuffer, int2 renderSize)
        {
            float2 resolution = renderSize;
            cmdBuffer.SetGlobalVector(PassShaderIDs.ScreenParams, new Vector4(resolution.x, resolution.y, 1f + 1f / resolution.x, 1f + 1f / resolution.y));
            cmdBuffer.SetGlobalInt(ID_FrameIndex, frameIndex);
            cmdBuffer.SetGlobalInt(ID_LastFrameIndex, lastFrameIndex);
            cmdBuffer.SetGlobalVector(ID_TAAJitter, new float4(jitter.x / resolution.x, jitter.y / resolution.y, lastJitter.x / resolution.x, lastJitter.y / resolution.y));
            cmdBuffer.SetGlobalMatrix(ID_Matrix_WorldToView, matrix_WorldToView);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_ViewToWorld, matrix_ViewToWorld);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_Proj, matrix_Proj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvProj, matrix_InvProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_JitterProj, matrix_JitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvJitterProj, matrix_InvJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_FlipYProj, matrix_FlipYProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvFlipYProj, matrix_InvFlipYProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_FlipYJitterProj, matrix_FlipYJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvFlipYJitterProj, matrix_InvFlipYJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_ViewProj, matrix_ViewProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvViewProj, matrix_InvViewProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_ViewFlipYProj, matrix_ViewFlipYProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvViewFlipYProj, matrix_InvViewFlipYProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_ViewJitterProj, matrix_ViewJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvViewJitterProj, matrix_InvViewJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_ViewFlipYJitterProj, matrix_ViewFlipYJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_InvViewFlipYJitterProj, matrix_InvViewFlipYJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_LastViewProj, matrix_LastViewProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_LastViewFlipYProj, matrix_LastViewFlipYProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_LastViewJitterProj, matrix_LastViewJitterProj);
            cmdBuffer.SetGlobalMatrix(ID_Matrix_LastViewFlipYJitterProj, matrix_LastViewFlipYJitterProj);
        }
    }

    public partial class InfinityRenderPipeline : RenderPipeline
    {
        private bool m_UpdateInit;
        private RGBuilder m_RGBuilder;
        private ResourcePool m_ResourcePool;
        private Dictionary<int, CameraFrameState> m_CameraStates;
        private readonly List<int> m_CameraStateRecycleIds = new List<int>(8);
        private Dictionary<int, ProfilingSampler> m_CameraSamplers;
        private BufferHandle m_DiffusionProfileBuffer;
        private DiffusionProfileRecord[] m_DiffusionProfileRecords = Array.Empty<DiffusionProfileRecord>();
        private AtmosphereSharedCache m_AtmosphereSharedCache;
        private LocalShadowCache m_LocalShadowCache;
        private LightCookieAtlas m_LightCookieAtlas;
        private CookieProduction m_CookieProduction;
        private ProbeVolumeIntegration m_ProbeVolumes;
        private ReflectionProbeAtlas m_ReflectionProbeAtlas;
        private ReflectionProbeProduction m_ReflectionProbeProduction;
        // Pipeline-wide frame transactions, driven in one order by Render (camera-owned ones live in CameraFrameState).
        IFrameTransaction[] m_FrameTransactions = Array.Empty<IFrameTransaction>();
        SupportedRenderingFeatures m_PreviousSupportedFeatures;
        string m_PreviousGlobalRenderPipeline;
        const string RtaoAssetDisabled = "Ray tracing is disabled on the RP Asset.";
        const string RtaoFrameSettings = "Disabled by Frame Settings.";
        const string RtaoVolumeInactive = "RTAO Volume is inactive.";
        const string RtaoNotReady = "UnifiedRayTracing is not ready.";
        const string RtaoNoInstances = "URT accel has no mesh instances.";
        bool m_PreviousLightsUseLinearIntensity;
        bool m_PreviousLightsUseColorTemperature;
        bool m_PreviousUseScriptableRenderPipelineBatching;
#if UNITY_EDITOR
        static InfinityRenderPipeline s_LightmapperStateOwner;
        InfinityRenderPipeline m_PreviousLightmapperStateOwner;
        bool m_LightmapperStateReleased;
        UnityEngine.Experimental.GlobalIllumination.Lightmapping.RequestLightsDelegate m_PreviousLightmapperDelegate;
#endif

        static InfinityRenderPipeline s_GlobalStateOwner;

        // The active pipeline's atmosphere cubemap from the last submitted frame (Editor baking-sky tool).
        internal static bool TryGetAtmosphereCubemap(out RenderTexture cubemap)
        {
            cubemap = null;
            InfinityRenderPipeline owner = s_GlobalStateOwner;
            return owner != null && owner.m_AtmosphereSharedCache != null && owner.m_AtmosphereSharedCache.TryGetCubemap(out cubemap);
        }
        InfinityRenderPipeline m_PreviousGlobalStateOwner;
        SupportedRenderingFeatures m_InstalledSupportedFeatures;
        Material m_PreviousBlitMaterial;
        Texture m_PreviousBestFitTexture;
        int m_PreviousAntiAliasing;
        bool m_InstalledBatching;
        Material m_InstalledBlitMaterial;
        Texture m_InstalledBestFitTexture;
        VolumeProfile m_InstalledQualityProfile;
        bool m_OwnsVolumeManager;
        bool m_ResourcesDisposed;

        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        InfinityRenderPipelineGlobalSettings m_GlobalSettings;
        internal InfinityRenderPipelineResources resources;
        internal InfinityRenderPipelineRuntimeShaders shaders => resources.shaders;
        internal PipelineKernels kernels => resources.kernels;
        internal BlitShaderPasses blitPasses => resources.blitPasses;
        public override RenderPipelineGlobalSettings defaultSettings => m_GlobalSettings;
        internal RenderContext renderContext;
        // Read-only access for diagnostics observers that report pool estimates outside the measured scopes.
        internal ResourcePool resourcePool => m_ResourcePool;
        // The asset snapshot this pipeline instance renders with (diagnostics read it outside the measured scopes).
        internal InfinityRenderPipelineAsset.Settings settings => m_Settings;

        public InfinityRenderPipeline(InfinityRenderPipelineAsset asset)
        {
            // Pooled and retired resources are reused or destroyed only after their GPU completion fence passes.
            if (!SystemInfo.supportsGraphicsFence)
                throw new NotSupportedException("InfinityRP requires graphics fences.");
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));
            m_Settings = asset.ResolveSettings();
            if (m_Settings.atmosphericalProfile == null)
                throw new ArgumentException("InfinityRP requires a pipeline asset with AtmosphericalProfile.", nameof(asset));
            m_GlobalSettings = InfinityRenderPipelineGlobalSettings.Require();
            resources = new InfinityRenderPipelineResources();
            try
            {
                CaptureGraphicsState();
                SetGraphicsSetting();
                QualitySettings.antiAliasing = 0;
                RTHandles.Initialize(Screen.width, Screen.height);

                m_InstalledQualityProfile = asset.qualityVolumeProfile;
                VolumeManager.instance.Initialize(resources.defaultVolumeProfile, m_InstalledQualityProfile);
                m_OwnsVolumeManager = true;
                InfinityDebugDisplaySettings.EnsureRegistered();

                m_UpdateInit = true;
                m_ResourcePool = new ResourcePool();
                renderContext = new RenderContext(m_ResourcePool, shaders.meshDrawPipelineCS);
                m_RGBuilder = new RGBuilder("RenderGraph", m_ResourcePool);
                m_CameraStates = new Dictionary<int, CameraFrameState>();
                m_CameraSamplers = new Dictionary<int, ProfilingSampler>();
                m_RGBuilder.SetMeshWorld(renderContext.meshWorld);
                m_AtmosphereSharedCache = new AtmosphereSharedCache(m_ResourcePool);
                m_LocalShadowCache = new LocalShadowCache(m_ResourcePool);
                m_LightCookieAtlas = new LightCookieAtlas(m_ResourcePool);
                m_CookieProduction = new CookieProduction { atlas = m_LightCookieAtlas };
                m_ProbeVolumes = new ProbeVolumeIntegration(m_Settings);
                m_ReflectionProbeAtlas = new ReflectionProbeAtlas(m_ResourcePool);
                m_ReflectionProbeProduction = new ReflectionProbeProduction { atlas = m_ReflectionProbeAtlas };
                m_FrameTransactions = new IFrameTransaction[] { m_AtmosphereSharedCache, m_LocalShadowCache, m_LightCookieAtlas, m_ReflectionProbeAtlas };
            }
            catch (Exception constructionFailure)
            {
                try { ReleaseOwnedResources(); }
                catch (Exception cleanupFailure) { throw new AggregateException(constructionFailure, cleanupFailure); }
                throw;
            }
        }

        protected override void Render(ScriptableRenderContext scriptableRenderContext, List<Camera> cameras)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PipelineDiagnostics.BeginFrame(cameras);
#endif
            Exception firstCameraException = null;
            bool submitted = false;
            bool frameValid = false;

            using (new PipelineFrameMeasurement.Scope(false))
            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.FrameRendering)))
            {
                m_ResourcePool.BeginFrame();
                renderContext.scriptableRenderContext = scriptableRenderContext;
                m_RGBuilder.RecoverUnjoinedAsync(scriptableRenderContext, m_ResourcePool.requiresQueueDrain);
                m_ResourcePool.QueueDrainAccepted();

                InvokeProxyUpdate();
                renderContext.meshWorld.UpdateResidency();
                CommandBuffer cmdBuffer = CommandBufferPool.Get();
                cmdBuffer.SetExecutionFlags(CommandBufferExecutionFlags.None);
                cmdBuffer.Clear();

                bool contextBegan = false;
                try
                {
                    BeginContextRendering(scriptableRenderContext, cameras);
                    contextBegan = true;
#if UNITY_EDITOR
                    resources.ResolveShadersAfterImport();
#endif
                    for (int i = 0; i < m_FrameTransactions.Length; ++i)
                    {
                        m_FrameTransactions[i].BeginFrame();
                    }
                    for (int i = 0; i < cameras.Count; ++i)
                    {
                        RenderCamera(cameras[i], cameras, cmdBuffer, ref firstCameraException);
                    }
                }
                catch (Exception exception)
                {
                    if (firstCameraException == null) firstCameraException = exception;
                }

                try
                {
                    frameValid = SubmitFrame(scriptableRenderContext, cmdBuffer, ref firstCameraException, out submitted);
                }
                finally
                {
                    try
                    {
                        if (contextBegan) EndContextRendering(scriptableRenderContext, cameras);
                    }
                    catch (Exception exception)
                    {
                        if (firstCameraException == null) firstCameraException = exception;
                    }
                    finally
                    {
                        try { cmdBuffer.Clear(); }
                        catch (Exception error) { if (firstCameraException == null) firstCameraException = error; }
                        try { CommandBufferPool.Release(cmdBuffer); }
                        catch (Exception error) { if (firstCameraException == null) firstCameraException = error; }
                    }
                }

            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PipelineDiagnostics.EndFrame(new PipelineFrameResult(m_CameraStates, frameValid, submitted, firstCameraException,
                m_RGBuilder.RetiredResourceCount, renderContext.lightContext.RetiredBufferCount, m_RGBuilder.CanRetireSubmittedResources));
#endif
            if (firstCameraException != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstCameraException).Throw();
        }

        // Queues the frame, completes every frame transaction by the outcome and retires what the reuse fence covers.
        // A failure after submission is kept as the frame's first exception.
        bool SubmitFrame(ScriptableRenderContext scriptableRenderContext, CommandBuffer cmdBuffer, ref Exception firstCameraException, out bool submitted)
        {
            GraphicsFence poolFence = cmdBuffer.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
            var retiredReuseFence = new ResourceReuseFence
            {
                hasGraphics = true, graphics = cmdBuffer.CreateAsyncGraphicsFence(), graphicsCompletion = poolFence
            };
            bool frameValid = FrameSubmission.Execute(new ContextFrameSubmission(scriptableRenderContext),
                cmdBuffer, ref firstCameraException, out submitted, out bool fenceCommandsQueued, out bool nativeSubmitted);

            try
            {
                if (!nativeSubmitted) renderContext.meshWorld.transformVersions.RollbackUnsubmitted();
                if (!nativeSubmitted) m_ResourcePool.SubmissionUncertain(true);
                else if (!fenceCommandsQueued || !m_RGBuilder.CanRetireSubmittedResources) m_ResourcePool.SubmissionNeedsJoin();
                frameValid &= m_RGBuilder.CanRetireSubmittedResources;
                InfinityRayTracingEnvironment.current?.CompleteFrame(frameValid);
                CompleteFrameTransaction(m_FrameTransactions, m_CameraStates, frameValid);
                if (nativeSubmitted && fenceCommandsQueued && m_RGBuilder.CanRetireSubmittedResources)
                {
                    FlushFrameRetirement(retiredReuseFence);
                    m_ResourcePool.AfterSubmit(poolFence, retiredReuseFence);
                    RecycleUnseenCameraStates(Time.frameCount, retiredReuseFence);
                }
            }
            catch (Exception exception)
            {
                if (firstCameraException == null) firstCameraException = exception;
            }
            return frameValid;
        }

        // One camera: PrepareCamera decides, CullCamera culls, RecordCamera records the graph, Execute runs it.
        // A failure rolls this camera back and keeps the frame going; a queue submission failure aborts the frame.
        void RenderCamera(Camera camera, List<Camera> cameras, CommandBuffer cmdBuffer, ref Exception firstCameraException)
        {
            ScriptableRenderContext scriptableRenderContext = renderContext.scriptableRenderContext;
            camera.TryGetComponent(out InfinityAdditionalCameraData cameraData);
            CameraFrameState state = BeginCameraState(camera, out bool newlyCreated, out int previousSeenFrame);
            using (new ProfilingScope(cmdBuffer, GetCameraSampler(camera, cameraData)))
            {
                BeginCameraRendering(scriptableRenderContext, camera);
                try
                {
                    CameraSetup setup = PrepareCamera(camera, cameras, cameraData, state, newlyCreated, previousSeenFrame);
                    CameraScene scene = CullCamera(setup, cmdBuffer);
                    using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.RecordRG)))
                    {
                        RecordCamera(new CameraRecordContext(setup, scene), scene.mainView);
                    }

                    using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ExecuteRG)))
                    {
                        m_RGBuilder.debugLabel = RGDebugSession.captureEnabled ? camera.name : null;
                        m_RGBuilder.Execute(scriptableRenderContext, cmdBuffer);
                        state.executeSucceeded = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        PipelineDiagnostics.AfterCamera(camera, true, null);
#endif
                    }
                }
                catch (Exception exception)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    PipelineDiagnostics.AfterCamera(camera, false, exception);
#endif
                    // A queue failure may follow an accepted write but precede OnQueued; each owner decides what it keeps.
                    bool queueUncertain = m_RGBuilder.HasQueueSubmissionFailure;
                    state.CameraFailed(queueUncertain);
                    for (int i = 0; i < m_FrameTransactions.Length; ++i)
                    {
                        m_FrameTransactions[i].CameraFailed(queueUncertain);
                    }
                    if (firstCameraException == null)
                    {
                        firstCameraException = exception;
                    }
                    if (m_RGBuilder.HasQueueSubmissionFailure) throw;
                }
                finally
                {
                    // If recording aborted before Execute, still free DrawList visibility / GPU payloads.
                    m_RGBuilder.ClearRecordedGraph();
                }
                EndCameraRendering(scriptableRenderContext, camera);
            }
        }

        // Marks the camera's cross-frame state as prepared this render. previousSeenFrame is its last render before this one.
        CameraFrameState BeginCameraState(Camera camera, out bool newlyCreated, out int previousSeenFrame)
        {
            CameraFrameState state = GetOrCreateCameraFrameState(GetCameraID(camera), out newlyCreated);
            previousSeenFrame = state.lastSeenFrame;
            state.lastSeenFrame = Time.frameCount;
            state.camera = camera;
            state.preparedThisRender = true;
            state.cameraType = camera.cameraType;
            state.executeSucceeded = false;
            state.temporalColorView = default;
            return state;
        }

        // Every decision of this camera's frame that does not need culling.
        CameraSetup PrepareCamera(Camera camera, List<Camera> cameras, InfinityAdditionalCameraData cameraData, CameraFrameState state,
            bool newlyCreated, int previousSeenFrame)
        {
            CameraRenderPolicy policy = CameraRenderPolicy.For(camera.cameraType);
            ResolvedFrameSettings frameSettings = FrameSettingsUtility.Resolve(m_Settings.defaultFrameSettings, m_Settings.reflectionFrameSettings,
                camera.cameraType == CameraType.Reflection, cameraData != null && cameraData.customFrameSettings,
                cameraData != null ? cameraData.frameSettings : default);
            CameraDimensionDescriptor dimensions = CameraDimensionDescriptor.FromCamera(camera, m_Settings.enableSuperResolution, m_Settings.renderScale, cameraData);
            EHistoryReset historyReset = state.ResolveHistoryReset(camera, policy, newlyCreated, previousSeenFrame, Time.frameCount, dimensions);
            ETemporalResolve temporalResolve = !policy.temporal ? ETemporalResolve.None
                : dimensions.superResolution ? ETemporalResolve.SuperResolution : ETemporalResolve.TAA;
            dimensions = dimensions.WithCapacity(state.internalCapacity.Prepare(dimensions.internalSize), state.displayCapacity.Prepare(dimensions.displaySize));
            state.preparedDimensions = dimensions;
            m_RGBuilder.SetTextureCapacity(dimensions.textureCapacity);
            state.historyCache.SetTextureCapacity(dimensions.textureCapacity);
            state.BeginFrame();

            ResolveVolumeSelection(camera, cameras, cameraData, out Transform volumeTrigger, out LayerMask volumeLayerMask);
            VolumeManager.instance.Update(state.volumeStack, volumeTrigger, volumeLayerMask);
            VolumeSnapshot volumes = VolumeSnapshot.Capture(state.volumeStack);
            AtmosphereParameter atmosphere = AtmosphereParameter.FromProfile(m_Settings.atmosphericalProfile);
            atmosphere.ThrowIfInvalid();
            state.cameraUniform.UpdateCurrFrameData(camera, dimensions.internalSize, historyReset);

            FeatureSupportContext featureSupport = ConfigureFrameFeatures(state, policy, frameSettings, temporalResolve, volumes);
            // Unity keeps previous object transforms only for a camera that declares motion vectors. Declare them once,
            // before camera setup and culling; the flag stays on the camera.
            const DepthTextureMode motionOutputs = DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            if (state.features.ShouldRecord(EFrameFeature.Motion) && (camera.depthTextureMode & motionOutputs) != motionOutputs)
            {
                camera.depthTextureMode |= motionOutputs;
            }

            var setup = new CameraSetup(camera, state, policy, frameSettings, dimensions, historyReset, temporalResolve,
                policy.postProcessing && CoreUtils.ArePostProcessesEnabled(camera), !CoreUtils.IsSceneLightingDisabled(camera),
                InfinityDebugDisplaySettings.current.ResolveDebugView(camera), ResolveOutputDecision(camera, state), volumes, atmosphere, featureSupport);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PipelineDiagnostics.BeforeCamera(setup);
#endif
            return setup;
        }

        OutputTransformDecision ResolveOutputDecision(Camera camera, CameraFrameState state)
        {
            UpdateHdrTonemapping();
            OutputTransformDecision decision = OutputTransformUtility.ResolveFromHardware(m_Settings.outputMode, m_Settings.hdrEncoding, camera,
                state.hasResolvedBackbufferFormat, state.lastResolvedBackbufferFormat);
            state.lastResolvedBackbufferFormat = decision.backbufferFormat;
            state.hasResolvedBackbufferFormat = decision.backbufferFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            if (!state.loggedOutputDecision)
            {
                Debug.Log($"[InfinityRP] Present camera={camera.name} type={camera.cameraType} format={decision.backbufferFormat} colorSpace={decision.colorSpace} policy={decision.policy} authority={(decision.displayTransferAuthority ? "DisplayTransferCapability" : "TextureFormat")}");
                state.loggedOutputDecision = true;
            }
            return decision;
        }

        // Unity camera setup, culling and everything that reads the culling results before recording.
        CameraScene CullCamera(in CameraSetup setup, CommandBuffer cmdBuffer)
        {
            ScriptableRenderContext scriptableRenderContext = renderContext.scriptableRenderContext;
            Camera camera = setup.camera;
            CullingResults cullingResults;
            MeshView mainView;
            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.SetupCamera)))
            {
#if UNITY_EDITOR
                if (camera.cameraType == CameraType.SceneView)
                {
                    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                }
#endif

                setup.state.cameraUniform.SetUniformData(cmdBuffer, setup.dimensions.internalSize);
                // Adaptive Probe Volumes: CoreRP-owned resources, bound before culling and recording.
                m_ProbeVolumes.SetupCamera(cmdBuffer, camera, setup.state.volumeStack, setup.policy.temporal);
                scriptableRenderContext.SetupCameraProperties(camera);
                scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();

                VFXManager.PrepareCamera(camera);

                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.CulllingScene)))
                {
                    camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParameters);
                    cullingParameters.shadowDistance = m_Settings.shadowDistance;
                    cullingParameters.cullingOptions = CullingOptions.ShadowCasters | CullingOptions.NeedsLighting |
                        CullingOptions.NeedsReflectionProbes | CullingOptions.DisablePerObjectCulling;
                    cullingResults = scriptableRenderContext.Cull(ref cullingParameters);

                    BeginMeshCamera(camera, m_Settings.meshBackendPolicy);
                    mainView = MeshView.FromCamera(camera, ref cullingParameters);
                    setup.state.meshViewKey = mainView.viewKey;
                }

                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ProcessLOD)))
                {
                    List<TerrainComponent> terrains = renderContext.GetWorldTerrains();
                    float4x4 matrix_Proj = TerrainUtility.GetProjectionMatrix(camera.fieldOfView + 30, setup.dimensions.internalSize.x, setup.dimensions.internalSize.y, camera.nearClipPlane, camera.farClipPlane);
                    for (int j = 0; j < terrains.Count; ++j)
                    {
                        TerrainComponent terrain = terrains[j];
                        terrain.ProcessLOD(camera.transform.position, matrix_Proj);

#if UNITY_EDITOR
                        if (Handles.ShouldRenderGizmos())
                        {
                            terrain.DrawBounds(true);
                        }
#endif
                    }
                }

                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ProcessLight)))
                {
                    ShadowAllocatorSettings shadowSettings;
                    shadowSettings.cascadeMapResolution = m_Settings.cascadeShadowMapResolution;
                    shadowSettings.localMapResolution = m_Settings.localShadowMapResolution;
                    shadowSettings.shadowDistance = m_Settings.shadowDistance;
                    shadowSettings.cascadeRatios = m_Settings.cascadeSplitRatios;
                    shadowSettings.maxLocalLights = m_Settings.maxShadowedLocalLights;
                    shadowSettings.cascadeCount = m_Settings.cascadeCount;
                    shadowSettings.cascadeBlendWidth = m_Settings.cascadeBlendWidth;
                    shadowSettings.filterGuardTexels = m_Settings.shadowFilter.pcfRadius + 1;
                    m_LocalShadowCache.Configure(m_Settings.cachedShadowAtlasWidth,
                        m_Settings.localShadowMapResolution, Mathf.Max(1, m_Settings.localShadowMapResolution / 4));
                    if (m_LocalShadowCache.SlotCount > 0) m_LocalShadowCache.GetAtlas();
                    LightContext lightContext = renderContext.lightContext;
                    lightContext.Build(cullingResults, renderContext.GetWorldLight(), camera, shadowSettings, setup.dimensions.internalSize, renderContext.meshWorld, m_LocalShadowCache);
                    m_LightCookieAtlas.Configure(m_Settings.cookieAtlasResolution, m_Settings.cookieMaxSize);
                    // A camera with cookies / probes switched off resolves no atlas entries (records carry no cookie index).
                    lightContext.ResolveCookies(setup.frameSettings.IsEnabled(EFrameSetting.LightCookies) ? m_LightCookieAtlas : null,
                        cullingResults, renderContext.GetWorldLight(), Time.frameCount);
                    m_ReflectionProbeAtlas.Configure(m_Settings.reflectionProbeResolution, m_Settings.maxReflectionProbes);
                    lightContext.ResolveReflectionProbes(setup.frameSettings.IsEnabled(EFrameSetting.ReflectionProbes) ? m_ReflectionProbeAtlas : null,
                        cullingResults, Time.frameCount);
                    ConfigureContentFeatures(setup);
                    InfinityDebugDisplaySettingsLighting lightingDebug = InfinityDebugDisplaySettings.current.lighting;
                    lightingDebug.lastDirectionalCount = lightContext.DirectionalLightCount;
                    lightingDebug.lastLocalCount = lightContext.LocalLightCount;
                    lightingDebug.lastReflectionProbeCount = lightContext.ReflectionProbeCount;
                    lightingDebug.lastUnsupportedShapeCount = lightContext.UnsupportedShapeCount;
                    lightingDebug.lastDistanceCulledCount = lightContext.DistanceCulledCount;

                    scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                    cmdBuffer.Clear();
                }

                VFXCameraXRSettings cameraXRSettings;
                cameraXRSettings.viewTotal = 1;
                cameraXRSettings.viewCount = 1;
                cameraXRSettings.viewOffset = 0;
                VFXManager.ProcessCameraCommand(camera, cmdBuffer, cameraXRSettings, cullingResults);
                scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();
            }

            return new CameraScene(cullingResults, mainView);
        }

        // The camera's pass list in submission order. mainView is replaced when two-stage occlusion is prepared.
        void RecordCamera(in CameraRecordContext context, MeshView mainView)
        {
            // PHASE 0: frame-constant async. Zero RG-resource inputs; submit first
            // so the whole geometry raster window can overlap them.
            UploadLightData(renderContext.lightContext);
            ComputeLightCookies(context);
            ComputeReflectionProbes(context);
            ComputeCombineLuts(context, CombineLutParameterUtility.FromVolumeStack(context.volumes.filmTonemap, context.volumes.colorGrading));
            ComputeAtmosphericLUT(context);

            ComputeTemporalColorPyramid(context);

            // PHASE 1: geometry raster (shared depth attachment chain).
            bool twoStageMesh = PrepareMeshOcclusion(context, ref mainView, out var meshOcclusion);
            RecordMeshGpuCulls(context, mainView);
            m_RGBuilder.FlushMeshFrustumCulling();
            MeshView depthView = twoStageMesh ? meshOcclusion.first : mainView;
            RGMeshDrawListHandle depthList = MakeMeshList(depthView, InfinityPassIDs.RasterDepthPass);
            RGMeshDrawListHandle[] cascadeLists = CreateCascadeMeshLists(context);
            RGMeshDrawListHandle[] localLists = CreateLocalMeshLists(context);
            RGMeshDrawListHandle gbufferList = default, motionList = default, forwardList = default, translucentDepthList = default, translucentT0 = default, translucentT1 = default, translucentT2 = default;
            if (twoStageMesh)
            {
                PrepareMeshList(depthList);
                PrepareMeshLists(cascadeLists);
                PrepareMeshLists(localLists);
            }
            else
            {
                gbufferList = MakeMeshList(mainView, InfinityPassIDs.RasterGBufferPass);
                motionList = MakeMeshList(mainView, InfinityPassIDs.RasterMotionPass);
                forwardList = MakeMeshList(mainView, InfinityPassIDs.RasterForwardPass);
                translucentDepthList = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentDepthPass);
                translucentT0 = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentT0Pass);
                translucentT1 = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentT1Pass);
                translucentT2 = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentT2Pass);
                PrepareMeshLists(depthList, gbufferList, motionList, forwardList, translucentDepthList, translucentT0, translucentT1, translucentT2);
                PrepareMeshLists(cascadeLists);
                PrepareMeshLists(localLists);
            }
            RasterDepth(context, depthView, depthList);
            if (twoStageMesh)
            {
                RecoverMeshOcclusion(context, meshOcclusion);
                RGMeshDrawListHandle recoveryList = MakeMeshList(meshOcclusion.recovered, InfinityPassIDs.RasterDepthPass);
                gbufferList = MakeMeshList(mainView, InfinityPassIDs.RasterGBufferPass);
                motionList = MakeMeshList(mainView, InfinityPassIDs.RasterMotionPass);
                forwardList = MakeMeshList(mainView, InfinityPassIDs.RasterForwardPass);
                translucentDepthList = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentDepthPass);
                translucentT0 = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentT0Pass);
                translucentT1 = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentT1Pass);
                translucentT2 = MakeMeshList(mainView, InfinityPassIDs.RasterTranslucentT2Pass);
                PrepareMeshLists(recoveryList, gbufferList, motionList, forwardList, translucentDepthList, translucentT0, translucentT1, translucentT2);
                RasterDepthRecovery(recoveryList);
            }
            RasterDBuffer(context);
            RasterGBuffer(context, mainView, gbufferList);
            RasterMotion(context, mainView, motionList);

            // PHASE 2: depth-derived async. ZBin stays here so fog/deferred can consume it.
            ComputeHiZ(context);
            ComputeHalfResDownsample(context);
            ComputeLightTileMask(context);

            // Dispatch near-deadline AO/contact before the longer previous-color effects.
            // Their graphics-independent results can overlap the shadow raster window.
            TraceOcclusion(context);
            ComputeGroundTruthOcclusion(context);
            ComputeContactShadow(context);
            if (SystemInfo.supportsAsyncCompute)
            {
                ComputeScreenSpaceReflection(context);
                ComputeScreenSpaceIndirect(context);
            }

            // PHASE 3: shadow raster (longest ROP window).
            CullShadowCasterSplits(context);
            RasterCascadeShadow(context, cascadeLists);
            RasterLocalShadow(context, localLists);
            ComputeShadowMoments(context);

            // PHASE 6: base lighting, forward, indirect replacement and diffuse-only SSS.
            ComputeDeferredShading(context);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordDeferredLightingCapture(context);
#endif
            RasterForward(context, mainView, forwardList);
            if (!SystemInfo.supportsAsyncCompute)
            {
                ComputeScreenSpaceReflection(context);
                ComputeScreenSpaceIndirect(context);
            }
            ComputeScreenSpaceComposite(context);
            ResolveOpaqueSceneColor();
            // History copies stay after the shadow window; an early graphics copy
            // would join the async effects before independent shadow rasterization.
            CopyHistorySSR(context);
            CopyHistorySSGI(context);
            ComputeBurleySubsurface(context);
            ComputeAtmosphericSkyAndFog(context);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordLightingCapture(context);
#endif

            // PHASE 7: T0 depth → Cloud/Fog → FogComposite → FoggedSceneColor → T0 → RefractionPyramid → T1 → T2
            RasterTranslucentDepth(context, mainView, translucentDepthList);
            ComputeVolumetricCloud(context);
            CopyHistoryVolumetricCloud(context);
            ComputeVolumetricFog(context);
            CopyHistoryVolumetricFog(context);
            ComputeFogComposite(context);
            ResolveFoggedSceneColor();
            RasterTranslucentT0(context, mainView, translucentT0);
            ComputeColorPyramid(context);
            RasterTranslucentT1(context, mainView, translucentT1);
            RasterTranslucentT2(context, mainView, translucentT2);

            // PHASE 8: temporal resolve + post (TAA after fog/translucents)
            // Previews and probe faces have no temporal resolve; post reads FoggedSceneColor.
            if (context.temporalResolve == ETemporalResolve.SuperResolution)
            {
                ComputeSuperResolution(context);
            }
            else if (context.temporalResolve == ETemporalResolve.TAA)
            {
                ComputeAntiAliasing(context);
            }
            RecordCameraOutput(context);
            context.features.EnsureRequiredProducers(context.temporalResolve);
        }

        void BeginMeshCamera(Camera camera, EMeshDrawBackendPolicy assetPolicy)
        {
            MeshWorld world = renderContext.meshWorld;
            InfinityDebugDisplaySettingsMesh debug = InfinityDebugDisplaySettings.current.mesh;
            EMeshDrawBackendPolicy? diagnosticOverride = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PipelineDiagnostics.TryOverrideBackend(camera, out EMeshDrawBackendPolicy diagnosticBackend))
                diagnosticOverride = diagnosticBackend;
#endif
            world.requestedBackend = ResolveMeshBackend(diagnosticOverride, debug.backendOverride, assetPolicy);
            world.BeginCamera(camera);
            debug.requestedBackend = world.requestedBackend;
            debug.actualBackend = world.SelectBackend(MeshPassTable.Depth);
            debug.backendReason = world.requestedBackend == EMeshDrawBackendPolicy.CPUDirect ? "Requested CPU Direct"
                : world.gpuBackend.isSupported ? "Indirect drawing supported" : "Indirect drawing unsupported; CPU Direct";
            debug.entityCount = world.entityCount;
            debug.elementCount = world.elementCount;
            debug.passCacheCount = world.stats.passCacheCount;
            debug.commandCount = world.stats.commandCount;
        }

        /// <summary>A diagnostics override wins, then the Rendering Debugger, then the asset.</summary>
        internal static EMeshDrawBackendPolicy ResolveMeshBackend(EMeshDrawBackendPolicy? diagnosticOverride, EMeshDrawBackendPolicy? debugOverride,
            EMeshDrawBackendPolicy assetPolicy)
        {
            return diagnosticOverride ?? debugOverride ?? assetPolicy;
        }

        internal static void CompleteFrameTransaction(IFrameTransaction[] shared, Dictionary<int, CameraFrameState> states, bool submitted)
        {
            for (int i = 0; i < shared.Length; ++i)
            {
                if (submitted) shared[i].CommitFrame();
                else shared[i].RollbackPending();
            }
            foreach (CameraFrameState state in states.Values)
            {
                if (submitted && state.executeSucceeded) state.CommitFrame();
                else if (state.preparedThisRender) state.RollbackFrame();
            }
        }

        void FlushFrameRetirement(ResourceReuseFence reuseFence)
        {
            m_RGBuilder.FlushRetiredResources(reuseFence);
            renderContext.lightContext.FlushRetiredBuffers();
            for (int i = 0; i < m_FrameTransactions.Length; ++i)
            {
                m_FrameTransactions[i].FlushRetired(reuseFence);
            }
            foreach (CameraFrameState state in m_CameraStates.Values)
            {
                state.FlushRetired(reuseFence);
            }
            renderContext.meshWorld.FlushRetired(reuseFence);
        }

        void CaptureGraphicsState()
        {
            m_PreviousGlobalStateOwner = s_GlobalStateOwner;
#if UNITY_EDITOR
            m_PreviousLightmapperStateOwner = s_LightmapperStateOwner;
            m_PreviousLightmapperDelegate = UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate();
#endif
            m_PreviousSupportedFeatures = SupportedRenderingFeatures.active;
            m_PreviousGlobalRenderPipeline = Shader.globalRenderPipeline;
            m_PreviousLightsUseLinearIntensity = GraphicsSettings.lightsUseLinearIntensity;
            m_PreviousLightsUseColorTemperature = GraphicsSettings.lightsUseColorTemperature;
            m_PreviousUseScriptableRenderPipelineBatching = GraphicsSettings.useScriptableRenderPipelineBatching;
            m_PreviousAntiAliasing = QualitySettings.antiAliasing;
            m_PreviousBlitMaterial = GraphicsUtility.s_BlitMaterial;
            m_PreviousBestFitTexture = Shader.GetGlobalTexture("g_BestFitNormal_LUT");
        }

#if UNITY_EDITOR
        void InstallLightmapperState()
        {
            UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(InfinityLightmapperDelegate.Delegate);
            m_LightmapperStateReleased = false;
            s_LightmapperStateOwner = this;
        }

        void RestoreLightmapperState()
        {
            m_LightmapperStateReleased = true;
            if (s_LightmapperStateOwner != this) return;
            var previousOwner = m_PreviousLightmapperStateOwner;
            var previousDelegate = m_PreviousLightmapperDelegate;
            while (previousOwner != null && previousOwner.m_LightmapperStateReleased)
            {
                if (previousDelegate == InfinityLightmapperDelegate.Delegate)
                    previousDelegate = previousOwner.m_PreviousLightmapperDelegate;
                previousOwner = previousOwner.m_PreviousLightmapperStateOwner;
            }
            s_LightmapperStateOwner = previousOwner;
            if (UnityEngine.Experimental.GlobalIllumination.Lightmapping.GetDelegate() == InfinityLightmapperDelegate.Delegate)
                UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate(previousDelegate);
        }
#endif

        void RestoreGraphicsState()
        {
#if UNITY_EDITOR
            RestoreLightmapperState();
#endif
            if (s_GlobalStateOwner != this) return;
            s_GlobalStateOwner = m_PreviousGlobalStateOwner != null && !m_PreviousGlobalStateOwner.m_ResourcesDisposed ? m_PreviousGlobalStateOwner : null;
            // A replacement pipeline may have already installed its own global state.
            if (!ReferenceEquals(SupportedRenderingFeatures.active, m_InstalledSupportedFeatures)) return;
            SupportedRenderingFeatures.active = m_PreviousSupportedFeatures;
            if (Shader.globalRenderPipeline == "InfinityRenderPipeline")
                Shader.globalRenderPipeline = m_PreviousGlobalRenderPipeline;
            if (GraphicsSettings.lightsUseLinearIntensity)
                GraphicsSettings.lightsUseLinearIntensity = m_PreviousLightsUseLinearIntensity;
            if (GraphicsSettings.lightsUseColorTemperature)
                GraphicsSettings.lightsUseColorTemperature = m_PreviousLightsUseColorTemperature;
            if (GraphicsSettings.useScriptableRenderPipelineBatching == m_InstalledBatching)
                GraphicsSettings.useScriptableRenderPipelineBatching = m_PreviousUseScriptableRenderPipelineBatching;
            if (QualitySettings.antiAliasing == 0)
                QualitySettings.antiAliasing = m_PreviousAntiAliasing;
            if (GraphicsUtility.s_BlitMaterial == m_InstalledBlitMaterial)
                GraphicsUtility.s_BlitMaterial = m_PreviousBlitMaterial;
            if (Shader.GetGlobalTexture("g_BestFitNormal_LUT") == m_InstalledBestFitTexture)
                Shader.SetGlobalTexture("g_BestFitNormal_LUT", m_PreviousBestFitTexture);
        }

        static void ResolveVolumeSelection(Camera camera, List<Camera> cameras, InfinityAdditionalCameraData cameraData, out Transform volumeTrigger, out LayerMask volumeLayerMask)
        {
            if (camera.cameraType == CameraType.Preview)
            {
                volumeTrigger = camera.transform;
                volumeLayerMask = 0;
                return;
            }

            InfinityAdditionalCameraData resolved = cameraData;
            if (camera.cameraType == CameraType.SceneView)
            {
                Camera game = FindUniqueActiveGameCamera(cameras);
                if (game != null)
                {
                    game.TryGetComponent(out resolved);
                }
            }

            volumeTrigger = (resolved != null && resolved.volumeTrigger != null) ? resolved.volumeTrigger : camera.transform;
            volumeLayerMask = resolved != null ? resolved.volumeLayerMask : (LayerMask)~0;
        }

        static Camera FindUniqueActiveGameCamera(List<Camera> cameras)
        {
            Camera found = null;
            for (int i = 0; i < cameras.Count; ++i)
            {
                Camera candidate = cameras[i];
                if (candidate == null || candidate.cameraType != CameraType.Game || !candidate.isActiveAndEnabled)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = candidate;
            }

            return found;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SetGraphicsSetting()
        {
            // Resolve dependencies and allocate the feature declaration before any global write.
            m_InstalledBatching = m_Settings.enableSRPBatch;
            m_InstalledBlitMaterial = resources.materials.blitMaterial;
            m_InstalledBestFitTexture = resources.textures.bestFitNormalTexture;
            var installedFeatures = new SupportedRenderingFeatures()
            {
                // Probes are axis aligned (their capture is world aligned); influence and projection boxes blend.
                reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.None,
                reflectionProbesBlendDistance = true,
                defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.IndirectOnly,
                mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.IndirectOnly | SupportedRenderingFeatures.LightmapMixedBakeModes.Shadowmask,
                lightmapBakeTypes = LightmapBakeType.Baked | LightmapBakeType.Mixed | LightmapBakeType.Realtime,
                lightmapsModes = LightmapsMode.NonDirectional | LightmapsMode.CombinedDirectional,
                lightProbeProxyVolumes = true,
                overridesLightProbeSystem = m_Settings.supportProbeVolume,
                skyOcclusion = m_Settings.supportProbeVolume,
                motionVectors = true,
                receiveShadows = true,
                reflectionProbes = true,
                rendererPriority = true,
                overridesFog = true,
                overridesOtherLightingSettings = true,
                editableMaterialRenderQueue = true,
                enlighten = false,
                overridesLODBias = true,
                overridesMaximumLODLevel = true,
                rendersUIOverlay = true,
                supportsHDR = true
            };
            m_InstalledSupportedFeatures = installedFeatures;
            s_GlobalStateOwner = this;
            InheritHdrTonemappingOwnership(m_PreviousGlobalStateOwner);
            SupportedRenderingFeatures.active = installedFeatures;
            Shader.globalRenderPipeline = "InfinityRenderPipeline";

            GraphicsUtility.s_BlitMaterial = m_InstalledBlitMaterial;

            GraphicsSettings.lightsUseLinearIntensity = true;
            GraphicsSettings.lightsUseColorTemperature = true;
            GraphicsSettings.useScriptableRenderPipelineBatching = m_InstalledBatching;
            Shader.SetGlobalTexture("g_BestFitNormal_LUT", m_InstalledBestFitTexture);
#if UNITY_EDITOR
            InstallLightmapperState();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected int GetCameraID(Camera camera)
        {
            int cameraId = camera.GetHashCode();

            if (camera.cameraType == CameraType.Preview)
            {
                if (camera.pixelHeight == 64)
                {
                    cameraId += 1;
                }
                // Unity will use one PreviewCamera to draw Material icon and Material Preview together, this will cause resources identity be confused.
                // We found that the Material preview can not be less than 70 pixel, and the icon is always 64, so we use this to distinguish them.
            }

            return cameraId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ProfilingSampler GetCameraSampler(Camera camera, InfinityAdditionalCameraData cameraComponent)
        {
            if (cameraComponent != null && cameraComponent.viewProfiler != null)
            {
                return cameraComponent.viewProfiler;
            }

            int cameraId = GetCameraID(camera);
            if (!m_CameraSamplers.TryGetValue(cameraId, out ProfilingSampler sampler))
            {
                sampler = new ProfilingSampler(camera.name);
                m_CameraSamplers.Add(cameraId, sampler);
            }

            return sampler;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void InvokeProxyUpdate()
        {
            using (new PipelineFrameMeasurement.Scope(true))
            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ProxyUpdate)))
            {
                RenderTaskQueue.ProcessTasks(renderContext);
                RenderTaskQueue.ClearTasks();
                renderContext.InitializeWorldDecals();
                if (ConsumeStaticPoseRefresh()) MeshComponent.NotifyAllStaticPoses();

                // Registration tasks raised by the steps above apply in the same frame.
                RenderTaskQueue.ProcessTasks(renderContext);
                RenderTaskQueue.ClearTasks();

                // One structural transaction, then poses: explicit static notifications and every dynamic transform.
                renderContext.DrainDirtyMeshes();
                renderContext.ApplyMeshTransforms();
                renderContext.UpdateDynamicMeshPoses();
            }
        }

        // Static poses refresh once per pipeline instance; in the Editor only when the asset asks for it.
        bool ConsumeStaticPoseRefresh()
        {
            if (!m_UpdateInit) return false;
            m_UpdateInit = false;
#if UNITY_EDITOR
            return m_Settings.updateProxy;
#else
            return true;
#endif
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) ReleaseOwnedResources();
        }

        void ReleaseOwnedResources()
        {
            if (m_ResourcesDisposed) return;
            m_ResourcesDisposed = true;
            var failures = new List<Exception>();
            void Release(Action action)
            {
                try { action(); }
                catch (Exception error) { failures.Add(error); }
            }
            bool ownsSharedResources = s_GlobalStateOwner == this;
            if (ownsSharedResources)
            {
                PipelineFrameMeasurement.Dispose();
                Release(RestoreHdrTonemapping);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (ownsSharedResources) Release(PipelineDiagnostics.PipelineDisposed);
#endif
            Release(() => m_RGBuilder?.ClearRecordedGraph());
            if (m_CameraStates != null)
            {
                foreach (CameraFrameState state in m_CameraStates.Values) Release(state.Dispose);
                m_CameraStates.Clear();
            }
            // History owners return their acquisitions to the pool, so they dispose before the pool does.
            Release(() => m_AtmosphereSharedCache?.Dispose());
            m_AtmosphereSharedCache = null;
            Release(() => m_LocalShadowCache?.Dispose());
            m_LocalShadowCache = null;
            Release(() => m_LightCookieAtlas?.Dispose());
            m_LightCookieAtlas = null;
            Release(() => m_ReflectionProbeAtlas?.Dispose());
            m_ReflectionProbeAtlas = null;
            Release(() => renderContext?.Dispose());
            Release(() => m_RGBuilder?.Dispose());
            if (m_DiffusionProfileBuffer.buffer != null) Release(() => m_ResourcePool.ReleaseBuffer(m_DiffusionProfileBuffer));
            m_DiffusionProfileBuffer = default;
            Release(() => m_ResourcePool?.Dispose());

            m_CameraSamplers?.Clear();
            if (ownsSharedResources && m_OwnsVolumeManager &&
                ReferenceEquals(VolumeManager.instance.globalDefaultProfile, resources.defaultVolumeProfile) &&
                ReferenceEquals(VolumeManager.instance.qualityDefaultProfile, m_InstalledQualityProfile)) Release(VolumeManager.instance.Deinitialize);
            m_OwnsVolumeManager = false;
            m_DiffusionProfileRecords = Array.Empty<DiffusionProfileRecord>();
            Release(() => m_ProbeVolumes?.Dispose());
            m_ProbeVolumes = null;
            if (ownsSharedResources) Release(() => InfinityRayTracingEnvironment.current?.Dispose());
            Release(RestoreGraphicsState);
            if (failures.Count > 0) throw new AggregateException("InfinityRP resource disposal failed.", failures);
        }

        static bool VolumeComponentActive(VolumeComponent component)
        {
            return GraphicsUtility.VolumeComponentActive(component);
        }

        CameraFrameState GetOrCreateCameraFrameState(int cameraId, out bool created)
        {
            created = !m_CameraStates.TryGetValue(cameraId, out CameraFrameState frameState);
            if (created)
            {
                frameState = new CameraFrameState(cameraId, m_ResourcePool);
                m_CameraStates.Add(cameraId, frameState);
            }

            return frameState;
        }

        void RecycleUnseenCameraStates(int frameCount, ResourceReuseFence reuseFence)
        {
            m_CameraStateRecycleIds.Clear();
            foreach (KeyValuePair<int, CameraFrameState> pair in m_CameraStates)
            {
                if (pair.Value.camera == null || CameraFrameState.ShouldRecycle(pair.Value.lastSeenFrame, frameCount, pair.Value.cameraType))
                {
                    m_CameraStateRecycleIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < m_CameraStateRecycleIds.Count; ++i)
            {
                int cameraId = m_CameraStateRecycleIds[i];
                if (m_CameraStates.TryGetValue(cameraId, out CameraFrameState frameState))
                {
                    renderContext.meshWorld.ReleaseView(frameState.meshViewKey);
                    frameState.Dispose(reuseFence);
                    m_CameraStates.Remove(cameraId);
                }
            }
        }

        // States what this camera wants to record. FeatureRegistry.Resolve decides what can record (kernels,
        // dependencies, camera policy), requests dependencies and keeps the reason for anything that cannot.
        FeatureSupportContext ConfigureFrameFeatures(CameraFrameState state, CameraRenderPolicy policy, in ResolvedFrameSettings frameSettings,
            ETemporalResolve temporalResolve, in VolumeSnapshot volumes)
        {
            FrameFeatureSet features = state.features;
            features.Reset();
            bool temporal = policy.temporal;

            foreach (EFrameFeature feature in FeatureRegistry.RequiredProducers)
            {
                features.Request(feature);
            }
            features.Request(EFrameFeature.Motion);
            features.Request(EFrameFeature.HiZ);
            features.Request(EFrameFeature.ColorPyramid);
            features.Request(EFrameFeature.PostProcess);
            if (renderContext.WorldDecalCount > 0 && frameSettings.IsEnabled(EFrameSetting.Decals))
            {
                features.Request(EFrameFeature.DBuffer);
            }

            // Without a temporal resolve PostProcess reads FoggedSceneColor directly.
            if (temporalResolve == ETemporalResolve.SuperResolution)
            {
                features.Request(EFrameFeature.SuperResolution);
            }
            else if (temporalResolve == ETemporalResolve.TAA)
            {
                features.Request(EFrameFeature.TAA);
            }

            if (VolumeComponentActive(volumes.subsurfaceScattering)) features.Request(EFrameFeature.SubsurfaceScattering);

            var rtao = volumes.rayTracingAmbientOcclusion;
            string rtaoReason = null;
            if (!m_Settings.enableRayTrace)
                rtaoReason = RtaoAssetDisabled;
            else if (!frameSettings.IsEnabled(EFrameSetting.RayTracedAmbientOcclusion))
                rtaoReason = RtaoFrameSettings;
            else if (!VolumeComponentActive(rtao))
                rtaoReason = RtaoVolumeInactive;
            else if (!InfinityRayTracingEnvironment.CanRecord(shaders))
                rtaoReason = string.IsNullOrEmpty(InfinityRayTracingEnvironment.lastBlockReason) ? RtaoNotReady : InfinityRayTracingEnvironment.lastBlockReason;
            else
            {
                var rayEnv = InfinityRayTracingEnvironment.current;
                rayEnv.SyncGeometry(renderContext.meshWorld);
                if (rayEnv.InstanceCount == 0) rtaoReason = RtaoNoInstances;
            }
            InfinityRayTracingEnvironment.ReportBlockReason(rtaoReason ?? string.Empty);

            // RTAO owns AO when it can record; otherwise GTAO does (if requested).
            if (rtaoReason == null)
            {
                features.Request(EFrameFeature.RTAO);
            }
            else if (frameSettings.IsEnabled(EFrameSetting.AmbientOcclusion) && ScreenSpaceModeUtility.ShouldRequestGTAO(volumes.screenSpaceAmbientOcclusion))
            {
                features.Request(EFrameFeature.GTAO);
            }

            EScreenSpaceMode screenSpaceMode = ScreenSpaceModeUtility.Resolve(
                volumes.screenSpaceReflection,
                volumes.screenSpaceIndirectDiffuse);
            if (ScreenSpaceModeUtility.IncludesSSR(screenSpaceMode) && frameSettings.IsEnabled(EFrameSetting.ScreenSpaceReflection))
            {
                features.Request(EFrameFeature.SSR);
            }
            if (ScreenSpaceModeUtility.IncludesSSGI(screenSpaceMode) && frameSettings.IsEnabled(EFrameSetting.ScreenSpaceGlobalIllumination))
            {
                features.Request(EFrameFeature.SSGI);
            }

            if (frameSettings.IsEnabled(EFrameSetting.VolumetricFog) && VolumeComponentActive(volumes.volumetricFog))
            {
                features.Request(EFrameFeature.VolumetricFog);
            }
            if (frameSettings.IsEnabled(EFrameSetting.VolumetricClouds) && VolumeComponentActive(volumes.volumetricCloud))
            {
                features.Request(EFrameFeature.VolumetricCloud);
            }

            var support = new FeatureSupportContext
            {
                resourceReasons = resources.featureResourceReasons,
                temporal = temporal,
                hasDiffusionProfiles = m_Settings.hasDiffusionProfiles,
                rayTracedOcclusionReady = rtaoReason == null,
                rayTracedOcclusionReason = rtaoReason,
            };
            FeatureRegistry.Resolve(features, support);
            InfinityDebugDisplaySettings.current.rendering.lastFeatures = features;
            InfinityDebugDisplaySettings.current.rendering.lastFrameSettings = frameSettings;
            InfinityDebugDisplaySettings.current.rendering.hasFrameSettings = true;
            return support;
        }

        // Second phase, after culling and LightContext.Build: features whose recording depends on scene content.
        void ConfigureContentFeatures(in CameraSetup setup)
        {
            FrameFeatureSet features = setup.state.features;
            if (renderContext.lightContext.HasLocalLightList())
            {
                features.Request(EFrameFeature.LocalLightList);
            }
            LightContext lightContext = renderContext.lightContext;
            if (setup.frameSettings.IsEnabled(EFrameSetting.ContactShadows)
                && (lightContext.SunHasContactShadow || lightContext.ContactLightCount > 0)
                && VolumeComponentActive(setup.volumes.contactShadow))
            {
                features.Request(EFrameFeature.ContactShadow);
            }
            if (lightContext.CookieCount > 0)
            {
                features.Request(EFrameFeature.LightCookies);
            }
            if (lightContext.ReflectionProbeCount > 0)
            {
                features.Request(EFrameFeature.ReflectionProbes);
            }
            ShadowAllocator shadowAllocator = renderContext.lightContext.ShadowAllocator;
            if (shadowAllocator.CascadeUsesMoments || shadowAllocator.LocalMomentSliceCount > 0)
            {
                features.Request(EFrameFeature.ShadowMoments);
            }
            FeatureRegistry.Resolve(features, setup.featureSupport);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnEntryPlayEditor()
        {
            RenderTaskQueue.ClearTasks();
        }
#endif
    }
}
