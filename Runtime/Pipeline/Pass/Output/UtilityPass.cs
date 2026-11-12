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
    internal static class UtilityPassUtilityData
    {
        internal static readonly int InfinityOutputTransferID = Shader.PropertyToID("_InfinityOutputTransfer");
    }

    public partial class InfinityRenderPipeline
    {
#if UNITY_EDITOR
        struct DisplayDepthPassData
        {
            public RGTextureHandle source;
            public int shaderPass;
        }

        void RasterUpscaleDepthForEditorOverlays(in CameraRecordContext context)
        {
            RGTextureHandle source = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var dimensions = context.dimensions;
            if (!dimensions.downscaled)
            {
                m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.DisplayDepthBuffer, source);
                return;
            }
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            descriptor.width = dimensions.displaySize.x;
            descriptor.height = dimensions.displaySize.y;
            descriptor.name = "DisplayDepthTexture";
            descriptor.sizingDomain = ETextureSizingDomain.Display;
            descriptor.viewDivisor = 1;
            RGTextureHandle output = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DisplayDepthBuffer, descriptor);
            using (var pass = m_RGBuilder.AddRasterPass<DisplayDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterUpscaleDepthForEditorOverlays)))
            {
                pass.SetDepthStencilAttachment(output, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref var data = ref pass.GetPassData();
                data.source = pass.ReadTexture(source);
                data.shaderPass = blitPasses.upscaleDepth;
                pass.SetExecuteFunc((in DisplayDepthPassData data, in RGRasterEncoder commands) =>
                {
                    commands.SetGlobalTexture(PassShaderIDs.InfinitySourceDepth, data.source);
                    commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, data.shaderPass);
                });
            }
        }

        // WireOverlay Graph
        struct WireOverlayPassData
        {
            public RendererList rendererList;
            public Vector4 screenParams;
        }
        
        void RasterWireOverlay(in CameraRecordContext context)
        {
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
            RGTextureHandle colorTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);

            RendererList wireOverlayRendererList = renderContext.scriptableRenderContext.CreateWireOverlayRendererList(context.camera);

            using (var passRef = m_RGBuilder.AddRasterPass<WireOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RasterWireOverlay)))
            {
                // Unity forbids drawing gizmos / wire overlay inside BeginRenderPass.
                passRef.EnableNativeRenderPass(false);
                passRef.SetColorAttachment(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref WireOverlayPassData passData = ref passRef.GetPassData();
                {
                    passData.rendererList = wireOverlayRendererList;
                    var size = context.dimensions.displaySize;
                    passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                }

                passRef.SetExecuteFunc((in WireOverlayPassData passData, in RGRasterEncoder cmdEncoder) =>
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

        void RasterGizmos(in CameraRecordContext context)
        {
            if (Handles.ShouldRenderGizmos())
            {
                RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
                RGTextureHandle colorTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);

                RendererList gizmosRendererList = renderContext.scriptableRenderContext.CreateGizmoRendererList(context.camera, GizmoSubset.PostImageEffects);

                using (var passRef = m_RGBuilder.AddRasterPass<GizmosPassData>(ProfilingSampler.Get(CustomSamplerId.RasterGizmos)))
                {
                    // Unity forbids drawing gizmos inside BeginRenderPass.
                    passRef.EnableNativeRenderPass(false);
                    passRef.SetColorAttachment(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                    ref GizmosPassData passData = ref passRef.GetPassData();
                    {
                        passData.rendererList = gizmosRendererList;
                        var size = context.dimensions.displaySize;
                        passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                    }

                    passRef.SetExecuteFunc((in GizmosPassData passData, in RGRasterEncoder cmdEncoder) =>
                    {
                        cmdEncoder.SetGlobalVector(PassShaderIDs.ScreenParams, passData.screenParams);
                        cmdEncoder.DrawRendererList(passData.rendererList);
                    });
                }
            }
        }
#endif

        struct FinalOutputPassData
        {
            public Rect viewport;
            public Vector4 scaleBias, outputTransfer;
            public int shaderPass, sourceWidth, sourceHeight;
            public RGTextureHandle srcTexture;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public RenderCaptureSession captureSession;
            public NativeDisplayProbe displayProbe;
#endif
        }

        void RecordCameraOutput(in CameraRecordContext context)
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
            bool linearCapture = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var capture = RenderCaptureService.current;
            linearCapture = capture != null && !capture.Finished && capture.request.Matches(context.camera) &&
                capture.Captures(ECaptureBuffer.DisplayColor | ECaptureBuffer.PostProcess);
#endif
            var plan = new OutputPlan(context.camera.cameraType == CameraType.Game && context.camera.targetTexture == null,
                context.outputDecision.policy, context.debugView != EDebugView.None, linearCapture, wire, gizmos);
            RGTextureHandle target = ImportCameraOutput(context);
            RasterPostProcessing(context, plan, target);
            if (plan.linearIntermediate)
            {
                ComputeDebugView(context);
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
                m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.DisplayColorBuffer, m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer));
                if (plan.uiOverlay)
                {
                    RasterUIOverlay(context, m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer), false);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                RecordNormalFrameCapture(context);
#endif
                RasterFinalOutput(context, m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer), target, true);
            }
            else
            {
                // Hardware sRGB targets decode before blending and encode after it.
                if (plan.uiOverlay)
                {
                    RasterUIOverlay(context, target, true);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                RecordNormalFrameCapture(context);
#endif
            }
            context.features.ThrowIfCannotProduce(EFrameFeature.Display);
            context.features.MarkProduced(EFrameFeature.Display);
        }

        RGTextureHandle ImportCameraOutput(in CameraRecordContext context)
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

        FinalOutputPassData PrepareFinalOutput(in CameraRecordContext context, RGTextureHandle source, bool finalTarget, int shaderPass)
        {
            var dimensions = context.dimensions;
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            var data = new FinalOutputPassData
            {
                srcTexture = source, sourceWidth = descriptor.width, sourceHeight = descriptor.height,
                viewport = finalTarget ? dimensions.outputViewport : new Rect(0, 0, dimensions.displaySize.x, dimensions.displaySize.y),
                scaleBias = finalTarget && context.camera.targetTexture == null ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0),
                outputTransfer = finalTarget ? new Vector4((int)context.outputDecision.policy, context.outputDecision.outputGamut == OutputTransformUtility.OutputGamutRec2020 ? 1 : 0, context.outputDecision.paperWhiteNits, 0)
                    : new Vector4((int)EOutputEncodePolicy.LinearTarget, 0, 100, 0),
                shaderPass = shaderPass
            };
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (finalTarget)
            {
                var capture = RenderCaptureService.current;
                data.captureSession = capture != null && !capture.Finished && capture.request.Matches(context.camera) ? capture : null;
                data.captureSession?.RecordOutputDecision(context.outputDecision);
                data.captureSession?.RecordPresent(context.camera, data.viewport, data.scaleBias, data.sourceWidth, data.sourceHeight, false);
                data.displayProbe = data.captureSession?.ReserveNativeProbe();
            }
