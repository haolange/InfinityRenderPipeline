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

/*[Serializable]
[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
[UnityEngine.Categorization.CategoryInfo(Name = "Volume", Order = 0)]
public class InfinityRPDefaultVolumeProfileSettings : IDefaultVolumeProfileSettings
{
    #region Version
    internal enum Version : int
    {
        Initial = 0,
    }

    [SerializeField]
    [HideInInspector]
    Version m_Version;

    /// <summary>Current version.</summary>
    public int version => (int)m_Version;
    #endregion

    [SerializeField]
    VolumeProfile m_VolumeProfile;

    /// <summary>
    /// The default volume profile asset.
    /// </summary>
    public VolumeProfile volumeProfile
    {
        get => m_VolumeProfile;
        set => this.SetValueAndNotify(ref m_VolumeProfile, value);
    }
}*/

namespace InfinityTech.Rendering.Pipeline
{
    internal enum EPipelineProfileId
    {
        SetupCamera,
        CulllingScene,
        ProcessLOD,
        ProcessLight,
        BeginFrameRendering,
        EndFrameRendering,
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

        private bool m_HasLastView;
        private Vector3 m_LastCameraPosition;
        private Quaternion m_LastCameraRotation;
        private const float HistoryCutPosition = 2.0f;
        private const float HistoryCutAngle = 20.0f;

        private void UpdateCurrFrameData(Camera camera)
        {
            if (!m_HasLastView)
            {
                historyReset = true;
            }
            else
            {
                float positionDelta = Vector3.Distance(camera.transform.position, m_LastCameraPosition);
                float angleDelta = Quaternion.Angle(camera.transform.rotation, m_LastCameraRotation);
                historyReset = positionDelta > HistoryCutPosition || angleDelta > HistoryCutAngle;
            }

            matrix_WorldToView = camera.worldToCameraMatrix;
            matrix_ViewToWorld = matrix_WorldToView.inverse;
            matrix_Proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            matrix_FlipYProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            TemporalAntiAliasingGenerator.CaculateProjectionMatrix(camera, 0.75f, ref frameIndex, ref jitter, ref matrix_JitterProj, ref matrix_FlipYJitterProj);
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
        }

        private void UpdateLastFrameData(Camera camera)
        {
            lastJitter = jitter;
            lastFrameIndex = frameIndex;
            matrix_LastViewProj = matrix_ViewProj;
            matrix_LastViewFlipYProj = matrix_ViewFlipYProj;
            matrix_LastViewJitterProj = matrix_ViewJitterProj;
            matrix_LastViewFlipYJitterProj = matrix_ViewFlipYJitterProj;
            m_LastCameraPosition = camera.transform.position;
            m_LastCameraRotation = camera.transform.rotation;
            m_HasLastView = true;
        }

        public void UnpateUniformData(Camera camera, in bool bLastFrame = false)
        {
            if(!bLastFrame) 
            {
                UpdateCurrFrameData(camera);
            } else {
                UpdateLastFrameData(camera);
            }
        }

