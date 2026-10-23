using System;
using UnityEngine;
using UnityEngine.VFX;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Component;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.Feature;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.PostProcess;
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
        private static readonly int ID_FrameIndex = Shader.PropertyToID("FrameIndex");
        private static readonly int ID_TAAJitter = Shader.PropertyToID("TAAJitter");
        private static readonly int ID_Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");
        private static readonly int ID_Matrix_ViewToWorld = Shader.PropertyToID("Matrix_ViewToWorld");
        private static readonly int ID_Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
        private static readonly int ID_Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
        private static readonly int ID_Matrix_JitterProj = Shader.PropertyToID("Matrix_JitterProj");
        private static readonly int ID_Matrix_InvJitterProj = Shader.PropertyToID("Matrix_InvJitterProj");
        private static readonly int ID_Matrix_FlipYProj = Shader.PropertyToID("Matrix_FlipYProj");
        private static readonly int ID_Matrix_InvFlipYProj = Shader.PropertyToID("Matrix_InvFlipYProj");
        private static readonly int ID_Matrix_FlipYJitterProj = Shader.PropertyToID("Matrix_FlipYJitterProj");
        private static readonly int ID_Matrix_InvFlipYJitterProj = Shader.PropertyToID("Matrix_InvFlipYJitterProj");
        private static readonly int ID_Matrix_ViewProj = Shader.PropertyToID("Matrix_ViewProj");
        private static readonly int ID_Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
        private static readonly int ID_Matrix_ViewFlipYProj = Shader.PropertyToID("Matrix_ViewFlipYProj");
        private static readonly int ID_Matrix_InvViewFlipYProj = Shader.PropertyToID("Matrix_InvViewFlipYProj");
        private static readonly int ID_Matrix_ViewJitterProj = Shader.PropertyToID("Matrix_ViewJitterProj");
        private static readonly int ID_Matrix_InvViewJitterProj = Shader.PropertyToID("Matrix_InvViewJitterProj");
        private static readonly int ID_Matrix_ViewFlipYJitterProj = Shader.PropertyToID("Matrix_ViewFlipYJitterProj");
        private static readonly int ID_Matrix_InvViewFlipYJitterProj = Shader.PropertyToID("Matrix_InvViewFlipYJitterProj");
        private static readonly int ID_LastFrameIndex = Shader.PropertyToID("Prev_FrameIndex");
        private static readonly int ID_Matrix_LastViewProj = Shader.PropertyToID("Matrix_LastViewProj");
        private static readonly int ID_Matrix_LastViewFlipYProj = Shader.PropertyToID("Matrix_LastViewFlipYProj");
        private static readonly int ID_Matrix_LastViewJitterProj = Shader.PropertyToID("Matrix_LastViewJitterProj");
        private static readonly int ID_Matrix_LastViewFlipYJitterProj = Shader.PropertyToID("Matrix_LastViewFlipYJitterProj");

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
        public bool historyReset;
        public Vector2 previousJitterUV;
        private Vector3 m_PreparedPosition;
        private Quaternion m_PreparedRotation;
        private Matrix4x4 m_PreparedProjection, m_LastProjection;
        private int m_CommittedFrames;
        private bool m_HasPreparedView;

        private bool m_HasLastView;
        private Vector3 m_LastCameraPosition;
        private Quaternion m_LastCameraRotation;
        private const float HistoryCutPosition = 2.0f;
        private const float HistoryCutAngle = 20.0f;

        internal void UpdateCurrFrameData(Camera camera, int2 renderSize, bool forceHistoryReset = false)
        {
            if (!m_HasLastView)
            {
                historyReset = true;
            }
            else
            {
                float positionDelta = Vector3.Distance(camera.transform.position, m_LastCameraPosition);
                float angleDelta = Quaternion.Angle(camera.transform.rotation, m_LastCameraRotation);
                historyReset = positionDelta > HistoryCutPosition || angleDelta > HistoryCutAngle || camera.projectionMatrix != m_LastProjection;
            }

            if (forceHistoryReset)
            {
                historyReset = true;
            }

            m_HasPreparedView = true;
            m_PreparedPosition = camera.transform.position;
            m_PreparedRotation = camera.transform.rotation;
            m_PreparedProjection = camera.projectionMatrix;
            frameIndex = m_CommittedFrames % 8;
            matrix_WorldToView = camera.worldToCameraMatrix;
            matrix_ViewToWorld = matrix_WorldToView.inverse;
            matrix_Proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            matrix_FlipYProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            bool applyJitter = CameraRenderPolicy.For(camera.cameraType).temporal && !historyReset;
            TemporalJitter.CalculateProjectionMatrix(camera, renderSize, 0.75f, frameIndex, ref jitter, ref matrix_JitterProj, ref matrix_FlipYJitterProj, applyJitter: applyJitter);
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
            if (historyReset)
            {
                // No previous camera sample exists after a reset. Seed the upload without
                // committing last-camera state; successful frame completion still owns that.
                lastJitter = jitter; lastFrameIndex = frameIndex;
                previousJitterUV = InfinityRenderPipeline.ProjectionJitterUV(matrix_FlipYJitterProj, matrix_FlipYProj);
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
            previousJitterUV = InfinityRenderPipeline.ProjectionJitterUV(matrix_FlipYJitterProj, matrix_FlipYProj);
            m_CommittedFrames++;
            m_HasLastView = true;
        }

        public void SetUniformData(CommandBuffer cmdBuffer, int2 renderSize)
        {
            float2 resolution = renderSize;
            cmdBuffer.SetGlobalVector("_ScreenParams", new Vector4(resolution.x, resolution.y, 1f + 1f / resolution.x, 1f + 1f / resolution.y));
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
        private MeshWorld m_MeshWorld;
        private RGScoper m_RGScoper;
        private RGBuilder m_RGBuilder;
        private ResourcePool m_ResourcePool;
        private Dictionary<int, CameraFrameState> m_CameraStates;
        private readonly List<int> m_CameraStateRecycleIds = new List<int>(8);
        private Dictionary<int, ProfilingSampler> m_CameraSamplers;
        private CameraUniform m_CameraUniform;
        private CameraFrameState m_ActiveFrameState;
        private GraphicsBuffer m_DiffusionProfileBuffer;
        private int m_DiffusionProfileCapacity;
        private AtmosphereSharedCache m_AtmosphereSharedCache;
        private LocalShadowCache m_LocalShadowCache;
        private LightCookieAtlas m_LightCookieAtlas;
        private ProbeVolumeIntegration m_ProbeVolumes;
        private ReflectionProbeAtlas m_ReflectionProbeAtlas;
        SupportedRenderingFeatures m_PreviousSupportedFeatures;
        string m_PreviousGlobalRenderPipeline;
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

        InfinityRenderPipelineAsset m_PipelineAsset;
        InfinityRenderPipelineGlobalSettings m_GlobalSettings;
        internal InfinityRenderPipelineResources resources;
        internal InfinityRenderPipelineRuntimeShaders shaders => resources.shaders;
        public override RenderPipelineGlobalSettings defaultSettings => m_GlobalSettings;
        internal RenderContext renderContext;
        internal InfinityRenderPipelineAsset pipelineAsset
        {
            get
            {
                return m_PipelineAsset;
            }
        }

        public InfinityRenderPipeline(InfinityRenderPipelineAsset asset)
        {
            m_PipelineAsset = asset;
            m_GlobalSettings = InfinityRenderPipelineGlobalSettings.Require();
            resources = new InfinityRenderPipelineResources();
            if (asset == null || asset.atmosphericalProfile == null)
                throw new ArgumentException("InfinityRP requires a pipeline asset with AtmosphericalProfile.", nameof(asset));
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
                renderContext = new RenderContext();
                m_RGBuilder = new RGBuilder("RenderGraph");
                m_RGScoper = new RGScoper(m_RGBuilder);
                m_CameraStates = new Dictionary<int, CameraFrameState>();
                m_CameraSamplers = new Dictionary<int, ProfilingSampler>();
                m_ResourcePool = new ResourcePool();
                m_MeshWorld = new MeshWorld(renderContext.GetMeshScene(), m_ResourcePool);
                m_RGBuilder.SetMeshWorld(m_MeshWorld);
                MeshDrawGPUBackend.SetShader(shaders.meshDrawPipelineCS);
                m_AtmosphereSharedCache = new AtmosphereSharedCache();
                m_LocalShadowCache = new LocalShadowCache();
                m_LightCookieAtlas = new LightCookieAtlas();
                m_ProbeVolumes = new ProbeVolumeIntegration(asset);
                m_ReflectionProbeAtlas = new ReflectionProbeAtlas();
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
            // Begin FrameContext
            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.FrameRendering)))
            {
                renderContext.scriptableRenderContext = scriptableRenderContext;
                m_RGBuilder.RecoverUnjoinedAsync(renderContext);
                RenderCaptureService.Tick();
                RenderFaultValidation.Tick(cameras);
                PostEffectValidation.Tick(cameras);
                VisualLightingValidation.Tick();
                CameraMotionValidation.Tick();

                InvokeProxyUpdate();
                m_MeshWorld.UpdateResidency();
                CommandBuffer cmdBuffer = CommandBufferPool.Get();
                cmdBuffer.SetExecutionFlags(CommandBufferExecutionFlags.None);
                cmdBuffer.Clear();

                Exception firstCameraException = null;
                bool contextBegan = false;
                bool submitted = false;
                try
                {
                    BeginContextRendering(scriptableRenderContext, cameras);
                    contextBegan = true;
                    m_AtmosphereSharedCache.BeginFrame();
                    m_LocalShadowCache.BeginFrame();
                    m_LightCookieAtlas.BeginFrame();
                    m_ReflectionProbeAtlas.BeginFrame();
                    for (int i = 0; i < cameras.Count; ++i)
                    {
                        Camera camera = cameras[i];
                        InfinityAdditionalCameraData cameraComponent = camera.GetComponent<InfinityAdditionalCameraData>();
                        ResolveVolumeSelection(camera, cameras, cameraComponent, out Transform volumeTrigger, out LayerMask volumeLayerMask);

                        MeshView mainView = default;
                        CullingResults cullingResults;

                        int cameraId = GetCameraID(camera);
                        bool isEditView = camera.cameraType == CameraType.SceneView;
                        CameraRenderPolicy renderPolicy = CameraRenderPolicy.For(camera.cameraType);

                        CameraFrameState frameState = GetOrCreateCameraFrameState(cameraId, out bool newlyCreated);
                        int previousLastSeen = frameState.lastSeenFrame;
                        // Non-temporal cameras (previews, probe faces) reset every frame, which also zeroes jitter.
                        bool forceHistoryReset = !renderPolicy.temporal || frameState.requiresHistoryReset
                            || CameraFrameState.ShouldForceHistoryReset(newlyCreated, previousLastSeen, Time.frameCount, camera.cameraType);
                        frameState.lastSeenFrame = Time.frameCount;
                        frameState.camera = camera;
                        frameState.preparedThisRender = true;
                        frameState.cameraType = camera.cameraType;
                        frameState.renderPolicy = renderPolicy;
                        frameState.frameSettings = FrameSettingsUtility.Resolve(pipelineAsset.defaultFrameSettings, pipelineAsset.reflectionFrameSettings,
                            camera.cameraType == CameraType.Reflection, cameraComponent != null && cameraComponent.customFrameSettings,
                            cameraComponent != null ? cameraComponent.frameSettings : default);
                        frameState.executeSucceeded = false;
                        using (new ProfilingScope(cmdBuffer, GetCameraSampler(camera, cameraComponent)))
                        {
                            BeginCameraRendering(scriptableRenderContext, camera);
                            try
                            {
                            CameraDimensionDescriptor dimensions = CameraDimensionDescriptor.FromCamera(camera, pipelineAsset, cameraComponent);
                            if (math.any(frameState.dimensions.displaySize != dimensions.displaySize) || math.any(frameState.dimensions.internalSize != dimensions.internalSize) || frameState.dimensions.superResolution != dimensions.superResolution)
                            {
                                forceHistoryReset = true;
                                frameState.descriptorGeneration++;

                            }

                            frameState.dimensions = dimensions;
                            frameState.postProcessingEnabled = renderPolicy.postProcessing && CoreUtils.ArePostProcessesEnabled(camera);
                            frameState.sceneLightingEnabled = !CoreUtils.IsSceneLightingDisabled(camera);
                            CameraUniform cameraUniform = frameState.cameraUniform;
                            HistoryCache historyCache = frameState.historyCache;
                            historyCache.BeginFrame();
                            frameState.atmosphereViewCache.BeginFrame();
                            frameState.combineLutCache.BeginFrame();

                            VolumeManager.instance.Update(frameState.volumeStack, volumeTrigger, volumeLayerMask);

                            // CameraRendering
                            cameraUniform.UpdateCurrFrameData(camera, dimensions.internalSize, forceHistoryReset);
                            m_CameraUniform = cameraUniform;
                            m_ActiveFrameState = frameState;
                            RenderCaptureService.current?.PrepareCamera(camera, frameState, pipelineAsset);
                            RenderFaultValidation.BeforeCamera(camera, frameState);
                            frameState.debugView = InfinityDebugDisplaySettings.current.ResolveDebugView(camera);
                            ConfigureFrameFeatures(frameState);
                            // Declare camera outputs before Unity camera setup and culling.
                            if (ShouldRecordFeature(EFrameFeature.Motion))
                                camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
                            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.SetupCamera)))
                            {
                                #if UNITY_EDITOR
                                if (isEditView)
                                {
                                    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                                }
                                #endif

                                cameraUniform.SetUniformData(cmdBuffer, dimensions.internalSize);
                                // Adaptive Probe Volumes: CoreRP-owned resources, bound before culling and recording.
                                m_ProbeVolumes.SetupCamera(cmdBuffer, camera, frameState.volumeStack, renderPolicy.temporal);
                                scriptableRenderContext.SetupCameraProperties(camera);
                                scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                                cmdBuffer.Clear();

                                // ProcessVfx
                                VFXManager.PrepareCamera(camera);

                                // SceneCulling
                                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.CulllingScene)))
                                {
                                    camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParameters);
                                    cullingParameters.shadowDistance = Mathf.Max(0.0f, pipelineAsset.shadowDistance);
                                    cullingParameters.cullingOptions = CullingOptions.ShadowCasters | CullingOptions.NeedsLighting |
                                        CullingOptions.NeedsReflectionProbes | CullingOptions.DisablePerObjectCulling;
                                    cullingResults = scriptableRenderContext.Cull(ref cullingParameters);

                                    m_MeshWorld.BeginCamera(camera);
                                    mainView = MeshView.FromCamera(camera, ref cullingParameters);
                                }

                                // ProcessLOD
                                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ProcessLOD)))
                                {
                                    List<TerrainComponent> terrains = renderContext.GetWorldTerrains();
                                    float4x4 matrix_Proj = TerrainUtility.GetProjectionMatrix(camera.fieldOfView + 30, dimensions.internalSize.x, dimensions.internalSize.y, camera.nearClipPlane, camera.farClipPlane);
                                    for(int j = 0; j < terrains.Count; ++j)
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

                                // ProcessLight
                                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ProcessLight)))
                                {
                                    FShadowAllocatorSettings shadowSettings;
                                    shadowSettings.cascadeMapResolution = pipelineAsset.cascadeShadowMapResolution;
                                    shadowSettings.localMapResolution = pipelineAsset.localShadowMapResolution;
                                    shadowSettings.shadowDistance = pipelineAsset.shadowDistance;
                                    shadowSettings.cascadeRatios = ShadowAllocator.ValidateCascadeRatios(pipelineAsset.cascadeSplitRatios);
                                    shadowSettings.maxLocalLights = Mathf.Clamp(pipelineAsset.maxShadowedLocalLights, 1, 64);
                                    shadowSettings.cascadeCount = Mathf.Clamp(pipelineAsset.cascadeCount, 1, ShadowAllocator.CascadeCount);
                                    shadowSettings.cascadeBlendWidth = pipelineAsset.cascadeBlendWidth;
                                    shadowSettings.filterGuardTexels = pipelineAsset.ResolveShadowFilter().pcfRadius + 1;
                                    m_LocalShadowCache.Configure(Mathf.Max(0, pipelineAsset.cachedShadowAtlasWidth),
                                        pipelineAsset.localShadowMapResolution, Mathf.Max(1, pipelineAsset.localShadowMapResolution / 4));
                                    if (m_LocalShadowCache.SlotCount > 0) m_LocalShadowCache.GetAtlas();
                                    renderContext.lightContext.Build(cullingResults, renderContext.GetWorldLight(), camera, shadowSettings, frameState.dimensions.internalSize, m_MeshWorld, m_LocalShadowCache);
                                    m_LightCookieAtlas.Configure(pipelineAsset.cookieAtlasResolution, pipelineAsset.cookieMaxSize);
                                    // A camera with cookies / probes switched off resolves no atlas entries (records carry no cookie index).
                                    renderContext.lightContext.ResolveCookies(frameState.frameSettings.IsEnabled(EFrameSetting.LightCookies) ? m_LightCookieAtlas : null,
                                        cullingResults, renderContext.GetWorldLight(), Time.frameCount);
                                    m_ReflectionProbeAtlas.Configure(pipelineAsset.reflectionProbeResolution, pipelineAsset.maxReflectionProbes);
                                    renderContext.lightContext.ResolveReflectionProbes(frameState.frameSettings.IsEnabled(EFrameSetting.ReflectionProbes) ? m_ReflectionProbeAtlas : null,
                                        cullingResults, Time.frameCount);
                                    ConfigureContentFeatures(frameState);
                                    InfinityDebugDisplaySettingsLighting lightingDebug = InfinityDebugDisplaySettings.current.lighting;
                                    lightingDebug.lastDirectionalCount = renderContext.lightContext.DirectionalLightCount;
                                    lightingDebug.lastLocalCount = renderContext.lightContext.LocalLightCount;
                                    lightingDebug.lastReflectionProbeCount = renderContext.lightContext.ReflectionProbeCount;
                                    lightingDebug.lastUnsupportedShapeCount = renderContext.lightContext.UnsupportedShapeCount;
                                    lightingDebug.lastDistanceCulledCount = renderContext.lightContext.DistanceCulledCount;

                                    scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                                    cmdBuffer.Clear();
                                }

                                // ProcessVfx Command
                                VFXCameraXRSettings cameraXRSettings;
                                {
                                    cameraXRSettings.viewTotal = 1;
                                    cameraXRSettings.viewCount = 1;
                                    cameraXRSettings.viewOffset = 0;
                                }
                                VFXManager.ProcessCameraCommand(camera, cmdBuffer, cameraXRSettings, cullingResults);
                                scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                                cmdBuffer.Clear();
                            }

                            #region PostProcessVolume Parameter
                            VolumeStack volumeStack = frameState.volumeStack;

                            FilmTonemap filmTonemapVolume = volumeStack.GetComponent<FilmTonemap>();
                            ColorGrading colorGradingVolume = volumeStack.GetComponent<ColorGrading>();
                            CombineLutParameterDescriptor combineLutParameterDescriptor = CombineLutParameterUtility.FromVolumeStack(
                                filmTonemapVolume,
                                colorGradingVolume);
                            float3 ColorTransform = new float3(0.0f, 0.5f, 1.0f);
                            {
                                // x is the input value, y the output value
                                // RGB = a, b, c where y = a * x*x + b * x + c

                                float c = ColorTransform.x;
                                float b = 4 * ColorTransform.y - 3 * ColorTransform.x - ColorTransform.z;
                                float a = ColorTransform.z - ColorTransform.x - b;

                                combineLutParameterDescriptor.MappingPolynomial.x = a;
                                combineLutParameterDescriptor.MappingPolynomial.y = b;
                                combineLutParameterDescriptor.MappingPolynomial.z = c;
                                combineLutParameterDescriptor.MappingPolynomial.w = 1;
                            }

                            OutputTransformDecision outputDecision = OutputTransformUtility.ResolveFromHardware(
                                pipelineAsset.outputMode,
                                pipelineAsset.hdrEncoding,
                                camera,
                                frameState.hasResolvedBackbufferFormat,
                                frameState.lastResolvedBackbufferFormat);
                            frameState.lastResolvedBackbufferFormat = outputDecision.backbufferFormat;
                            frameState.hasResolvedBackbufferFormat = outputDecision.backbufferFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                            if (!frameState.loggedOutputDecision)
                            {
                                Debug.Log($"[InfinityRP] Present camera={camera.name} type={camera.cameraType} format={outputDecision.backbufferFormat} colorSpace={outputDecision.colorSpace} policy={outputDecision.policy} authority={(outputDecision.displayTransferAuthority ? "DisplayTransferCapability" : "TextureFormat")}");
                                frameState.loggedOutputDecision = true;
                            }
                            combineLutParameterDescriptor.OutputGamut = outputDecision.outputGamut;
                            combineLutParameterDescriptor.OutputDevice = outputDecision.outputDevice;
                            combineLutParameterDescriptor.OutputMode = (int)outputDecision.mode;
                            combineLutParameterDescriptor.HDREncoding = (int)outputDecision.hdrEncoding;

                            float DisplayGamma = 2.2f;
                            combineLutParameterDescriptor.InverseGamma.x = 1.0f / DisplayGamma;
                            combineLutParameterDescriptor.InverseGamma.y = 2.2f / DisplayGamma;
                            combineLutParameterDescriptor.InverseGamma.z = 1.0f / math.max(0, 1.0f);
                            combineLutParameterDescriptor.InverseGamma.w = 0.0f;

                            combineLutParameterDescriptor.ColorShadowTint2 = new float4(0, 0, 0, 1);
                            #endregion PostProcessVolume Parameter

                                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.RecordRG)))
                                {
                                    // PHASE 0: frame-constant async. Zero RG-resource inputs; submit first
                                    // so the whole geometry raster window can overlap them.
                                    UploadLightData(renderContext.lightContext);
                                    ComputeLightCookies(renderContext);
                                    ComputeReflectionProbes(renderContext);
                                    ComputeCombineLuts(frameState, combineLutParameterDescriptor);
                                    ComputeAtmosphericLUT(renderContext, camera);

                                    // PHASE 1: geometry raster (shared depth attachment chain).
                                    RecordMeshGpuCulls(renderContext, camera, mainView, cullingResults);
                                    RenderDepth(renderContext, camera, mainView, cullingResults);
                                    RenderDBuffer(renderContext, camera, cullingResults);
                                    RenderGBuffer(renderContext, camera, mainView, cullingResults);
                                    RenderMotion(renderContext, camera, mainView, cullingResults);

                                    // PHASE 2: depth-derived async. ZBin stays here so fog/deferred can consume it.
                                    ComputeHiZ(renderContext, camera);
                                    ComputeHalfResDownsample(renderContext, camera);
                                    ComputeLightTileMask(renderContext, camera);

                                    // PHASE 3: shadow raster (longest ROP window).
                                    // Unity 6 CreateShadowRendererList is empty until CullShadowCasters runs.
                                    RecordAllocatedShadowCasterSplits(renderContext, cullingResults);
                                    FlushShadowCasterCulling(scriptableRenderContext, cullingResults);
                                    RenderCascadeShadow(renderContext, camera, cullingResults);
                                    RenderLocalShadow(renderContext, camera, cullingResults);
                                    ComputeShadowMoments(renderContext);

                                    // PHASE 4: reserved. VolFog moved after T0 depth so it can use CSM + ZBin.

                                    // PHASE 5: screen-space after HiZ / HalfRes (GTAO / Contact only)
                                    ComputeRayTracedOcclusion(renderContext, camera, historyCache);
                                    ComputeGroundTruthOcclusion(renderContext, camera, historyCache);
                                    CopyHistoryOcclusion(renderContext, historyCache, camera);
                                    ComputeContactShadow(renderContext, camera);

                                    // PHASE 6: DeferredBase → Forward → OpaqueLightingPyramid → SSR/SSGI → Composite → OpaqueSceneColor
                                    ComputeDeferredShading(renderContext, camera);
                                    RecordDeferredLightingCapture();
                                    RenderForward(renderContext, camera, mainView, cullingResults);
                                    ComputeBurleySubsurface(renderContext, camera);
                                    RenderAtmosphericSkyAndFog(renderContext, camera);
                                    RecordLightingCapture();
                                    ComputeOpaqueLightingPyramid(renderContext, camera);
                                    ComputeScreenSpaceReflection(renderContext, camera, historyCache);
                                    CopyHistorySSR(renderContext, historyCache, camera);
                                    ComputeScreenSpaceIndirect(renderContext, camera, historyCache);
                                    CopyHistorySSGI(renderContext, historyCache, camera);
                                    ComputeScreenSpaceComposite(renderContext, camera);
                                    ResolveOpaqueSceneColor();

                                    // PHASE 7: T0 depth → Cloud/Fog → FogComposite → FoggedSceneColor → T0 → RefractionPyramid → T1 → T2
                                    RenderTranslucentDepth(renderContext, camera, cullingResults);
                                    EnsureReactiveMask(camera);
                                    ComputeVolumetricCloud(renderContext, camera, historyCache);
                                    CopyHistoryVolumetricCloud(historyCache, camera);
                                    ComputeVolumetricFog(renderContext, camera, historyCache);
                                    CopyHistoryVolumetricFog(historyCache, camera);
                                    ComputeFogComposite(renderContext, camera);
                                    ResolveFoggedSceneColor();
                                    RenderTranslucentT0(renderContext, camera, cullingResults);
                                    ComputeColorPyramid(renderContext, camera);
                                    RenderTranslucentT1(renderContext, camera, cullingResults);
                                    RenderTranslucentT2(renderContext, camera, cullingResults);

                                    // PHASE 8: temporal resolve + post (TAA after fog/translucents)
                                    // Previews and probe faces have no temporal resolve; post reads FoggedSceneColor.
                                    if (renderPolicy.temporal && frameState.dimensions.superResolution)
                                    {
                                        ComputeSuperResolution(renderContext, camera, historyCache, cameraUniform.jitter);
                                        CopyHistorySuperResolution(renderContext, historyCache, camera);
                                    }
                                    else if (renderPolicy.temporal)
                                    {
                                        ComputeAntiAliasing(renderContext, camera, historyCache, cameraUniform);
                                        CopyHistoryAntiAliasing(renderContext, historyCache, camera);
                                        CopyHistoryDepth(renderContext, historyCache, camera);
                                    }
                                    ComputePostProcessing(renderContext, camera, frameState);
                                    ComputeDebugView(renderContext, camera);
                                #if UNITY_EDITOR
                                    ResolveDisplayDepth();
                                    RenderWireOverlay(renderContext, camera);
                                    RenderGizmos(renderContext, camera);
                                #endif
                                    ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.Display);
                                    m_RGScoper.RegisterTexture(InfinityShaderIDs.DisplayColorBuffer, m_RGScoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer));
                                    MarkFeatureProduced(EFrameFeature.Display);
                                    RenderUIOverlay(renderContext, camera);
                                    RecordNormalFrameCapture(camera);
                                    frameState.features.EnsureRequiredProducers(renderPolicy.temporal && !frameState.dimensions.superResolution);
                                    RenderPresent(renderContext, camera, outputDecision);
                                }

                                using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ExecuteRG)))
                                {
                                    // ReleaseAllDrawLists releases per-Declare visibility refs (exception-safe).
                                    m_RGBuilder.debugLabel = RGDebugSession.captureEnabled ? camera.name : null;
                                    m_RGBuilder.Execute(renderContext, m_ResourcePool, cmdBuffer);
                                    frameState.executeSucceeded = true;
                                    RenderCaptureService.current?.CameraExecuted(camera, frameState.executeSucceeded);
                                    RenderFaultValidation.CameraResult(camera, true);
                                }
                            }
                            catch (Exception exception)
                            {
                                RenderCaptureService.current?.CameraExecuted(camera, false, exception);
                                RenderFaultValidation.CameraResult(camera, false, exception);
                                frameState.RollbackFrame();
                                m_AtmosphereSharedCache.DiscardUnproducedPending();
                                // A queue failure may follow an accepted write but precede OnQueued.
                                // Keep its pending state so frame completion invalidates uncertain contents.
                                if (!m_RGBuilder.HasQueueSubmissionFailure)
                                {
                                    m_LightCookieAtlas.DiscardUnproducedPending();
                                    m_ReflectionProbeAtlas.DiscardUnproducedPending();
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
                                m_ShadowCasterSplits.Clear();
                                m_ActiveFrameState = null;
                            }
                            EndCameraRendering(scriptableRenderContext, camera);
                        }

                        m_RGScoper.Clear();

                    }

                }
                catch (Exception exception)
                {
                    if (firstCameraException == null) firstCameraException = exception;
                }

                bool frameValid = FrameSubmission.Execute(new ContextFrameSubmission(scriptableRenderContext),
                    cmdBuffer, ref firstCameraException, out submitted);

                try
                {
                    frameValid &= m_RGBuilder.CanRetireSubmittedResources;
                    CompleteFrameTransaction(m_AtmosphereSharedCache, m_CameraStates, frameValid, Time.frameCount);
                    if (frameValid) { m_LocalShadowCache.CommitFrame(); m_LightCookieAtlas.CommitFrame(); m_ReflectionProbeAtlas.CommitFrame(); }
                    else { m_LocalShadowCache.RollbackPending(); m_LightCookieAtlas.RollbackPending(); m_ReflectionProbeAtlas.RollbackPending(); }
                    if (!frameValid)
                        RenderCaptureService.current?.Fail(firstCameraException?.ToString() ?? "Frame submission failed.");
                    if (submitted)
                    {
                        RenderCaptureService.current?.AfterSubmit();
                        if (m_RGBuilder.CanRetireSubmittedResources)
                        {
                            FlushFrameRetirement();
                            RecycleUnseenCameraStates(Time.frameCount);
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (firstCameraException == null) firstCameraException = exception;
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
                        m_ActiveFrameState = null;
                        m_RGScoper.Clear();
                        try { m_MeshWorld?.ClearResidency(); }
                        catch (Exception error) { if (firstCameraException == null) firstCameraException = error; }
                        try { cmdBuffer.Clear(); }
                        catch (Exception error) { if (firstCameraException == null) firstCameraException = error; }
                        try { CommandBufferPool.Release(cmdBuffer); }
                        catch (Exception error) { if (firstCameraException == null) firstCameraException = error; }
                    }
                }

                RenderFaultValidation.current?.EndFrame(m_CameraStates, submitted, firstCameraException,
                    m_RGBuilder.RetiredResourceCount, renderContext.lightContext.RetiredBufferCount, m_RGBuilder.CanRetireSubmittedResources);
                if (firstCameraException != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstCameraException).Throw();
            }
        }

        internal static void CompleteFrameTransaction(AtmosphereSharedCache shared, Dictionary<int, CameraFrameState> states,
            bool submitted, int frame)
        {
            if (submitted) shared.CommitFrame();
            else shared.RollbackPending();
            foreach (CameraFrameState state in states.Values)
            {
                if (submitted && state.executeSucceeded) state.CommitFrame();
                else if (state.preparedThisRender) state.RollbackFrame();
            }
        }

        void FlushFrameRetirement()
        {
            m_RGBuilder.FlushRetiredResources();
            renderContext.lightContext.FlushRetiredBuffers();
            m_AtmosphereSharedCache.FlushRetired();
            m_LocalShadowCache.FlushRetired();
            m_LightCookieAtlas.FlushRetired();
            m_ReflectionProbeAtlas.FlushRetired();
            foreach (CameraFrameState state in m_CameraStates.Values)
            {
                state.historyCache.FlushRetired();
                state.atmosphereViewCache.FlushRetired();
                state.combineLutCache.FlushRetired();
            }
            m_MeshWorld?.FlushRetired();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected float GetMipmapBiasOffset(in int renderWidth, in int displayWidth)
        {
            return math.log2((float)renderWidth / displayWidth) - 1.0f;
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
            m_PreviousBlitMaterial = GraphicsUtility.m_BlitMaterial;
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
            if (GraphicsUtility.m_BlitMaterial == m_InstalledBlitMaterial)
                GraphicsUtility.m_BlitMaterial = m_PreviousBlitMaterial;
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
                    resolved = game.GetComponent<InfinityAdditionalCameraData>();
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

        public VolumeStack GetVolumeStack(Camera camera)
        {
            if (camera == null || !m_CameraStates.TryGetValue(GetCameraID(camera), out CameraFrameState state))
            {
                return null;
            }

            return state.volumeStack;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SetGraphicsSetting()
        {
            // Resolve dependencies and allocate the feature declaration before any global write.
            m_InstalledBatching = m_PipelineAsset.enableSRPBatch;
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
                overridesLightProbeSystem = m_PipelineAsset.supportProbeVolume,
                skyOcclusion = m_PipelineAsset.supportProbeVolume,
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
                rendersUIOverlay = true
            };
            m_InstalledSupportedFeatures = installedFeatures;
            s_GlobalStateOwner = this;
            SupportedRenderingFeatures.active = installedFeatures;
            Shader.globalRenderPipeline = "InfinityRenderPipeline";

            GraphicsUtility.m_BlitMaterial = m_InstalledBlitMaterial;

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
            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ProxyUpdate)))
            {
                FGraphics.ProcessTasks(renderContext);
                FGraphics.ClearTasks();
                renderContext.InitializeWorldDecals();
                renderContext.InterpolateMeshProbes();

                #if UNITY_EDITOR
                    InvokeProxyUpdateEditor();
                #else
                    InvokeProxyUpdateRuntime();
                #endif

                // EventUpdate emits transform tasks after registration was drained.
                // Apply those poses before residency upload and per-view history snapshots.
                FGraphics.ProcessTasks(renderContext);
                FGraphics.ClearTasks();

                // After proxy EventUpdate (Dynamic may MarkDirty); drain applies SyncFromSnapshot same frame.
                renderContext.DrainDirtyMeshes();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void InvokeProxyUpdateEditor()
        {
            if(pipelineAsset.updateProxy)
            {
                pipelineAsset.updateProxy = false;
                renderContext.InvokeWorldStaticMeshUpdate();
            }

            renderContext.InvokeWorldDynamicMeshUpdate();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void InvokeProxyUpdateRuntime()
        {
            if(m_UpdateInit == true)
            {
                m_UpdateInit = false;
                renderContext.InvokeWorldStaticMeshUpdate();
            }

            renderContext.InvokeWorldDynamicMeshUpdate();
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
            if (ownsSharedResources) Release(() => RenderCaptureService.current?.Cancel("PipelineDisposed"));
            Release(() => renderContext?.Dispose());
            Release(() => m_MeshWorld?.Dispose());
            if (ownsSharedResources) Release(MeshDrawGPUBackend.Dispose);
            Release(() => m_RGScoper?.Dispose());
            Release(() => m_RGBuilder?.Dispose());
            Release(() => m_ResourcePool?.Dispose());
            if (m_CameraStates != null)
            {
                foreach (CameraFrameState state in m_CameraStates.Values) Release(state.Dispose);
                m_CameraStates.Clear();
            }
            m_CameraSamplers?.Clear();
            if (ownsSharedResources && m_OwnsVolumeManager &&
                ReferenceEquals(VolumeManager.instance.globalDefaultProfile, resources.defaultVolumeProfile) &&
                ReferenceEquals(VolumeManager.instance.qualityDefaultProfile, m_InstalledQualityProfile)) Release(VolumeManager.instance.Deinitialize);
            m_OwnsVolumeManager = false;
            Release(() => m_DiffusionProfileBuffer?.Release());
            m_DiffusionProfileBuffer = null;
            Release(() => m_AtmosphereSharedCache?.Dispose());
            m_AtmosphereSharedCache = null;
            Release(() => m_LocalShadowCache?.Dispose());
            m_LocalShadowCache = null;
            Release(() => m_LightCookieAtlas?.Dispose());
            m_LightCookieAtlas = null;
            Release(() => m_ProbeVolumes?.Dispose());
            m_ProbeVolumes = null;
            Release(() => m_ReflectionProbeAtlas?.Dispose());
            m_ReflectionProbeAtlas = null;
            if (ownsSharedResources) Release(() => InfinityRayTracingEnvironment.current?.Dispose());
            Release(RestoreGraphicsState);
            if (failures.Count > 0) throw new AggregateException("InfinityRP resource disposal failed.", failures);
        }

        static bool VolumeComponentActive(VolumeComponent component)
        {
            return GraphicsUtility.VolumeComponentActive(component);
        }

        VolumeStack ActiveVolumeStack => m_ActiveFrameState.volumeStack;

        FrameFeatureSet ActiveFeatures => m_ActiveFrameState.features;

        FeatureSupportContext m_FeatureSupportContext;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool ShouldRecordFeature(EFrameFeature feature)
        {
            return m_ActiveFrameState.features.ShouldRecord(feature);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void MarkFeatureProduced(EFrameFeature feature)
        {
            m_ActiveFrameState.features.MarkProduced(feature);
        }

        CameraFrameState GetOrCreateCameraFrameState(int cameraId, out bool created)
        {
            created = !m_CameraStates.TryGetValue(cameraId, out CameraFrameState frameState);
            if (created)
            {
                frameState = new CameraFrameState(cameraId);
                m_CameraStates.Add(cameraId, frameState);
            }

            return frameState;
        }

        void RecycleUnseenCameraStates(int frameCount)
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
                    frameState.Dispose();
                    m_CameraStates.Remove(cameraId);
                }
            }
        }

        // States what this camera wants to record. FeatureRegistry.Resolve decides what can record (kernels,
        // dependencies, camera policy), requests dependencies and keeps the reason for anything that cannot.
        void ConfigureFrameFeatures(CameraFrameState frameState)
        {
            FrameFeatureSet features = frameState.features;
            features.Reset();
            VolumeStack stack = frameState.volumeStack;
            bool temporal = frameState.renderPolicy.temporal;
            FResolvedFrameSettings frameSettings = frameState.frameSettings;

            features.Request(EFrameFeature.Depth);
            features.Request(EFrameFeature.GBuffer);
            features.Request(EFrameFeature.Motion);
            features.Request(EFrameFeature.DeferredShading);
            features.Request(EFrameFeature.HiZ);
            features.Request(EFrameFeature.ColorPyramid);
            features.Request(EFrameFeature.PostProcess);
            features.Request(EFrameFeature.Display);
            if (renderContext.WorldDecalCount > 0 && frameSettings.IsEnabled(EFrameSetting.Decals))
            {
                features.Request(EFrameFeature.DBuffer);
            }

            // Without a temporal policy PostProcess reads FoggedSceneColor directly.
            if (temporal)
            {
                features.Request(frameState.dimensions.superResolution ? EFrameFeature.SuperResolution : EFrameFeature.TAA);
            }

            var rtao = stack.GetComponent<RayTracingAmbientOcclusion>();
            InfinityRayTracingEnvironment rayEnv = InfinityRayTracingEnvironment.EnsureOwned();
            rayEnv.SyncGeometry(renderContext.GetMeshScene());
            string rtaoReason = null;
            if (!pipelineAsset.enableRayTrace)
                rtaoReason = "Ray tracing is disabled on the RP Asset.";
            else if (!frameSettings.IsEnabled(EFrameSetting.RayTracedAmbientOcclusion))
                rtaoReason = "Disabled by Frame Settings (" + frameSettings.SourceOf(EFrameSetting.RayTracedAmbientOcclusion) + ").";
            else if (!VolumeComponentActive(rtao))
                rtaoReason = "RTAO Volume is inactive.";
            else if (!InfinityRayTracingEnvironment.CanRecord(pipelineAsset, shaders))
                rtaoReason = string.IsNullOrEmpty(InfinityRayTracingEnvironment.lastBlockReason) ? "UnifiedRayTracing is not ready." : InfinityRayTracingEnvironment.lastBlockReason;
            else if (rayEnv.InstanceCount == 0)
                rtaoReason = "URT accel has no mesh instances.";
            InfinityRayTracingEnvironment.ReportBlockReason(rtaoReason ?? string.Empty);

            // RTAO owns AO when it can record; otherwise GTAO does (if requested).
            if (rtaoReason == null)
            {
                features.Request(EFrameFeature.RTAO);
            }
            else if (frameSettings.IsEnabled(EFrameSetting.AmbientOcclusion) && ScreenSpaceModeUtility.ShouldRequestGTAO(stack.GetComponent<ScreenSpaceAmbientOcclusion>()))
            {
                features.Request(EFrameFeature.GTAO);
            }

            EScreenSpaceMode screenSpaceMode = ScreenSpaceModeUtility.Resolve(
                stack.GetComponent<ScreenSpaceReflection>(),
                stack.GetComponent<ScreenSpaceIndirectDiffuse>());
            if (ScreenSpaceModeUtility.IncludesSSR(screenSpaceMode) && frameSettings.IsEnabled(EFrameSetting.ScreenSpaceReflection))
            {
                features.Request(EFrameFeature.SSR);
            }
            if (ScreenSpaceModeUtility.IncludesSSGI(screenSpaceMode) && frameSettings.IsEnabled(EFrameSetting.ScreenSpaceGlobalIllumination))
            {
                features.Request(EFrameFeature.SSGI);
            }

            if (frameSettings.IsEnabled(EFrameSetting.VolumetricFog) && VolumeComponentActive(stack.GetComponent<VolumetricFog>()))
            {
                features.Request(EFrameFeature.VolumetricFog);
            }
            if (frameSettings.IsEnabled(EFrameSetting.VolumetricClouds) && VolumeComponentActive(stack.GetComponent<VolumetricCloud>()))
            {
                features.Request(EFrameFeature.VolumetricCloud);
            }

            m_FeatureSupportContext = new FeatureSupportContext
            {
                shaders = shaders,
                textures = resources.textures,
                temporal = temporal,
                rayTracedOcclusionReady = rtaoReason == null,
                rayTracedOcclusionReason = rtaoReason,
                presentAvailable = GraphicsUtility.BlitMaterial != null && GraphicsUtility.BlitMaterial.FindPass("Present") >= 0,
            };
            FeatureRegistry.Resolve(features, m_FeatureSupportContext);
            InfinityDebugDisplaySettings.current.rendering.lastFeatures = features;
            InfinityDebugDisplaySettings.current.rendering.lastFrameSettings = frameSettings;
            InfinityDebugDisplaySettings.current.rendering.hasFrameSettings = true;
        }

        // Second phase, after culling and LightContext.Build: features whose recording depends on scene content.
        void ConfigureContentFeatures(CameraFrameState frameState)
        {
            FrameFeatureSet features = frameState.features;
            if (renderContext.lightContext.HasLocalLightList())
            {
                features.Request(EFrameFeature.LocalLightList);
            }
            LightContext lightContext = renderContext.lightContext;
            if (frameState.frameSettings.IsEnabled(EFrameSetting.ContactShadows)
                && (lightContext.SunHasContactShadow || lightContext.ContactLightCount > 0)
                && VolumeComponentActive(frameState.volumeStack.GetComponent<ContactShadow>()))
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
            FeatureRegistry.Resolve(features, m_FeatureSupportContext);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnEntryPlayEditor()
        {
            FGraphics.ClearTasks();
        }

        static void OnSceneChangedEditor(Scene current)
        {
            Debug.Log("OnSceneChangedEditor");
            FGraphics.ClearTasks();
        }
#endif
    }
}
