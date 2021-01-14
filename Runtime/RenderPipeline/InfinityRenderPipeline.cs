using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Core;
using InfinityTech.Rendering.MeshDrawPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial struct ViewUnifromBuffer
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

        private static readonly int ID_Prev_FrameIndex = Shader.PropertyToID("Prev_FrameIndex");
        private static readonly int ID_Matrix_PrevViewProj = Shader.PropertyToID("Matrix_PrevViewProj");
        private static readonly int ID_Matrix_PrevViewFlipYProj = Shader.PropertyToID("Matrix_PrevViewFlipYProj");


        public int FrameIndex;
        public float2 TAAJitter;
        public Matrix4x4 Matrix_WorldToView;
        public Matrix4x4 Matrix_ViewToWorld;
        public Matrix4x4 Matrix_Proj;
        public Matrix4x4 Matrix_InvProj;
        public Matrix4x4 Matrix_JitterProj;
        public Matrix4x4 Matrix_InvJitterProj;
        public Matrix4x4 Matrix_FlipYProj;
        public Matrix4x4 Matrix_InvFlipYProj;
        public Matrix4x4 Matrix_FlipYJitterProj;
        public Matrix4x4 Matrix_InvFlipYJitterProj;
        public Matrix4x4 Matrix_ViewProj;
        public Matrix4x4 Matrix_InvViewProj;
        public Matrix4x4 Matrix_ViewFlipYProj;
        public Matrix4x4 Matrix_InvViewFlipYProj;
        public Matrix4x4 Matrix_ViewJitterProj;
        public Matrix4x4 Matrix_InvViewJitterProj;
        public Matrix4x4 Matrix_ViewFlipYJitterProj;
        public Matrix4x4 Matrix_InvViewFlipYJitterProj;

        public int Prev_FrameIndex;
        public float2 Prev_TAAJitter;
        public Matrix4x4 Matrix_PrevViewProj;
        public Matrix4x4 Matrix_PrevViewFlipYProj;

        private Matrix4x4 GetJitteredProjectionMatrix(Matrix4x4 origProj, Camera UnityCamera)
        {

            float jitterX = HaltonSequence.Get((FrameIndex & 1023) + 1, 2) - 0.5f;
            float jitterY = HaltonSequence.Get((FrameIndex & 1023) + 1, 3) - 0.5f;
            TAAJitter = new float2(jitterX, jitterY);
            float4 taaJitter = new float4(jitterX, jitterY, jitterX / UnityCamera.pixelRect.size.x, jitterY / UnityCamera.pixelRect.size.y);

            if (++FrameIndex >= 8)
                FrameIndex = 0;

            Matrix4x4 proj;

            if (UnityCamera.orthographic) {
                float vertical = UnityCamera.orthographicSize;
                float horizontal = vertical * UnityCamera.aspect;

                var offset = taaJitter;
                offset.x *= horizontal / (0.5f * UnityCamera.pixelRect.size.x);
                offset.y *= vertical / (0.5f * UnityCamera.pixelRect.size.y);

                float left = offset.x - horizontal;
                float right = offset.x + horizontal;
                float top = offset.y + vertical;
                float bottom = offset.y - vertical;

                proj = Matrix4x4.Ortho(left, right, bottom, top, UnityCamera.nearClipPlane, UnityCamera.farClipPlane);
            } else {
                var planes = origProj.decomposeProjection;

                float vertFov = Math.Abs(planes.top) + Math.Abs(planes.bottom);
                float horizFov = Math.Abs(planes.left) + Math.Abs(planes.right);

                var planeJitter = new Vector2(jitterX * horizFov / UnityCamera.pixelRect.size.x, jitterY * vertFov / UnityCamera.pixelRect.size.y);

                planes.left += planeJitter.x;
                planes.right += planeJitter.x;
                planes.top += planeJitter.y;
                planes.bottom += planeJitter.y;

                proj = Matrix4x4.Frustum(planes);
            }

            return proj;
        }

        private void UnpateCurrBufferData(Camera RenderCamera)
        {
            Matrix_WorldToView = RenderCamera.worldToCameraMatrix;
            Matrix_ViewToWorld = Matrix_WorldToView.inverse;
            Matrix_Proj = GL.GetGPUProjectionMatrix(RenderCamera.projectionMatrix, true);
            Matrix_InvProj = Matrix_Proj.inverse;
            Matrix_JitterProj = GetJitteredProjectionMatrix(Matrix_Proj, RenderCamera);
            Matrix_InvJitterProj = Matrix_JitterProj.inverse;
            Matrix_FlipYProj = GL.GetGPUProjectionMatrix(RenderCamera.projectionMatrix, false);
            Matrix_InvFlipYProj = Matrix_FlipYProj.inverse;
            Matrix_FlipYJitterProj = GetJitteredProjectionMatrix(Matrix_FlipYProj, RenderCamera);
            Matrix_InvFlipYJitterProj = Matrix_FlipYJitterProj.inverse;
            Matrix_ViewProj = Matrix_Proj * Matrix_WorldToView;
            Matrix_InvViewProj = Matrix_ViewProj.inverse;
            Matrix_ViewFlipYProj = Matrix_FlipYProj * Matrix_WorldToView;
            Matrix_InvViewFlipYProj = Matrix_ViewFlipYProj.inverse;
            Matrix_ViewJitterProj = Matrix_JitterProj * Matrix_WorldToView;
            Matrix_InvViewJitterProj = Matrix_ViewJitterProj.inverse;
            Matrix_ViewFlipYJitterProj = Matrix_FlipYJitterProj * Matrix_WorldToView;
            Matrix_InvViewFlipYJitterProj = Matrix_ViewFlipYJitterProj.inverse;
        }

        private void UnpateLastBufferData()
        {
            Prev_FrameIndex = FrameIndex;
            Prev_TAAJitter = TAAJitter;
            Matrix_PrevViewProj = Matrix_ViewProj;
            Matrix_PrevViewFlipYProj = Matrix_ViewFlipYProj;
        }

        public void UnpateBufferData(bool bLastData, Camera RenderCamera)
        {
            if(!bLastData) {
                UnpateCurrBufferData(RenderCamera);
            } else {
                UnpateLastBufferData();
            }
        }

        public void BindGPUProperty(CommandBuffer CmdList)
        {
            CmdList.SetGlobalInt(ID_FrameIndex, FrameIndex);
            CmdList.SetGlobalInt(ID_Prev_FrameIndex, Prev_FrameIndex);
            CmdList.SetGlobalVector(ID_TAAJitter, new float4(TAAJitter.x, TAAJitter.y, Prev_TAAJitter.x, Prev_TAAJitter.y));

            CmdList.SetGlobalMatrix(ID_Matrix_WorldToView, Matrix_WorldToView);
            CmdList.SetGlobalMatrix(ID_Matrix_ViewToWorld, Matrix_ViewToWorld);
            CmdList.SetGlobalMatrix(ID_Matrix_Proj, Matrix_Proj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvProj, Matrix_InvProj);
            CmdList.SetGlobalMatrix(ID_Matrix_JitterProj, Matrix_JitterProj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvJitterProj, Matrix_InvJitterProj);
            CmdList.SetGlobalMatrix(ID_Matrix_FlipYProj, Matrix_FlipYProj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvFlipYProj, Matrix_InvFlipYProj);
            CmdList.SetGlobalMatrix(ID_Matrix_FlipYJitterProj, Matrix_FlipYJitterProj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvFlipYJitterProj, Matrix_InvFlipYJitterProj);
            CmdList.SetGlobalMatrix(ID_Matrix_ViewProj, Matrix_ViewProj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvViewProj, Matrix_InvViewProj);
            CmdList.SetGlobalMatrix(ID_Matrix_ViewFlipYProj, Matrix_ViewFlipYProj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvViewFlipYProj, Matrix_InvViewFlipYProj);
            CmdList.SetGlobalMatrix(ID_Matrix_ViewJitterProj, Matrix_ViewJitterProj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvViewJitterProj, Matrix_InvViewJitterProj);
            CmdList.SetGlobalMatrix(ID_Matrix_ViewFlipYJitterProj, Matrix_ViewFlipYJitterProj);
            CmdList.SetGlobalMatrix(ID_Matrix_InvViewFlipYJitterProj, Matrix_InvViewFlipYJitterProj);
           
            CmdList.SetGlobalMatrix(ID_Matrix_PrevViewProj, Matrix_PrevViewProj);
            CmdList.SetGlobalMatrix(ID_Matrix_PrevViewFlipYProj, Matrix_PrevViewFlipYProj);
        }
    }

    public partial class InfinityRenderPipeline : RenderPipeline
    {
        private RDGGraphBuilder GraphBuilder;
        private ViewUnifromBuffer ViewUnifrom;
        private InfinityRenderPipelineAsset RenderPipelineAsset;

        public InfinityRenderPipeline()
        {
            ViewUnifrom = new ViewUnifromBuffer();
            GraphBuilder = new RDGGraphBuilder("InfinityGraph");
            RenderPipelineAsset = (InfinityRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;

            SetGraphicsSetting();
        }

        protected override void Render(ScriptableRenderContext RenderContext, Camera[] ViewList)
        {
            //Init Frame
            NativeArray<FMeshBatch> MeshBatchs = new NativeArray<FMeshBatch>(GetWorld().GetMeshBatchColloctor().CacheMeshBatchStateBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            GetWorld().GetMeshBatchColloctor().GatherMeshBatch(MeshBatchs);

            //Render Pipeline
            BeginFrameRendering(RenderContext, ViewList);
            foreach (Camera View in ViewList)
            {
                bool bSceneView = View.cameraType == CameraType.SceneView;
                bool bRenderView = View.cameraType == CameraType.Game || View.cameraType == CameraType.Reflection || View.cameraType == CameraType.SceneView;

                bool isSceneViewCam = View.cameraType == CameraType.SceneView;
                #if UNITY_EDITOR
                if (isSceneViewCam) {
                    ScriptableRenderContext.EmitWorldGeometryForSceneView(View);
                }
                #endif

                //Prepare VisualEffects
                VFXManager.PrepareCamera(View);

                //Prepare ViewUnifrom
                ViewUnifrom.UnpateBufferData(false, View);

                //View RenderFamily
                CommandBuffer CmdBuffer = CommandBufferPool.Get("");

                //Binding ViewParameter
                BeginCameraRendering(RenderContext, View);
                CmdBuffer.DisableScissorRect();
                ViewUnifrom.BindGPUProperty(CmdBuffer);
                RenderContext.SetupCameraProperties(View);

                //VisualEffect
                VFXManager.ProcessCameraCommand(View, CmdBuffer);

                //Culling MeshBatch
                GetWorld().GetMeshBatchColloctor().Sync();
                FCullingData CullingData = new FCullingData();
                CullingData.Run(View, MeshBatchs, ECullingMethod.VisibleMark);

                //Culling Context
                ScriptableCullingParameters CullingParameter;
                View.TryGetCullingParameters(out CullingParameter);
                CullingResults CullingResult = RenderContext.Cull(ref CullingParameter);

                //Sync MeshBatch
                CullingData.Sync();

                //View RenderPass
                /*NativeList<FMeshDrawCommand> MeshDrawCommands = new NativeList<FMeshDrawCommand>(64, Allocator.TempJob);
                MeshDrawCommands.Dispose();*/

                RenderOpaqueDepth(View, CullingResult, MeshBatchs, CullingData);
                RenderOpaqueGBuffer(View, CullingResult, MeshBatchs, CullingData);
                RenderOpaqueMotion(View, CullingResult, MeshBatchs, CullingData);
                RenderSkyAtmosphere(View);

                #if UNITY_EDITOR
                    if (Handles.ShouldRenderGizmos()) {
                        RenderGizmo(View, GizmoSubset.PostImageEffects);
                    }
                #endif

                RenderPresentView(View, GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_ThinGBufferA), View.targetTexture);

                //Execute RenderGraph
                GraphBuilder.Execute(RenderContext, GetWorld(), CmdBuffer, ViewUnifrom.FrameIndex);
                EndCameraRendering(RenderContext, View);

                //Execute ViewRender
                RenderContext.ExecuteCommandBuffer(CmdBuffer);
                CommandBufferPool.Release(CmdBuffer);
                RenderContext.Submit();

                //Prepare ViewUnifrom
                ViewUnifrom.UnpateBufferData(true, View);

                //Release View
                CullingData.Release();
            }
            EndFrameRendering(RenderContext, ViewList);

            //Release Frame
            MeshBatchs.Dispose();
        }

        protected FRenderWorld GetWorld()
        {
            if (FRenderWorld.ActiveWorld != null) {
                return FRenderWorld.ActiveWorld;
            }

            return null;
        }

        protected void SetGraphicsSetting()
        {
            Shader.globalRenderPipeline = "InfinityRenderPipeline";

            GraphicsSettings.lightsUseLinearIntensity = true;
            GraphicsSettings.lightsUseColorTemperature = true;
            GraphicsSettings.useScriptableRenderPipelineBatching = RenderPipelineAsset.EnableSRPBatch;

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
                , terrainDetailUnsupported = true
            };
        }        
       
        protected override void Dispose(bool disposing)
        {
            GraphBuilder.Cleanup();
        }
    }
}