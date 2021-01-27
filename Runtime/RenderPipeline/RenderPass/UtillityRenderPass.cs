using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {

        ///////////Gizmos Graph
        struct GizmosPassData
        {
        #if UNITY_EDITOR
            public Camera RenderCamera;
            public GizmoSubset GizmoSubset;
        #endif
        }

        void RenderGizmo(Camera RenderCamera, GizmoSubset gizmoSubset)
        {
#if UNITY_EDITOR
            if (Handles.ShouldRenderGizmos())
            {
                // Add GizmosPass
                GraphBuilder.AddPass<GizmosPassData>("Gizmos", ProfilingSampler.Get(CustomSamplerId.Gizmos),
                (ref GizmosPassData PassData, ref RDGPassBuilder PassBuilder) =>
                {
                    PassData.RenderCamera = RenderCamera;
                    PassData.GizmoSubset = gizmoSubset;
                },
                (ref GizmosPassData PassData, RDGContext GraphContext) =>
                {
                    GraphContext.RenderContext.DrawGizmos(PassData.RenderCamera, PassData.GizmoSubset);
                });
            }
#endif
        }

        ///////////SkyBox Graph
        struct SkyBoxData
        {
            public Camera RenderCamera;
        }

        void RenderSkyBox(Camera RenderCamera)
        {
            // Add SkyAtmospherePass
            GraphBuilder.AddPass<SkyBoxData>("SkyBox", ProfilingSampler.Get(CustomSamplerId.SkyBox),
            (ref SkyBoxData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RenderCamera = RenderCamera;
            },
            (ref SkyBoxData PassData, RDGContext GraphContext) =>
            {
                GraphContext.RenderContext.DrawSkybox(PassData.RenderCamera);
            });
        }

        ///////////Present Graph
        struct PresentViewData
        {
            public float2 CameraSize;
            public RDGTextureRef RT_Source;
            public RenderTargetIdentifier RT_Dest;
        }

        void RenderPresentView(Camera RenderCamera, RDGTextureRef SourceTexture, RenderTexture DestTexture)
        {
            // Add PresentPass
            GraphBuilder.AddPass<PresentViewData>("Present", ProfilingSampler.Get(CustomSamplerId.Present),
            (ref PresentViewData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.CameraSize = new float2(RenderCamera.pixelWidth, RenderCamera.pixelHeight);
                PassData.RT_Source = PassBuilder.ReadTexture(SourceTexture);
                PassData.RT_Dest = new RenderTargetIdentifier(DestTexture);
            },
            (ref PresentViewData PassData, RDGContext GraphContext) =>
            {
                RenderTexture RT_Source = PassData.RT_Source;

                /*float4 ScaleBias = new float4(PassData.CameraSize.x / RT_Source.width, PassData.CameraSize.y / RT_Source.height, 0.0f, 0.0f);
                if (DestTexture == null) {
                    ScaleBias.w = ScaleBias.y;
                    ScaleBias.y *= -1;
                }*/

                float4 ScaleBias = new float4(1, 1, 0.0f, 0.0f);
                if (DestTexture == null) {
                    ScaleBias.y *= -1;
                }

                GraphContext.CmdBuffer.SetGlobalVector(InfinityShaderIDs.BlitScaleBias, ScaleBias);
                GraphContext.CmdBuffer.DrawFullScreen(RT_Source, PassData.RT_Dest, 1);
            });
        }

        ///////////Mesh Batch
        public static RendererListDesc CreateRendererListDesc(CullingResults CullingData, Camera RenderCamera, ShaderTagId PassName, RenderQueueRange? renderQueueRange = null, PerObjectData rendererConfiguration = 0, bool excludeObjectMotionVectors = false, Material overrideMaterial = null, RenderStateBlock ? stateBlock = null)
        {
            RendererListDesc result = new RendererListDesc(PassName, CullingData, RenderCamera)
            {
                rendererConfiguration = rendererConfiguration,
                renderQueueRange = RenderQueueRange.opaque,
                sortingCriteria = SortingCriteria.CommonOpaque,
                stateBlock = stateBlock,
                overrideMaterial = overrideMaterial,
                excludeObjectMotionVectors = excludeObjectMotionVectors
            };
            return result;
        }

        public static void DrawRendererList(ScriptableRenderContext RenderContext, RendererList RendererList)
        {
            if (RendererList.stateBlock == null) {
                RenderContext.DrawRenderers(RendererList.cullingResult, ref RendererList.drawSettings, ref RendererList.filteringSettings);
            } else {
                var RenderStateBlock = RendererList.stateBlock.Value;
                RenderContext.DrawRenderers(RendererList.cullingResult, ref RendererList.drawSettings, ref RendererList.filteringSettings, ref RenderStateBlock);
            }
        }

    }
}
