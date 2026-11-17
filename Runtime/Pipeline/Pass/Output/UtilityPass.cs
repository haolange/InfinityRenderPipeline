using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RendererUtils;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    internal sealed class UtilityPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly PostProcessingPass m_PostProcessingPass;
        readonly UIOverlayPass m_UIOverlayPass;
        readonly FinalOutputPass m_FinalOutputPass;
#if UNITY_EDITOR
        // Only the Editor overlay depth reads a blit pass index.
        readonly InfinityRenderPipelineResources m_Resources;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly DebugViewPass m_DebugViewPass;
        readonly RenderCapturePass m_RenderCapturePass;
#endif

        internal UtilityPass(RGBuilder graph, InfinityRenderPipelineResources resources, PostProcessingPass postProcessingPass, UIOverlayPass uiOverlayPass,
            FinalOutputPass finalOutputPass
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            , DebugViewPass debugViewPass, RenderCapturePass renderCapturePass
#endif
            )
        {
            m_RGBuilder = graph;
            m_PostProcessingPass = postProcessingPass;
            m_UIOverlayPass = uiOverlayPass;
            m_FinalOutputPass = finalOutputPass;
#if UNITY_EDITOR
            m_Resources = resources;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_DebugViewPass = debugViewPass;
            m_RenderCapturePass = renderCapturePass;
#endif
        }

