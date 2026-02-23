using System;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.VFX;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
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
        ExecuteRG,
        CameraRendering
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

        private void UpdateCurrFrameData(Camera camera)
        {
            matrix_WorldToView = camera.worldToCameraMatrix;
            matrix_ViewToWorld = matrix_WorldToView.inverse;
            matrix_Proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            matrix_FlipYProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            TemporalAntiAliasingGenerator.CaculateProjectionMatrix(camera, 0.75f, ref frameIndex, ref jitter, matrix_Proj, ref matrix_JitterProj, ref matrix_FlipYJitterProj);
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

        private void UpdateLastFrameData()
        {
            lastJitter = jitter;
            lastFrameIndex = frameIndex;
            matrix_LastViewProj = matrix_ViewProj;
            matrix_LastViewFlipYProj = matrix_ViewFlipYProj;
            matrix_LastViewJitterProj = matrix_ViewJitterProj;
            matrix_LastViewFlipYJitterProj = matrix_ViewFlipYJitterProj;
        }

        public void UnpateUniformData(Camera camera, in bool bLastFrame = false)
        {
            if(!bLastFrame) 
            {
                UpdateCurrFrameData(camera);
            } else {
                UpdateLastFrameData();
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
        private GPUScene m_GPUScene;
        private RGScoper m_RGScoper;
        private RGBuilder m_RGBuilder;
        private ResourcePool m_ResourcePool;
        private NativeList<JobHandle> m_MeshPassJobRefs;
        private MeshPassProcessor m_DepthMeshProcessor;
        private MeshPassProcessor m_GBufferMeshProcessor;
        private MeshPassProcessor m_ForwardMeshProcessor;
        private Dictionary<int, HistoryCache> m_HistoryCaches;
        private Dictionary<int, CameraUniform> m_CameraUniforms;

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
            m_HistoryCaches = new Dictionary<int, HistoryCache>();
            m_CameraUniforms = new Dictionary<int, CameraUniform>();
            m_MeshPassJobRefs = new NativeList<JobHandle>(32, Allocator.Persistent);
            m_ResourcePool = new ResourcePool();
            m_GPUScene = new GPUScene(m_ResourcePool, renderContext.GetMeshBatchColloctor());
            m_DepthMeshProcessor = new MeshPassProcessor(m_GPUScene, m_ResourcePool, ref m_MeshPassJobRefs);
            m_GBufferMeshProcessor = new MeshPassProcessor(m_GPUScene, m_ResourcePool, ref m_MeshPassJobRefs);
            m_ForwardMeshProcessor = new MeshPassProcessor(m_GPUScene, m_ResourcePool, ref m_MeshPassJobRefs);
        }

        protected override void Render(ScriptableRenderContext scriptableRenderContext, Camera[] cameras)
        {
            // Begin FrameContext
            using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.FrameRendering)))
            {
                renderContext.scriptableRenderContext = scriptableRenderContext;

                InvokeProxyUpdate();
                m_GPUScene.Update();
                CommandBuffer cmdBuffer = CommandBufferPool.Get("");
                cmdBuffer.Clear();
                
                BeginFrameRendering(scriptableRenderContext, cameras);
                for (int i = 0; i < cameras.Length; ++i)
                {
                    Camera camera = cameras[i];
                    CameraComponent cameraComponent = camera.GetComponent<CameraComponent>();

                    CullingDatas cullingDatas;
                    HistoryCache historyCache;
                    CameraUniform cameraUniform;
                    CullingResults cullingResults;

                    int cameraId = GetCameraID(camera);
                    bool isEditView = camera.cameraType == CameraType.SceneView;
                    bool isSceneView = camera.cameraType == CameraType.Game || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.SceneView;

                    // Get PerCamera HistoryCache
                    if (!m_HistoryCaches.ContainsKey(cameraId))
                    {
                        historyCache = new HistoryCache();
                        m_HistoryCaches.Add(cameraId, historyCache);
                    } 
                    else 
                    {
                        historyCache = m_HistoryCaches[cameraId];
                    }

                    // Get PerCamera Data
                    if (!m_CameraUniforms.ContainsKey(cameraId))
                    {
                        cameraUniform = new CameraUniform();
                        m_CameraUniforms.Add(cameraId, cameraUniform);
                    } 
                    else 
                    {
                        cameraUniform = m_CameraUniforms[cameraId];
                    }

                    // CameraRendering
                    cameraUniform.UnpateUniformData(camera, false);
                    using (new ProfilingScope(cmdBuffer, cameraComponent ? cameraComponent.viewProfiler : ProfilingSampler.Get(EPipelineProfileId.CameraRendering)))
                    {
                        BeginCameraRendering(scriptableRenderContext, camera);
                        using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.SetupCamera)))
                        {
                            m_MeshPassJobRefs.Clear();

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
                                cullingDatas = scriptableRenderContext.DispatchCull(m_GPUScene, isSceneView, ref cullingParameters);
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
                                renderContext.lightContext.Clear();
                                NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
                                Dictionary<int, LightComponent> lights = renderContext.GetWorldLight();

                                for (int j = 0; j < visibleLights.Length; ++j)
                                {
                                    VisibleLight visibleLight = visibleLights[j];
                                    if (lights.TryGetValue(visibleLight.light.GetInstanceID(), out LightComponent additionLight))
                                    {
                                        switch (additionLight.lightType)
                                        {
                                            case ELightType.Directional:
                                                renderContext.lightContext.AddDirectionalLight(j, additionLight);
                                                break;

                                            case ELightType.Point:

                                                break;

                                            case ELightType.Spot:

                                                break;

                                            case ELightType.Rect:

                                                break;
                                        }
                                    }
                                }

                                renderContext.lightContext.SetDirectionalLightData(cmdBuffer);
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
                        }

                        #region PostProcessVolume Parameter
                        VolumeStack volumeStack = VolumeManager.instance.stack;

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
                            // ═══════════════════════════════════════════════════════════
                            // PHASE 1: BASE GEOMETRY (Raster-heavy → async overlap window)
                            // ═══════════════════════════════════════════════════════════
                            RenderDepth(renderContext, camera, cullingDatas, cullingResults);         // [Graphics] Depth Prepass
                            ComputeCombineLuts(renderContext, combineLutParameterDescriptor);         // [Async] Color LUT (ALU-bound, overlaps depth raster)
                            RenderDBuffer(renderContext, camera, cullingResults);                     // [Graphics] Decal DBuffer
                            RenderGBuffer(renderContext, camera, cullingDatas, cullingResults);       // [Graphics] G-Buffer Fill
                            RenderMotion(renderContext, camera, cullingDatas, cullingResults);        // [Graphics] Object + Camera Motion

                            // ═══════════════════════════════════════════════════════════
                            // PHASE 2: SHADOWS (Vertex/raster-bound → best async window)
                            // ═══════════════════════════════════════════════════════════
                            RenderCascadeShadow(renderContext, camera, cullingResults);               // [Graphics] Cascade Shadow Maps
                            RenderLocalShadow(renderContext, camera, cullingResults);                 // [Graphics] Local Shadow Maps
                            ComputeHiZ(renderContext, camera);                                       // [Async] HiZ Min/Max Pyramid
                            ComputeHalfResDownsample(renderContext, camera);                         // [Async] Half-Res Depth + Normal
                            ComputeAtmosphericLUT(renderContext, camera);                            // [Async] Atmospheric Scattering LUTs

                            // ═══════════════════════════════════════════════════════════
                            // PHASE 3: SCREEN-SPACE EFFECTS (Compute-dominated after sync)
                            // ═══════════════════════════════════════════════════════════
                            ComputeZBinningLightList(renderContext, camera);                          // Z-Bin + Tile Light Lists
                            ComputeGroundTruthOcclusion(renderContext, camera);                       // GTAO (4-kernel: trace→spatialXY→temporal)
                            ComputeContactShadow(renderContext, camera);                              // Screen-Space Contact Shadows
                            ComputeScreenSpaceReflection(renderContext, camera);                      // HiZ Ray-Traced SSR
                            ComputeScreenSpaceIndirect(renderContext, camera);                        // HiZ Ray-Traced SSGI

                            // ═══════════════════════════════════════════════════════════
                            // PHASE 4: LIGHTING RESOLVE + OPAQUE
                            // ═══════════════════════════════════════════════════════════
                            ComputeDeferredShading(renderContext, camera);                            // Tiled Deferred Resolve
                            RenderForward(renderContext, camera, cullingDatas, cullingResults);       // [Graphics] Forward Opaque
                            ComputeBurleySubsurface(renderContext, camera);                           // [Async] SSS Diffusion (overlaps forward raster)

                            // ═══════════════════════════════════════════════════════════
                            // PHASE 5: ATMOSPHERE + VOLUMETRICS
                            // ═══════════════════════════════════════════════════════════
                            RenderAtmosphericSkyAndFog(renderContext, camera);                        // [Graphics] Sky + Aerial Perspective
                            ComputeVolumetricFog(renderContext, camera);                              // Froxel Volumetric Fog
                            ComputeVolumetricCloud(renderContext, camera);                            // Ray-Marched Clouds

                            // ═══════════════════════════════════════════════════════════
                            // PHASE 6: TRANSLUCENT
                            // ═══════════════════════════════════════════════════════════
                            RenderTranslucentDepth(renderContext, camera, cullingResults);            // Translucent Depth Prepass
                            ComputeColorPyramid(renderContext, camera);                              // Color Mip Chain (refraction)
                            RenderForwardTranslucent(renderContext, camera, cullingResults);          // Forward Translucent Rendering

                            // ═══════════════════════════════════════════════════════════
                            // PHASE 7: POST-PROCESSING
                            // ═══════════════════════════════════════════════════════════
                            ComputeSuperResolution(renderContext, camera, cameraUniform.jitter);      // Temporal Upscaling
                            ComputeAntiAliasing(renderContext, camera, historyCache);                 // TAA (fallback if no SR)
                            CopyHistoryAntiAliasing(renderContext);
                            ComputePostProcessing(renderContext, camera);                             // Bloom + Color Grading + Tonemapping

                        #if UNITY_EDITOR
                            RenderWireOverlay(renderContext, camera);
                            RenderGizmos(renderContext, camera);
                        #endif
                            RenderPresent(renderContext, camera);
                        }

                        using (new ProfilingScope(ProfilingSampler.Get(EPipelineProfileId.ExecuteRG)))
                        {
                            JobHandle.CompleteAll(m_MeshPassJobRefs.AsArray());
                            m_RGBuilder.Execute(renderContext, m_ResourcePool, cmdBuffer);
                        }
                        EndCameraRendering(scriptableRenderContext, camera);
                    }

                    m_RGScoper.Clear();
                    cullingDatas.Release();
                    cameraUniform.UnpateUniformData(camera, true);
                }
                EndFrameRendering(scriptableRenderContext, cameras);
                
                // Execute FrameContext
                scriptableRenderContext.ExecuteCommandBuffer(cmdBuffer);
                scriptableRenderContext.Submit();
                
                // End FrameContext
                m_GPUScene.Clear();
                CommandBufferPool.Release(cmdBuffer);
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
                m_RGScoper.Dispose();
                m_RGBuilder.Dispose();
                m_ResourcePool.Dispose();
                m_MeshPassJobRefs.Dispose();
                foreach (var historyCache in m_HistoryCaches)
                {
                    historyCache.Value.Release();
                }
                m_HistoryCaches.Clear();
                VolumeManager.instance.Deinitialize();
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