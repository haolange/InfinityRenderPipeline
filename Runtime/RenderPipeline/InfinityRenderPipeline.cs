using System;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.VFX;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Component;
using System.Collections.Generic;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Core;
using InfinityTech.Rendering.Feature;
using System.Runtime.CompilerServices;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.TerrainPipeline;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    internal enum EPipelineProfileId
    {
        ViewContext,
        ComputeLOD,
        CulllingScene,
        BeginFrameRendering,
        EndFrameRendering,
        SceneRendering,
        CameraRendering
    }

    internal class FViewUnifrom
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

        private void UnpateCurrBufferData(Camera camera)
        {
            matrix_WorldToView = camera.worldToCameraMatrix;
            matrix_ViewToWorld = matrix_WorldToView.inverse;
            matrix_Proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            matrix_InvProj = matrix_Proj.inverse;
            matrix_JitterProj = FTemporalAntiAliasing.CaculateProjectionMatrix(camera, ref frameIndex, ref jitter, matrix_Proj);
            matrix_InvJitterProj = matrix_JitterProj.inverse;
            matrix_FlipYProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            matrix_InvFlipYProj = matrix_FlipYProj.inverse;
            matrix_FlipYJitterProj = FTemporalAntiAliasing.CaculateProjectionMatrix(camera, ref frameIndex, ref jitter, matrix_FlipYProj, false);
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

        private void UnpateLastBufferData()
        {
            lastJitter = jitter;
            lastFrameIndex = frameIndex;
            matrix_LastViewProj = matrix_ViewProj;
            matrix_LastViewFlipYProj = matrix_ViewFlipYProj;
        }

        public void UnpateViewUnifromData(Camera camera, in bool isLastData = false)
        {
            if(!isLastData) 
            {
                UnpateCurrBufferData(camera);
            } else {
                UnpateLastBufferData();
            }
        }

        public void SetViewUnifromDataToGPU(CommandBuffer cmdBuffer, in float2 resolution)
        {
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
        }
    }

    public partial class InfinityRenderPipeline : RenderPipeline
    {
        private FGPUScene m_GPUScene;
        private FRDGScoper m_GraphScoper;
        private FRDGBuilder m_GraphBuilder;
        private FResourcePool m_ResourcePool;
        private NativeList<JobHandle> m_MeshPassJobRefs;
        private FMeshPassProcessor m_DepthMeshProcessor;
        private FMeshPassProcessor m_GBufferMeshProcessor;
        private FMeshPassProcessor m_ForwardMeshProcessor;
        private Dictionary<int, FViewUnifrom> m_ViewUnifroms;
        private Dictionary<int, FHistoryCache> m_HistoryCaches;

        internal FRenderWorld renderWorld => FRenderWorld.RenderWorld;
        internal InfinityRenderPipelineAsset pipelineAsset 
        { 
            get 
            { 
                return (InfinityRenderPipelineAsset)GraphicsSettings.currentRenderPipeline; 
            }
        }

        public InfinityRenderPipeline()
        {
            SetGraphicsSetting();
            m_ResourcePool = new FResourcePool();
            m_GraphBuilder = new FRDGBuilder("RenderGraph");
            m_GraphScoper = new FRDGScoper(m_GraphBuilder);
            m_ViewUnifroms = new Dictionary<int, FViewUnifrom>();
            m_HistoryCaches = new Dictionary<int, FHistoryCache>();
            m_MeshPassJobRefs = new NativeList<JobHandle>(32, Allocator.Persistent);
            m_GPUScene = new FGPUScene(m_ResourcePool, renderWorld.GetMeshBatchColloctor());
            m_DepthMeshProcessor = new FMeshPassProcessor(m_GPUScene, ref m_MeshPassJobRefs);
            m_GBufferMeshProcessor = new FMeshPassProcessor(m_GPUScene, ref m_MeshPassJobRefs);
            m_ForwardMeshProcessor = new FMeshPassProcessor(m_GPUScene, ref m_MeshPassJobRefs);
        }

        protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
        {
            //Begin FrameContext
            RTHandles.Initialize(Screen.width, Screen.height);
            CommandBuffer cmdBuffer = CommandBufferPool.Get("");
            cmdBuffer.Clear();
            m_GPUScene.Update(cmdBuffer);
            
            BeginFrameRendering(renderContext, cameras);
            for (int i = 0; i < cameras.Length; ++i)
            {
                Camera camera = cameras[i];
                CameraComponent cameraComponent = camera.GetComponent<CameraComponent>();

                //Camera Rendering
                using (new ProfilingScope(cmdBuffer, cameraComponent ? cameraComponent.viewProfiler : ProfilingSampler.Get(EPipelineProfileId.CameraRendering)))
                {
                    BeginCameraRendering(renderContext, camera);
                    {
                        FCullingData cullingData;
                        FViewUnifrom viewUnifrom;
                        FHistoryCache historyCache;
                        CullingResults cullingResult;

                        int cameraId = GetCameraID(camera);
                        bool isEditView = camera.cameraType == CameraType.SceneView;
                        bool isSceneView = camera.cameraType == CameraType.Game || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.SceneView;

                        #region BeginViewContext
                        using (new ProfilingScope(null, ProfilingSampler.Get(EPipelineProfileId.ViewContext)))
                        {
                            // Get PerCamera History ResourceCache Manager
                            if (!m_HistoryCaches.ContainsKey(cameraId))
                            {
                                historyCache = new FHistoryCache();
                                m_HistoryCaches.Add(cameraId, historyCache);
                            } else {
                                historyCache = m_HistoryCaches[cameraId];
                            }

                            // Get PerCamera ViewData
                            if (!m_ViewUnifroms.ContainsKey(cameraId))
                            {
                                viewUnifrom = new FViewUnifrom();
                                m_ViewUnifroms.Add(cameraId, viewUnifrom);
                            } else {
                                viewUnifrom = m_ViewUnifroms[cameraId];
                            }

                            #if UNITY_EDITOR
                            if (isEditView) 
                            { 
                                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera); 
                            }
                            #endif

                            // Update Vfx
                            m_MeshPassJobRefs.Clear();
                            VFXManager.PrepareCamera(camera);
                            VFXManager.ProcessCameraCommand(camera, cmdBuffer);

                            // Update ViewUnifrom
                            renderContext.SetupCameraProperties(camera);
                            viewUnifrom.UnpateViewUnifromData(camera, false);
                            viewUnifrom.SetViewUnifromDataToGPU(cmdBuffer, new float2(camera.pixelWidth, camera.pixelHeight));

                            //Scene Culling
                            using (new ProfilingScope(null, ProfilingSampler.Get(EPipelineProfileId.CulllingScene)))
                            {
                                camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParameters);
                                cullingParameters.shadowDistance = 128;
                                cullingParameters.cullingOptions = CullingOptions.ShadowCasters | CullingOptions.NeedsLighting | CullingOptions.DisablePerObjectCulling;
                                cullingResult = renderContext.Cull(ref cullingParameters);
                                cullingData = renderContext.DispatchCull(m_GPUScene, isSceneView, ref cullingParameters);
                            }
                            
                            //Compute LOD
                            using (new ProfilingScope(null, ProfilingSampler.Get(EPipelineProfileId.ComputeLOD)))
                            {
                                List<TerrainComponent> terrains = renderWorld.GetWorldTerrains();
                                float4x4 matrix_Proj = TerrainUtility.GetProjectionMatrix(camera.fieldOfView + 30, camera.pixelWidth, camera.pixelHeight, camera.nearClipPlane, camera.farClipPlane);
                                for(int j = 0; j < terrains.Count; ++j)
                                {
                                    TerrainComponent terrain = terrains[j];
                                    terrain.ComputeLOD(camera.transform.position, matrix_Proj);
                                    
                                    #if UNITY_EDITOR
                                        if (Handles.ShouldRenderGizmos()) { terrain.DrawBounds(true); }
                                    #endif
                                }
                            }
                        }
                        #endregion //BeginViewContext

                        #region ExecuteViewContext
                        RenderDepth(camera, cullingData, cullingResult);
                        RenderGBuffer(camera, cullingData, cullingResult);
                        RenderMotion(camera, cullingData, cullingResult);
                        RenderForward(camera, cullingData, cullingResult);
                        RenderSkyBox(camera);
                        RenderAntiAliasing(camera, historyCache);
                        #if UNITY_EDITOR
                        RenderGizmos(camera);
                        #endif
                        RenderPresent(camera, camera.targetTexture);

                        JobHandle.CompleteAll(m_MeshPassJobRefs);
                        m_GraphBuilder.Execute(renderContext, renderWorld, cmdBuffer, m_ResourcePool);
                        #endregion //ExecuteViewContext

                        #region EndViewContext
                        cullingData.Release();
                        m_GraphScoper.Clear();
                        viewUnifrom.UnpateViewUnifromData(camera, true);
                        #endregion //EndViewContext
                    }
                    EndCameraRendering(renderContext, camera);
                }
            }
            EndFrameRendering(renderContext, cameras);
            
            //Execute FrameContext
            renderContext.ExecuteCommandBuffer(cmdBuffer);
            renderContext.Submit();
            
            //End FrameContext
            m_GPUScene.Clear();
            CommandBufferPool.Release(cmdBuffer);
        }

        protected void SetGraphicsSetting()
        {
            Shader.globalRenderPipeline = "InfinityRenderPipeline";

            GraphicsSettings.lightsUseLinearIntensity = true;
            GraphicsSettings.lightsUseColorTemperature = true;
            InfinityRenderPipelineAsset PipelineAsset = (InfinityRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            GraphicsSettings.useScriptableRenderPipelineBatching = PipelineAsset.enableSRPBatch;

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
                editableMaterialRenderQueue = true
                , enlighten = true
                , overridesLODBias = true
                , overridesMaximumLODLevel = true
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                m_GraphScoper.Dispose();
                m_GraphBuilder.Dispose();
                m_ResourcePool.Dispose();
                m_MeshPassJobRefs.Dispose();
                foreach (var historyCache in m_HistoryCaches)
                {
                    historyCache.Value.Release();
                }
                m_HistoryCaches.Clear();
            }
        }
    }
}