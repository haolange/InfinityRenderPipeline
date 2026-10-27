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
    public partial class InfinityRenderPipeline
    {
#if UNITY_EDITOR
        struct DisplayDepthPassData
        {
            public RGTextureHandle source;
            public int shaderPass;
        }

        void RasterUpscaleDepthForEditorOverlays()
        {
            RGTextureHandle source = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var dimensions = m_ActiveFrameState.dimensions;
            if (!dimensions.downscaled)
            {
                m_RGScoper.RegisterTexture(InfinityShaderIDs.DisplayDepthBuffer, source);
                return;
            }
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            descriptor.width = dimensions.displaySize.x;
            descriptor.height = dimensions.displaySize.y;
            descriptor.name = "DisplayDepthTexture";
            descriptor.sizingDomain = ETextureSizingDomain.Display; descriptor.viewDivisor = 1;
            RGTextureHandle output = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DisplayDepthBuffer, descriptor);
            int shaderPass = GraphicsUtility.BlitMaterial.FindPass("RasterUpscaleDepth");
            if (shaderPass < 0) throw new System.InvalidOperationException("InfinityRP: required UpscaleDepth shader pass is missing.");
            using (var pass = m_RGBuilder.AddRasterPass<DisplayDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterUpscaleDepthForEditorOverlays)))
            {
                pass.SetDepthStencilAttachment(output, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref var data = ref pass.GetPassData<DisplayDepthPassData>();
                data.source = pass.ReadTexture(source);
                data.shaderPass = shaderPass;
                pass.SetExecuteFunc((in DisplayDepthPassData data, in RGRasterEncoder commands) =>
                {
                    commands.SetGlobalTexture(Shader.PropertyToID("_InfinitySourceDepth"), data.source);
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
        
        void RasterWireOverlay(RenderContext renderContext, Camera camera)
        {
            RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
            RGTextureHandle colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);

            RendererList wireOverlayRendererList = renderContext.scriptableRenderContext.CreateWireOverlayRendererList(camera);

            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<WireOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RasterWireOverlay)))
            {
                // Unity forbids drawing gizmos / wire overlay inside BeginRenderPass.
                passRef.EnableNativeRenderPass(false);
                passRef.SetColorAttachment(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref WireOverlayPassData passData = ref passRef.GetPassData<WireOverlayPassData>();
                {
                    passData.rendererList = wireOverlayRendererList;
                    var size = m_ActiveFrameState.dimensions.displaySize;
                    passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                }

                passRef.SetExecuteFunc((in WireOverlayPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ScreenParams"), passData.screenParams);
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

        void RasterGizmos(RenderContext renderContext, Camera camera)
        {
            if (Handles.ShouldRenderGizmos())
            {
                RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
                RGTextureHandle colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);

                RendererList gizmosRendererList = renderContext.scriptableRenderContext.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects);

                using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<GizmosPassData>(ProfilingSampler.Get(CustomSamplerId.RasterGizmos)))
                {
                    // Unity forbids drawing gizmos inside BeginRenderPass.
                    passRef.EnableNativeRenderPass(false);
                    passRef.SetColorAttachment(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                    ref GizmosPassData passData = ref passRef.GetPassData<GizmosPassData>();
                    {
                        passData.rendererList = gizmosRendererList;
                        var size = m_ActiveFrameState.dimensions.displaySize;
                        passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                    }

                    passRef.SetExecuteFunc((in GizmosPassData passData, in RGRasterEncoder cmdEncoder) =>
                    {
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ScreenParams"), passData.screenParams);
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
            public string targetIdentity;
            public RenderCaptureSession captureSession;
            public RGTextureHandle srcTexture;
            public NativeDisplayProbe displayProbe;
        }

        void RecordCameraOutput(RenderContext renderContext, Camera camera, CameraFrameState frameState, in OutputTransformDecision decision)
        {
            bool wire = false, gizmos = false;
#if UNITY_EDITOR
            gizmos = Handles.ShouldRenderGizmos();
            for (int viewIndex = 0; viewIndex < SceneView.sceneViews.Count; ++viewIndex)
            {
                var view = (SceneView)SceneView.sceneViews[viewIndex];
                if (view.camera != camera) continue;
                wire = view.cameraMode.drawMode == DrawCameraMode.TexturedWire || view.cameraMode.drawMode == DrawCameraMode.Wireframe;
                break;
            }
#endif
            var capture = RenderCaptureService.current;
            bool linearCapture = capture != null && !capture.Finished && capture.request.Matches(camera) &&
                (System.Array.IndexOf(capture.request.buffers, "DisplayColor") >= 0 || System.Array.IndexOf(capture.request.buffers, "PostProcess") >= 0);
            var plan = new FOutputPlan(camera.cameraType == CameraType.Game && camera.targetTexture == null,
                decision.policy, frameState.debugView != EDebugView.None, linearCapture, wire, gizmos);
            RGTextureHandle target = ImportCameraOutput(camera, decision);
            RasterPostProcessing(renderContext, camera, frameState, decision, plan, target);
            if (plan.linearIntermediate)
            {
                ComputeDebugView(renderContext, camera);
#if UNITY_EDITOR
                if (plan.editorDepth) RasterUpscaleDepthForEditorOverlays();
                if (plan.wireOverlay) RasterWireOverlay(renderContext, camera);
                if (plan.gizmos) RasterGizmos(renderContext, camera);
#endif
                m_RGScoper.RegisterTexture(InfinityShaderIDs.DisplayColorBuffer, m_RGScoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer));
                if (plan.uiOverlay) RasterUIOverlay(renderContext, camera, m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer), false);
                RecordNormalFrameCapture(camera);
                RasterFinalOutput(camera, decision, m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer), target, true);
            }
            else
            {
                // Hardware sRGB targets decode before blending and encode after it.
                if (plan.uiOverlay) RasterUIOverlay(renderContext, camera, target, true);
                RecordNormalFrameCapture(camera);
            }
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.Display);
            MarkFeatureProduced(EFrameFeature.Display);
        }

        RGTextureHandle ImportCameraOutput(Camera camera, in OutputTransformDecision decision)
        {
            RenderTexture texture = camera.targetTexture;
            var identifier = texture != null ? new RenderTargetIdentifier(texture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            // CameraTarget has no RenderTexture. The attachment size is this frame's display size;
            // a 0x0 import fails attachment validation and drops the present pass.
            int2 display = m_ActiveFrameState.dimensions.displaySize;
            var descriptor = new InfinityTech.Rendering.GPUResource.TextureDescriptor
            {
                name = texture != null ? texture.name : "DisplayBackbuffer",
                width = texture != null ? texture.width : display.x, height = texture != null ? texture.height : display.y,
                colorFormat = decision.backbufferFormat, dimension = TextureDimension.Tex2D, slices = 1
            };
            return m_RGBuilder.ImportBackbuffer(identifier, descriptor);
        }

        FinalOutputPassData PrepareFinalOutput(Camera camera, in OutputTransformDecision decision, RGTextureHandle source,
            bool finalTarget, string shaderPass)
        {
            var dimensions = m_ActiveFrameState.dimensions;
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            var data = new FinalOutputPassData
            {
                srcTexture = source, sourceWidth = descriptor.width, sourceHeight = descriptor.height,
                viewport = finalTarget ? dimensions.outputViewport : new Rect(0, 0, dimensions.displaySize.x, dimensions.displaySize.y),
                scaleBias = finalTarget && camera.targetTexture == null ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0),
                outputTransfer = finalTarget ? new Vector4((int)decision.policy, decision.outputGamut == OutputTransformUtility.OutputGamutRec2020 ? 1 : 0, decision.paperWhiteNits, 0)
                    : new Vector4((int)EOutputEncodePolicy.LinearTarget, 0, 100, 0),
                shaderPass = GraphicsUtility.BlitMaterial.FindPass(shaderPass)
            };
            if (data.shaderPass < 0) throw new System.InvalidOperationException("InfinityRP: required output shader pass is missing: " + shaderPass);
            if (finalTarget)
            {
                var capture = RenderCaptureService.current;
                data.captureSession = capture != null && !capture.Finished && capture.request.Matches(camera) ? capture : null;
                data.captureSession?.RecordOutputDecision(decision);
                data.targetIdentity = camera.targetTexture != null ? "RenderTexture:" + camera.targetTexture.GetEntityId() : "BuiltinRenderTextureType.CameraTarget";
                data.captureSession?.RecordPresent(camera, data.targetIdentity, data.viewport, data.scaleBias, data.sourceWidth, data.sourceHeight, false);
                data.displayProbe = data.captureSession?.ReserveNativeProbe();
            }
            return data;
        }

        static void BindFinalOutput(in FinalOutputPassData data, in RGRasterEncoder commands)
        {
            commands.SetViewport(data.viewport);
            commands.SetGlobalVector(Shader.PropertyToID("_InfinityOutputTransfer"), data.outputTransfer);
            commands.SetGlobalVector(InfinityShaderIDs.ScaleBias, data.scaleBias);
        }

        static void CompleteFinalOutput(in FinalOutputPassData data, in RGRasterEncoder commands)
        {
            data.captureSession?.RecordPresent(null, data.targetIdentity, data.viewport, data.scaleBias, data.sourceWidth, data.sourceHeight, true);
            if (data.displayProbe != null)
                commands.IssuePluginEventAndData(data.displayProbe.callback, 0, data.displayProbe.payload);
        }

        void RasterFinalOutput(Camera camera, in OutputTransformDecision decision, RGTextureHandle source, RGTextureHandle target, bool finalTarget)
        {
            var sampler = finalTarget ? CustomSamplerId.RasterFinalOutput : CustomSamplerId.RasterOutputCopy;
            using (var pass = m_RGBuilder.AddRasterPass<FinalOutputPassData>(ProfilingSampler.Get(sampler)))
            {
                pass.EnablePassCulling(false);
                if (finalTarget) pass.EnableNativeRenderPass(false);
                pass.SetColorAttachment(target, 0, finalTarget && !FOutputPlan.CoversTarget(camera.rect) ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                ref var data = ref pass.GetPassData<FinalOutputPassData>();
                data = PrepareFinalOutput(camera, decision, source, finalTarget, "RasterFinalOutput");
                data.srcTexture = pass.ReadTexture(source);
                if (data.displayProbe != null) pass.SetQueueObserver(data.displayProbe);
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