        public void SetUniformData(CommandBuffer cmdBuffer, Camera camera)
        {
            float2 resolution = new float2(camera.pixelWidth, camera.pixelHeight);
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
        private MeshSceneResidency m_MeshSceneResidency;
        private MeshVisibilityShare m_VisibilityShare;
        private RGScoper m_RGScoper;
        private RGBuilder m_RGBuilder;
        private ResourcePool m_ResourcePool;
        private MeshDrawPipeline m_DepthMeshProcessor;
        private MeshDrawPipeline m_GBufferMeshProcessor;
        private MeshDrawPipeline m_ForwardMeshProcessor;
        private MeshDrawPipeline m_MotionMeshProcessor;
        private MeshDrawPipeline m_ShadowMeshProcessor;
        private Dictionary<int, CameraFrameState> m_CameraStates;
        private readonly List<int> m_CameraStateRecycleIds = new List<int>(8);
        private Dictionary<int, ProfilingSampler> m_CameraSamplers;
        private CameraUniform m_CameraUniform;
        private CameraFrameState m_ActiveFrameState;
        private int m_ActiveCascadeCount;
        private readonly Matrix4x4[] m_ActiveCascadeMatrices = new Matrix4x4[4];
        private Vector4 m_ActiveCascadeSplitDistances;
        private GraphicsBuffer m_DiffusionProfileBuffer;
        private int m_DiffusionProfileCapacity;

        internal RenderContext renderContext;
        internal InfinityRenderPipelineAsset pipelineAsset 
        { 
            get 
            { 
                return (InfinityRenderPipelineAsset)GraphicsSettings.currentRenderPipeline; 
            }
        }

        public InfinityRenderPipeline(InfinityRenderPipelineAsset asset)
        {
            //EditorSceneManager.sceneUnloaded += OnSceneUnloaded;
            SetGraphicsSetting();
            QualitySettings.antiAliasing = 0;
            RTHandles.Initialize(Screen.width, Screen.height);

            //var defaultVolumeProfileSettings = GraphicsSettings.GetRenderPipelineSettings<InfinityRPDefaultVolumeProfileSettings>();
            VolumeManager.instance.Initialize(null, asset.volumeProfile);

            m_UpdateInit = true;
            renderContext = new RenderContext();
            m_RGBuilder = new RGBuilder("RenderGraph");
            m_RGScoper = new RGScoper(m_RGBuilder);
            m_CameraStates = new Dictionary<int, CameraFrameState>();
            m_CameraSamplers = new Dictionary<int, ProfilingSampler>();
            m_ResourcePool = new ResourcePool();
            m_MeshSceneResidency = new MeshSceneResidency(m_ResourcePool, renderContext.GetMeshScene());
            m_VisibilityShare = new MeshVisibilityShare();
            MeshDrawGPUBackend.SetShader(asset.meshDrawPipelineCS);
            m_DepthMeshProcessor = new MeshDrawPipeline(renderContext.GetMeshScene(), m_MeshSceneResidency, m_ResourcePool);
            m_GBufferMeshProcessor = new MeshDrawPipeline(renderContext.GetMeshScene(), m_MeshSceneResidency, m_ResourcePool);
            m_ForwardMeshProcessor = new MeshDrawPipeline(renderContext.GetMeshScene(), m_MeshSceneResidency, m_ResourcePool);
            m_MotionMeshProcessor = new MeshDrawPipeline(renderContext.GetMeshScene(), m_MeshSceneResidency, m_ResourcePool);
            m_ShadowMeshProcessor = new MeshDrawPipeline(renderContext.GetMeshScene(), m_MeshSceneResidency, m_ResourcePool);
        }

        protected override void Render(ScriptableRenderContext scriptableRenderContext, List<Camera> cameras)
        {
            // Begin FrameContext
            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.FrameRendering)))
            {
                renderContext.scriptableRenderContext = scriptableRenderContext;

                InvokeProxyUpdate();
                m_MeshSceneResidency.Update();
                CommandBuffer cmdBuffer = CommandBufferPool.Get();
                cmdBuffer.Clear();
                
                BeginContextRendering(scriptableRenderContext, cameras);
                Exception firstCameraException = null;
                for (int i = 0; i < cameras.Count; ++i)
                {
                    Camera camera = cameras[i];
                    CameraComponent cameraComponent = camera.GetComponent<CameraComponent>();

                    MeshVisibilityHandle sharedVisibility = MeshVisibilityHandle.Invalid;
                    CullingResults cullingResults;

                    int cameraId = GetCameraID(camera);
                    bool isEditView = camera.cameraType == CameraType.SceneView;
                    bool isSceneView = camera.cameraType == CameraType.Game || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.SceneView;

                    CameraFrameState frameState = GetOrCreateCameraFrameState(cameraId);
                    frameState.lastSeenFrame = Time.frameCount;
                    frameState.executeSucceeded = false;
                    if (frameState.pixelWidth != camera.pixelWidth || frameState.pixelHeight != camera.pixelHeight)
                    {
                        frameState.descriptorGeneration++;
                        frameState.pixelWidth = camera.pixelWidth;
                        frameState.pixelHeight = camera.pixelHeight;
                    }

                    CameraUniform cameraUniform = frameState.cameraUniform;
                    HistoryCache historyCache = frameState.historyCache;
                    historyCache.BeginFrame();

                    Transform volumeTrigger = (cameraComponent != null && cameraComponent.volumeTrigger != null)
                        ? cameraComponent.volumeTrigger
                        : camera.transform;
                    LayerMask volumeLayerMask = cameraComponent != null ? cameraComponent.volumeLayerMask : ~0;
                    VolumeManager.instance.Update(frameState.volumeStack, volumeTrigger, volumeLayerMask);

                    // CameraRendering
                    cameraUniform.UnpateUniformData(camera, false);
                    m_CameraUniform = cameraUniform;
                    m_ActiveFrameState = frameState;
                    using (new ProfilingScope(cmdBuffer, GetCameraSampler(camera, cameraComponent)))
                    {
                        BeginCameraRendering(scriptableRenderContext, camera);
                        try
                        {
                        ConfigureFrameFeatures(frameState);
                        using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.SetupCamera)))
                        {
                            #if UNITY_EDITOR
                            if (isEditView) 
                            { 
                                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera); 
                            }
                            #endif

                            cameraUniform.SetUniformData(cmdBuffer, camera);
                            scriptableRenderContext.SetupCameraProperties(camera);
                            scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                            cmdBuffer.Clear();

                            // ProcessVfx
                            VFXManager.PrepareCamera(camera);

                            // SceneCulling
                            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.CulllingScene)))
                            {
                                camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParameters);
                                cullingParameters.shadowDistance = 128;
                                cullingParameters.cullingOptions = CullingOptions.ShadowCasters | CullingOptions.NeedsLighting | CullingOptions.DisablePerObjectCulling;
                                cullingResults = scriptableRenderContext.Cull(ref cullingParameters);

                                MeshScene meshScene = renderContext.GetMeshScene();
                                m_VisibilityShare.BeginFrame(meshScene.VisibilityRevision);
                                ulong viewKey = MeshVisibilityShare.MakeCameraViewKey(camera);
                                // Main camera frustum: Depth/GBuffer/Forward/Motion share one Cull.
                                sharedVisibility = m_VisibilityShare.Acquire(
                                    meshScene,
                                    viewKey,
                                    ref cullingParameters,
                                    MeshVisibilityShare.PolicyMainFrustum,
                                    isSceneView);
                            }

                            // ProcessLOD
                            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ProcessLOD)))
                            {
                                List<TerrainComponent> terrains = renderContext.GetWorldTerrains();
                                float4x4 matrix_Proj = TerrainUtility.GetProjectionMatrix(camera.fieldOfView + 30, camera.pixelWidth, camera.pixelHeight, camera.nearClipPlane, camera.farClipPlane);
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
                                shadowSettings.cascadeRatios = new Vector3(0.067f, 0.2f, 0.467f);
                                shadowSettings.maxLocalLights = 16;
                                renderContext.lightContext.Build(cullingResults, renderContext.GetWorldLight(), camera, shadowSettings);
                                renderContext.lightContext.SetLightData(cmdBuffer);

                                ShadowAllocator shadowAllocator = renderContext.lightContext.ShadowAllocator;
                                m_ActiveCascadeCount = shadowAllocator.CascadeAllocatedCount;
                                m_ActiveCascadeSplitDistances = shadowAllocator.CascadeSplitDistances;
                                for (int cascade = 0; cascade < ShadowAllocator.CascadeCount; ++cascade)
                                {
                                    m_ActiveCascadeMatrices[cascade] = shadowAllocator.CascadeMatrices[cascade];
                                }

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

                        CombineLutParameterDescriptor combineLutParameterDescriptor;
                        {
                            combineLutParameterDescriptor.WhiteTemp = colorGradingVolume.Temp.value;
                            combineLutParameterDescriptor.WhiteTint = colorGradingVolume.Tint.value;

                            combineLutParameterDescriptor.FilmSlope = filmTonemapVolume.Slop.value;
                            combineLutParameterDescriptor.FilmToe = filmTonemapVolume.Toe.value;
                            combineLutParameterDescriptor.FilmShoulder = filmTonemapVolume.Shoulder.value;
                            combineLutParameterDescriptor.FilmBlackClip = filmTonemapVolume.BlackClip.value;
                            combineLutParameterDescriptor.FilmWhiteClip = filmTonemapVolume.WhiteClip.value;

                            combineLutParameterDescriptor.ColorSaturation = colorGradingVolume.ColorSaturation.value;
                            combineLutParameterDescriptor.ColorContrast = colorGradingVolume.ColorContrast.value;
                            combineLutParameterDescriptor.ColorGamma = colorGradingVolume.ColorGamma.value;
                            combineLutParameterDescriptor.ColorGain = colorGradingVolume.ColorGain.value;
                            combineLutParameterDescriptor.ColorOffset = colorGradingVolume.ColorOffset.value;

                            combineLutParameterDescriptor.ColorSaturationShadows = colorGradingVolume.ColorSaturationShadows.value;
                            combineLutParameterDescriptor.ColorContrastShadows = colorGradingVolume.ColorContrastShadows.value;
                            combineLutParameterDescriptor.ColorGammaShadows = colorGradingVolume.ColorGammaShadows.value;
                            combineLutParameterDescriptor.ColorGainShadows = colorGradingVolume.ColorGainShadows.value;
                            combineLutParameterDescriptor.ColorOffsetShadows = colorGradingVolume.ColorOffsetShadows.value;
                            combineLutParameterDescriptor.ColorCorrectionShadowsMax = colorGradingVolume.ShadowsMax.value;

                            combineLutParameterDescriptor.ColorSaturationMidtones = colorGradingVolume.ColorSaturationMidtones.value;
                            combineLutParameterDescriptor.ColorContrastMidtones = colorGradingVolume.ColorContrastMidtones.value;
                            combineLutParameterDescriptor.ColorGammaMidtones = colorGradingVolume.ColorGammaMidtones.value;
                            combineLutParameterDescriptor.ColorGainMidtones = colorGradingVolume.ColorGainMidtones.value;
                            combineLutParameterDescriptor.ColorOffsetMidtones = colorGradingVolume.ColorOffsetMidtones.value;

                            combineLutParameterDescriptor.ColorSaturationHighlights = colorGradingVolume.ColorSaturationHighlights.value;
                            combineLutParameterDescriptor.ColorContrastHighlights = colorGradingVolume.ColorContrastHighlights.value;
                            combineLutParameterDescriptor.ColorGammaHighlights = colorGradingVolume.ColorGammaHighlights.value;
                            combineLutParameterDescriptor.ColorGainHighlights = colorGradingVolume.ColorGainHighlights.value;
                            combineLutParameterDescriptor.ColorOffsetHighlights = colorGradingVolume.ColorOffsetHighlights.value;
                            combineLutParameterDescriptor.ColorCorrectionHighlightsMin = colorGradingVolume.HighlightsMin.value;
                            combineLutParameterDescriptor.ColorCorrectionHighlightsMax = colorGradingVolume.HighlightsMax.value;

                            combineLutParameterDescriptor.BlueCorrection = colorGradingVolume.BlueCorrection.value;
                            combineLutParameterDescriptor.ExpandGamut = colorGradingVolume.ExpandGamut.value;

                            combineLutParameterDescriptor.ColorScale = new float4(1.0f, 1.0f, 1.0f, 0.0f);
                            combineLutParameterDescriptor.OverlayColor = new float4(0, 0, 0, 0);
                        }
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

                        combineLutParameterDescriptor.OutputGamut = 0;
                        combineLutParameterDescriptor.OutputDevice = 0;

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
                                ComputeCombineLuts(renderContext, combineLutParameterDescriptor);
                                ComputeAtmosphericLUT(renderContext, camera);

                                // PHASE 1: geometry raster (shared depth attachment chain).
                                RenderDepth(renderContext, camera, sharedVisibility, cullingResults);
                                RenderDBuffer(renderContext, camera, cullingResults);
                                RenderGBuffer(renderContext, camera, sharedVisibility, cullingResults);
                                RenderMotion(renderContext, camera, sharedVisibility, cullingResults);

                                // PHASE 2: depth-derived async. Ready after Depth; VolCloud does not
                                // read the shadow map, so it can overlap the shadow ROP window.
                                ComputeHiZ(renderContext, camera);
                                ComputeHalfResDownsample(renderContext, camera);
                                ComputeZBinningLightList(renderContext, camera);
                                ComputeVolumetricCloud(renderContext, camera);

                                // PHASE 3: shadow raster (longest ROP window).
                                // Unity 6 CreateShadowRendererList is empty until CullShadowCasters runs.
                                RecordAllocatedShadowCasterSplits(renderContext);
                                FlushShadowCasterCulling(scriptableRenderContext, cullingResults);
                                RenderCascadeShadow(renderContext, camera, cullingResults);
                                RenderLocalShadow(renderContext, camera, cullingResults);

                                // PHASE 4: shadow-dependent async (VolFog reads CascadeShadowMap)
                                ComputeVolumetricFog(renderContext, camera);

                                // PHASE 5: screen-space after HiZ / HalfRes
                                ComputeGroundTruthOcclusion(renderContext, camera);
                                ComputeContactShadow(renderContext, camera);
                                ImportHistoryColorPyramid(camera, historyCache);
                                ComputeScreenSpaceReflection(renderContext, camera);
                                ComputeScreenSpaceIndirect(renderContext, camera);

                                // PHASE 6: lighting + opaque
                                ComputeDeferredShading(renderContext, camera);
                                RenderForward(renderContext, camera, sharedVisibility, cullingResults);
                                ComputeBurleySubsurface(renderContext, camera);
                                RenderAtmosphericSkyAndFog(renderContext, camera);

                                // PHASE 7: translucent slots. T0/T2 are insertion points only.
                                // T0 pre-fog: glass / surfaces that should receive aerial + volumetric fog.
                                RenderTranslucentDepth(renderContext, camera, cullingResults);
                                ComputeColorPyramid(renderContext, camera);
                                CopyHistoryColorPyramid(renderContext, camera, historyCache);
                                // T1 refractive: current ForwardTranslucent (no shader yet).
                                RenderForwardTranslucent(renderContext, camera, cullingResults);
                                // T2 post-fog: particles that must not be froxel-multiplied again.

                                // PHASE 8: temporal resolve + post
                                if (pipelineAsset.enableSuperResolution)
                                {
                                    ComputeSuperResolution(renderContext, camera, historyCache, cameraUniform.jitter);
                                    CopyHistorySuperResolution(renderContext, historyCache, camera);
                                }
                                else
                                {
                                    ComputeAntiAliasing(renderContext, camera, historyCache, cameraUniform);
                                    CopyHistoryAntiAliasing(renderContext, historyCache, camera);
                                    CopyHistoryDepth(renderContext, historyCache, camera);
                                }
                                ComputePostProcessing(renderContext, camera);
                                frameState.features.EnsureRequiredProducers(pipelineAsset.enableSuperResolution);

                            #if UNITY_EDITOR
                                RenderWireOverlay(renderContext, camera);
                                RenderGizmos(renderContext, camera);
                            #endif
                                RenderPresent(renderContext, camera);
                            }

                            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ExecuteRG)))
                            {
                                // ReleaseAllDrawLists releases per-Declare visibility refs (exception-safe).
                                frameState.executeSucceeded = m_RGBuilder.Execute(renderContext, m_ResourcePool, cmdBuffer);
                            }
                        }
                        catch (Exception exception)
                        {
                            historyCache.RollbackPending();
                            if (firstCameraException == null)
                            {
                                firstCameraException = exception;
                            }
                        }
                        finally
                        {
                            // If recording aborted before Execute, still free DrawList visibility / GPU payloads.
                            m_RGBuilder.ClearRecordedGraph();
                            m_ShadowCasterSplits.Clear();
                            m_VisibilityShare.Release(sharedVisibility);
                            sharedVisibility = MeshVisibilityHandle.Invalid;
                            m_ActiveFrameState = null;
                        }
                        EndCameraRendering(scriptableRenderContext, camera);
                    }

                    m_RGScoper.Clear();
                    cameraUniform.UnpateUniformData(camera, true);
                }

                scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                scriptableRenderContext.Submit();

                foreach (KeyValuePair<int, CameraFrameState> pair in m_CameraStates)
                {
                    CameraFrameState state = pair.Value;
                    if (state.executeSucceeded)
                    {
                        state.historyCache.CommitFrame();
                    }
                    state.historyCache.FlushRetired();
                    state.executeSucceeded = false;
                }

                RecycleUnseenCameraStates(Time.frameCount);
                EndContextRendering(scriptableRenderContext, cameras);

                // Physical GPU/CPU resource retirement after Submit (logical Retire happened in ReleaseAll).
                MeshDrawGPUBackend.FlushRetiredPayloads();
                m_DepthMeshProcessor?.FlushRetiredBuffers();
                m_GBufferMeshProcessor?.FlushRetiredBuffers();
                m_ForwardMeshProcessor?.FlushRetiredBuffers();
                m_MotionMeshProcessor?.FlushRetiredBuffers();
                m_ShadowMeshProcessor?.FlushRetiredBuffers();

                // End FrameContext
                m_MeshSceneResidency.Clear();
                CommandBufferPool.Release(cmdBuffer);

                if (firstCameraException != null)
                {
                    throw firstCameraException;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected float GetMipmapBiasOffset(in int renderWidth, in int displayWidth)
        {
            return math.log2((float)renderWidth / displayWidth) - 1.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SetGraphicsSetting()
        {
            Shader.globalRenderPipeline = "InfinityRenderPipeline";

            GraphicsUtility.m_BlitMaterial = pipelineAsset.blitMaterial;

            GraphicsSettings.lightsUseLinearIntensity = true;
            GraphicsSettings.lightsUseColorTemperature = true;
            GraphicsSettings.useScriptableRenderPipelineBatching = pipelineAsset.enableSRPBatch;

            SupportedRenderingFeatures.active = new SupportedRenderingFeatures()
            {
                reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.Rotation,
                defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.IndirectOnly,
                mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.IndirectOnly | SupportedRenderingFeatures.LightmapMixedBakeModes.Shadowmask,
                lightmapBakeTypes = LightmapBakeType.Baked | LightmapBakeType.Mixed | LightmapBakeType.Realtime,
                lightmapsModes = LightmapsMode.NonDirectional | LightmapsMode.CombinedDirectional,
                lightProbeProxyVolumes = true,
                motionVectors = true,
                receiveShadows = true,
                reflectionProbes = true,
                rendererPriority = true,
                overridesFog = true,
                overridesOtherLightingSettings = true,
                editableMaterialRenderQueue = true,
                enlighten = true,
                overridesLODBias = true,
                overridesMaximumLODLevel = true
            };
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
        ProfilingSampler GetCameraSampler(Camera camera, CameraComponent cameraComponent)
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

                #if UNITY_EDITOR
                    InvokeProxyUpdateEditor();
                #else
                    InvokeProxyUpdateRuntime();
                #endif

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
            if (disposing)
            {
                //EditorSceneManager.sceneUnloaded -= OnSceneUnloaded;
                renderContext.Dispose();
                m_DepthMeshProcessor?.Dispose();
                m_GBufferMeshProcessor?.Dispose();
                m_ForwardMeshProcessor?.Dispose();
                m_MotionMeshProcessor?.Dispose();
                m_ShadowMeshProcessor?.Dispose();
                MeshDrawGPUBackend.Dispose();
                m_VisibilityShare?.Dispose();
                m_MeshSceneResidency.Dispose();
                m_RGScoper.Dispose();
                m_RGBuilder.Dispose();
                m_ResourcePool.Dispose();
                foreach (KeyValuePair<int, CameraFrameState> pair in m_CameraStates)
                {
                    pair.Value.Dispose();
                }
                m_CameraStates.Clear();
                m_CameraSamplers.Clear();
                VolumeManager.instance.Deinitialize();
                m_DiffusionProfileBuffer?.Release();
                m_DiffusionProfileBuffer = null;
            }
        }

        VolumeStack ActiveVolumeStack => m_ActiveFrameState.volumeStack;

        FrameFeatureSet ActiveFeatures => m_ActiveFrameState.features;

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

        CameraFrameState GetOrCreateCameraFrameState(int cameraId)
        {
            if (!m_CameraStates.TryGetValue(cameraId, out CameraFrameState frameState))
            {
                frameState = new CameraFrameState(cameraId);
                m_CameraStates.Add(cameraId, frameState);
            }

            return frameState;
        }

        void RecycleUnseenCameraStates(int frameCount)
        {
            const int UnseenFramesToRecycle = 8;
            m_CameraStateRecycleIds.Clear();
            foreach (KeyValuePair<int, CameraFrameState> pair in m_CameraStates)
            {
                if (frameCount - pair.Value.lastSeenFrame > UnseenFramesToRecycle)
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

        void ConfigureFrameFeatures(CameraFrameState frameState)
        {
            FrameFeatureSet features = frameState.features;
            features.Reset();
            VolumeStack stack = frameState.volumeStack;

            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            features.Request(EFrameFeature.GBuffer);
            features.MarkSupported(EFrameFeature.GBuffer);
            if (renderContext.WorldDecalCount > 0)
            {
                features.Request(EFrameFeature.DBuffer);
                features.MarkSupported(EFrameFeature.DBuffer);
            }
            features.Request(EFrameFeature.Motion);
            features.MarkSupported(EFrameFeature.Motion);
            features.Request(EFrameFeature.DeferredShading);
            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.deferredShadingShader, "DeferredShadingCS"))
            {
                features.MarkSupported(EFrameFeature.DeferredShading);
            }

            features.Request(EFrameFeature.HiZ);
            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.hiZShader, "HiZ_Generation"))
            {
                features.MarkSupported(EFrameFeature.HiZ);
            }

            features.Request(EFrameFeature.ColorPyramid);
            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.colorPyramidShader, "KMain"))
            {
                features.MarkSupported(EFrameFeature.ColorPyramid);
            }

            features.Request(EFrameFeature.PostProcess);
            features.Request(EFrameFeature.Display);
            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.postProcessingShader, "BloomDownsample", "BloomUpsample", "FinalCombine"))
            {
                features.MarkSupported(EFrameFeature.PostProcess);
                features.MarkSupported(EFrameFeature.Display);
            }

            if (pipelineAsset.enableSuperResolution)
            {
                features.Request(EFrameFeature.SuperResolution);
                if (pipelineAsset.superResolutionShader != null)
                {
                    features.MarkSupported(EFrameFeature.SuperResolution);
                }
            }
            else
            {
                features.Request(EFrameFeature.TAA);
                if (pipelineAsset.taaShader != null)
                {
                    features.MarkSupported(EFrameFeature.TAA);
                }
            }

            // DeferredShading still hard-queries these optional SRVs, so request them when supported.
            // TODO: gate with VolumeHasOverrides after DeferredShading TryQuery optional inputs.
            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.ssaoShader, "OcclusionTrace", "OcclusionSpatialX", "OcclusionSpatialY"))
            {
                features.Request(EFrameFeature.GTAO);
                features.MarkSupported(EFrameFeature.GTAO);
            }

            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.contactShadowShader, "ContactShadowCS"))
            {
                features.Request(EFrameFeature.ContactShadow);
                features.MarkSupported(EFrameFeature.ContactShadow);
            }

            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.ssrShader, "Raytracing"))
            {
                features.Request(EFrameFeature.SSR);
                features.MarkSupported(EFrameFeature.SSR);
            }

            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.ssgiShader, "Raytracing"))
            {
                features.Request(EFrameFeature.SSGI);
                features.MarkSupported(EFrameFeature.SSGI);
            }

            var volFog = stack.GetComponent<VolumetricFog>();
            if (GraphicsUtility.VolumeHasOverrides(volFog))
            {
                features.Request(EFrameFeature.VolumetricFog);
            }
            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.volumetricFogShader, "ScatterDensity", "Integrate"))
            {
                features.MarkSupported(EFrameFeature.VolumetricFog);
            }

            var volCloud = stack.GetComponent<VolumetricCloud>();
            if (GraphicsUtility.VolumeHasOverrides(volCloud))
            {
                features.Request(EFrameFeature.VolumetricCloud);
            }
            if (GraphicsUtility.HasRequiredKernels(pipelineAsset.volumetricCloudShader, "VolumetricCloudCS"))
            {
                features.MarkSupported(EFrameFeature.VolumetricCloud);
            }
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