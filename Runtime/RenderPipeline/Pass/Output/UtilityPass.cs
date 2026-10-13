using UnityEngine;
using Unity.Mathematics;
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
            public RGTextureRef source;
            public int shaderPass;
        }

        void ResolveDisplayDepth()
        {
            RGTextureRef source = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
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
            RGTextureRef output = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DisplayDepthBuffer, descriptor);
            int shaderPass = GraphicsUtility.BlitMaterial.FindPass("UpscaleDepth");
            if (shaderPass < 0) throw new System.InvalidOperationException("InfinityRP: required UpscaleDepth shader pass is missing.");
            using (var pass = m_RGBuilder.AddRasterPass<DisplayDepthPassData>(ProfilingSampler.Get(CustomSamplerId.ResolveDisplayDepth)))
            {
                pass.SetDepthStencilAttachment(output, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, EDepthAccess.Write);
                ref var data = ref pass.GetPassData<DisplayDepthPassData>();
                data.source = pass.ReadTexture(source);
                data.shaderPass = shaderPass;
                pass.SetExecuteFunc((in DisplayDepthPassData data, in RGRasterEncoder commands, RGObjectPool pool) =>
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
        
        void RenderWireOverlay(RenderContext renderContext, Camera camera)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
            RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);

            RendererList wireOverlayRendererList = renderContext.scriptableRenderContext.CreateWireOverlayRendererList(camera);

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<WireOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RenderWireOverlay)))
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

                passRef.SetExecuteFunc((in WireOverlayPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
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

        void RenderGizmos(RenderContext renderContext, Camera camera)
        {
            if (Handles.ShouldRenderGizmos())
            {
                RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayDepthBuffer);
                RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);

                RendererList gizmosRendererList = renderContext.scriptableRenderContext.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects);

                using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<GizmosPassData>(ProfilingSampler.Get(CustomSamplerId.RenderGizmos)))
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

                    passRef.SetExecuteFunc((in GizmosPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                    {
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ScreenParams"), passData.screenParams);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                    });
                }
            }
        }
#endif

        // Present Graph
        struct PresentPassData
        {
            public Rect viewport;
            public Vector4 scaleBias;
            public Vector4 outputTransfer;
            public int shaderPass;
            public int sourceWidth, sourceHeight;
            public string targetIdentity;
            public RenderCaptureSession captureSession;
            public RGTextureRef srcTexture;
            public NativeDisplayProbe displayProbe;
        }

        void RenderPresent(RenderContext renderContext, Camera camera, in OutputTransformDecision decision)
        {
            var captureSession = RenderCaptureService.current;
            if (captureSession != null && !captureSession.request.Matches(camera)) captureSession = null;
            captureSession?.RecordOutputDecision(decision);
            RGTextureRef srcTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer);
            RenderTexture targetTexture = camera.targetTexture;
            var targetIdentifier = targetTexture != null
                ? new RenderTargetIdentifier(targetTexture)
                : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            var targetDescriptor = new InfinityTech.Rendering.GPUResource.TextureDescriptor
            {
                name = targetTexture != null ? targetTexture.name : "DisplayBackbuffer",
                width = targetTexture != null ? targetTexture.width : 0,
                height = targetTexture != null ? targetTexture.height : 0,
                colorFormat = decision.backbufferFormat,
                dimension = TextureDimension.Tex2D,
                slices = 1
            };
            RGTextureRef backbufferTexture = m_RGBuilder.ImportBackbuffer(targetIdentifier, targetDescriptor);

            // Add PresentPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<PresentPassData>(ProfilingSampler.Get(CustomSamplerId.Present)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                // The backbuffer has no owning RenderTexture, so it cannot be a native render pass attachment.
                passRef.EnableNativeRenderPass(false);
                passRef.SetColorAttachment(backbufferTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref PresentPassData passData = ref passRef.GetPassData<PresentPassData>();
                {
                    var sourceDescriptor = m_RGBuilder.GetTextureDescriptor(srcTexture);
                    var dimensions = m_ActiveFrameState.dimensions;
                    passData.viewport = dimensions.outputViewport;
                    passData.outputTransfer = new Vector4((int)decision.policy, decision.outputGamut == OutputTransformUtility.OutputGamutRec2020 ? 1 : 0, 100f, 0);
                    passData.shaderPass = GraphicsUtility.BlitMaterial.FindPass("Present");
                    if (passData.shaderPass < 0) throw new System.InvalidOperationException("InfinityRP: required Present shader pass is missing.");
                    passData.scaleBias = new Vector4(dimensions.displaySize.x / (float)sourceDescriptor.width,
                        dimensions.displaySize.y / (float)sourceDescriptor.height, 0, 0);
                    if (camera.targetTexture == null)
                    {
                        passData.scaleBias.w = passData.scaleBias.y;
                        passData.scaleBias.y = -passData.scaleBias.y;
                    }
                    passData.sourceWidth = sourceDescriptor.width;
                    passData.sourceHeight = sourceDescriptor.height;
                    passData.targetIdentity = targetTexture != null ? "RenderTexture:" + targetTexture.GetEntityId() : "BuiltinRenderTextureType.CameraTarget";
                    passData.captureSession = captureSession;
                    captureSession?.RecordPresent(camera, passData.targetIdentity, passData.viewport, passData.scaleBias, passData.sourceWidth, passData.sourceHeight, false);
                    passData.srcTexture = passRef.ReadTexture(srcTexture);
                    passData.displayProbe = RenderCaptureService.current?.ReserveNativeProbe();
                    passRef.SetQueueObserver(passData.displayProbe);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in PresentPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    RenderTexture srcBuffer = passData.srcTexture;
                    cmdEncoder.SetViewport(passData.viewport);
                    cmdEncoder.SetGlobalVector(Shader.PropertyToID("_InfinityOutputTransfer"), passData.outputTransfer);
                    cmdEncoder.SetGlobalVector(InfinityShaderIDs.ScaleBias, passData.scaleBias);
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.MainTexture, srcBuffer);
                    cmdEncoder.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, passData.shaderPass);
                    passData.captureSession?.RecordPresent(null, passData.targetIdentity, passData.viewport, passData.scaleBias, passData.sourceWidth, passData.sourceHeight, true);
                    if (passData.displayProbe != null)
                    {
                        cmdEncoder.IssuePluginEventAndData(passData.displayProbe.callback, 0, passData.displayProbe.payload);
                    }
                });
            }
        }
    }
}