#endif
            return data;
        }

        static void BindFinalOutput(in FinalOutputPassData data, in RGRasterEncoder commands)
        {
            commands.SetViewport(data.viewport);
            commands.SetGlobalVector(UtilityPassUtilityData.InfinityOutputTransferID, data.outputTransfer);
            commands.SetGlobalVector(InfinityShaderIDs.ScaleBias, data.scaleBias);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        static void CompleteFinalOutput(in FinalOutputPassData data, in RGRasterEncoder commands)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            data.captureSession?.RecordPresent(null, data.viewport, data.scaleBias, data.sourceWidth, data.sourceHeight, true);
            if (data.displayProbe != null)
                commands.IssuePluginEventAndData(data.displayProbe.callback, 0, data.displayProbe.payload);
#endif
        }

        void RasterFinalOutput(in CameraRecordContext context, RGTextureHandle source, RGTextureHandle target, bool finalTarget)
        {
            var sampler = finalTarget ? CustomSamplerId.RasterFinalOutput : CustomSamplerId.RasterOutputCopy;
            using (var pass = m_RGBuilder.AddRasterPass<FinalOutputPassData>(ProfilingSampler.Get(sampler)))
            {
                pass.EnablePassCulling(false);
                if (finalTarget)
                {
                    pass.EnableNativeRenderPass(false);
                }
                pass.SetColorAttachment(target, 0, finalTarget && !OutputPlan.CoversTarget(context.camera.rect) ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                ref var data = ref pass.GetPassData();
                data = PrepareFinalOutput(context, source, finalTarget, blitPasses.finalOutput);
                data.srcTexture = pass.ReadTexture(source);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (data.displayProbe != null)
                {
                    pass.SetQueueObserver(data.displayProbe);
                }
#endif
                pass.SetExecuteFunc((in FinalOutputPassData data, in RGRasterEncoder commands) =>
                {
                    BindFinalOutput(data, commands);
                    commands.SetGlobalTexture(InfinityShaderIDs.MainTexture, data.srcTexture);
                    commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, data.shaderPass);
                    CompleteFinalOutput(data, commands);
                });
            }
        }
    }
}
