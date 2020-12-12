using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Runtime.Rendering.RDG;

namespace InfinityTech.Runtime.Rendering.Pipeline
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
            // AddPass
            GraphBuilder.AddRenderPass<GizmosPassData>("RenderGizmos", ProfilingSampler.Get(CustomSamplerId.RenderGizmos),
            (ref GizmosPassData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RenderCamera = RenderCamera;
                PassData.GizmoSubset = gizmoSubset;
            },
            (ref GizmosPassData PassData, RDGContext GraphContext) =>
            {
                GraphContext.RenderContext.DrawGizmos(PassData.RenderCamera, PassData.GizmoSubset);
            });
#endif
        }

        ///////////SkyBox Graph
        struct SkyAtmosphereData
        {
            public Camera RenderCamera;
        }

        void RenderSkyAtmosphere(Camera RenderCamera)
        {
            // AddPass
            GraphBuilder.AddRenderPass<SkyAtmosphereData>("RenderSkyAtmosphere", ProfilingSampler.Get(CustomSamplerId.SkyAtmosphere),
            (ref SkyAtmosphereData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RenderCamera = RenderCamera;
            },
            (ref SkyAtmosphereData PassData, RDGContext GraphContext) =>
            {
                GraphContext.RenderContext.DrawSkybox(PassData.RenderCamera);
            });
        }

        ///////////Present Graph
        struct PresentBlitData
        {
            public float2 CameraSize;
            public RDGTextureRef RT_Source;
            public RenderTargetIdentifier RT_Dest;
        }

        void RenderPresentBlit(Camera RenderCamera, RDGTextureRef SourceTexture, RenderTexture DestTexture)
        {
            // AddPass
            GraphBuilder.AddRenderPass<PresentBlitData>("PresentBuffer", ProfilingSampler.Get(CustomSamplerId.PresentBackBuffer),
            (ref PresentBlitData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.CameraSize = new float2(RenderCamera.pixelWidth, RenderCamera.pixelHeight);
                PassData.RT_Source = PassBuilder.ReadTexture(SourceTexture);
                PassData.RT_Dest = new RenderTargetIdentifier(DestTexture);
            },
            (ref PresentBlitData PassData, RDGContext GraphContext) =>
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
        public static RendererListDesc CreateRendererListDesc(CullingResults CullingData, Camera RenderCamera, ShaderTagId PassName, PerObjectData rendererConfiguration = 0, RenderQueueRange? renderQueueRange = null, RenderStateBlock? stateBlock = null, Material overrideMaterial = null, bool excludeObjectMotionVectors = false)
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