#if UNITY_EDITOR
        struct DisplayDepthPassData
        {
            public RGTextureHandle sourceHandle;
            public int shaderPass;
        }

        void RasterUpscaleDepthForEditorOverlays(in ViewContext context)
        {
            RGTextureHandle sourceHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var dimensions = context.dimensions;
            if (!dimensions.downscaled)
            {
                m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.DisplayDepthBuffer, sourceHandle);
                return;
            }
            var descriptor = m_RGBuilder.GetTextureDescriptor(sourceHandle);
            descriptor.width = dimensions.displaySize.x;
            descriptor.height = dimensions.displaySize.y;
            descriptor.name = "DisplayDepthTexture";
            descriptor.sizingDomain = ETextureSizingDomain.Display;
            descriptor.viewDivisor = 1;
            RGTextureHandle outputHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DisplayDepthBuffer, descriptor);
            {
                RGRasterPassHandle<DisplayDepthPassData> passHandle = m_RGBuilder.AddRasterPass<DisplayDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterUpscaleDepthForEditorOverlays));
                passHandle.SetDepthStencilAttachment(outputHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref DisplayDepthPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = passHandle.ReadTexture(sourceHandle);
                passData.shaderPass = m_Resources.blitPasses.upscaleDepth;
                passHandle.SetExecuteFunc((in DisplayDepthPassData passData, in RGRasterEncoder commands) =>
                {
                    commands.SetGlobalTexture(PassShaderIDs.InfinitySourceDepth, passData.sourceHandle);
                    commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, passData.shaderPass);
                });
            }
        }

        // WireOverlay Graph
        struct WireOverlayPassData
        {
            public RendererList rendererList;
            public Vector4 screenParams;
        }
        
        void RasterWireOverlay(in ViewContext context)
        {
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
            RGTextureHandle colorTextureHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.display);

            RendererList wireOverlayRendererList = context.scriptableRenderContext.CreateWireOverlayRendererList(context.camera);

            {
                RGRasterPassHandle<WireOverlayPassData> passHandle = m_RGBuilder.AddRasterPass<WireOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RasterWireOverlay));
                // Unity forbids drawing gizmos / wire overlay inside BeginRenderPass.
                passHandle.EnableNativeRenderPass(false);
                passHandle.SetColorAttachment(colorTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref WireOverlayPassData passData = ref passHandle.GetPassData();
                {
                    passData.rendererList = wireOverlayRendererList;
                    var size = context.dimensions.displaySize;
                    passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                }

                passHandle.SetExecuteFunc((in WireOverlayPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalVector(PassShaderIDs.ScreenParams, passData.screenParams);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
        
        // Gizmos Graph
        struct GizmosPassData
        {
            public RendererList rendererList;
            public Vector4 screenParams;
        }

        void RasterGizmos(in ViewContext context)
        {
            if (Handles.ShouldRenderGizmos())
            {
                RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
                RGTextureHandle colorTextureHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.display);

                RendererList gizmosRendererList = context.scriptableRenderContext.CreateGizmoRendererList(context.camera, GizmoSubset.PostImageEffects);

                {
                    RGRasterPassHandle<GizmosPassData> passHandle = m_RGBuilder.AddRasterPass<GizmosPassData>(ProfilingSampler.Get(CustomSamplerId.RasterGizmos));
                    // Unity forbids drawing gizmos inside BeginRenderPass.
                    passHandle.EnableNativeRenderPass(false);
                    passHandle.SetColorAttachment(colorTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                    ref GizmosPassData passData = ref passHandle.GetPassData();
                    {
                        passData.rendererList = gizmosRendererList;
                        var size = context.dimensions.displaySize;
                        passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                    }

                    passHandle.SetExecuteFunc((in GizmosPassData passData, in RGRasterEncoder cmdEncoder) =>
                    {
                        cmdEncoder.SetGlobalVector(PassShaderIDs.ScreenParams, passData.screenParams);
                        cmdEncoder.DrawRendererList(passData.rendererList);
                    });
                }
            }
        }
#endif

        internal void RecordCameraOutput(in ViewContext context)
        {
            bool wire = false, gizmos = false;
#if UNITY_EDITOR
            gizmos = Handles.ShouldRenderGizmos();
            for (int viewIndex = 0; viewIndex < SceneView.sceneViews.Count; ++viewIndex)
            {
                var view = (SceneView)SceneView.sceneViews[viewIndex];
                if (view.camera != context.camera)
                {
                    continue;
                }
                wire = view.cameraMode.drawMode == DrawCameraMode.TexturedWire || view.cameraMode.drawMode == DrawCameraMode.Wireframe;
                break;
            }
#endif
            bool debugOutput = false, linearCapture = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugOutput = context.debugView != EDebugView.None;
            var capture = RenderCaptureService.current;
            linearCapture = capture != null && !capture.Finished && capture.request.Matches(context.camera) &&
                capture.Captures(ECaptureBuffer.DisplayColor | ECaptureBuffer.PostProcess);
#endif
            var plan = new OutputPlan(context.camera.cameraType == CameraType.Game && context.camera.targetTexture == null,
                context.outputDecision.policy, debugOutput, linearCapture, wire, gizmos);
            RGTextureHandle targetHandle = ImportCameraOutput(context);
            m_PostProcessingPass.RasterPostProcessing(context, plan, targetHandle);
            if (plan.linearIntermediate)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                m_DebugViewPass.ComputeDebugView(context);
#endif
#if UNITY_EDITOR
                if (plan.editorDepth)
                {
                    RasterUpscaleDepthForEditorOverlays(context);
                }
                if (plan.wireOverlay)
                {
                    RasterWireOverlay(context);
                }
                if (plan.gizmos)
                {
                    RasterGizmos(context);
                }
#endif
                m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.DisplayColorBuffer, m_RGBuilder.scoper.QueryTexture(context.sceneColor.display));
                if (plan.uiOverlay)
                {
                    m_UIOverlayPass.RasterUIOverlay(context, m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer), false);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                m_RenderCapturePass.RecordNormalFrameCapture(context);
#endif
                m_FinalOutputPass.RasterFinalOutput(context, m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer), targetHandle, true);
            }
            else
            {
                // Hardware sRGB targets decode before blending and encode after it.
                if (plan.uiOverlay)
                {
                    m_UIOverlayPass.RasterUIOverlay(context, targetHandle, true);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                m_RenderCapturePass.RecordNormalFrameCapture(context);
#endif
            }
            context.features.ThrowIfCannotProduce(EFrameFeature.Display);
            context.features.MarkProduced(EFrameFeature.Display);
        }

        RGTextureHandle ImportCameraOutput(in ViewContext context)
        {
            RenderTexture texture = context.camera.targetTexture;
            var identifier = texture != null ? new RenderTargetIdentifier(texture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            // CameraTarget has no RenderTexture. The attachment size is this frame's display size;
            // a 0x0 import fails attachment validation and drops the present pass.
            int2 display = context.dimensions.displaySize;
            var descriptor = new InfinityTech.Rendering.GPUResource.TextureDescriptor
            {
                name = texture != null ? texture.name : "DisplayBackbuffer",
                width = texture != null ? texture.width : display.x, height = texture != null ? texture.height : display.y,
                colorFormat = context.outputDecision.backbufferFormat, dimension = TextureDimension.Tex2D, slices = 1
            };
            return m_RGBuilder.ImportBackbuffer(identifier, descriptor);
        }
    }
}
