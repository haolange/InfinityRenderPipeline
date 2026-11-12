using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct UIOverlayPassData
        {
            public RendererList rendererList;
            public Vector4 screenParams;
            public Rect viewport;
        }

        void RasterUIOverlay(in CameraRecordContext context, RGTextureHandle displayColor, bool finalTarget)
        {
            if (context.camera.cameraType != CameraType.Game || context.camera.targetTexture != null)
            {
                return;
            }

            RendererList overlay = renderContext.scriptableRenderContext.CreateUIOverlayRendererList(context.camera);
            using (var passRef = m_RGBuilder.AddRasterPass<UIOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RasterUIOverlay)))
            {
                passRef.EnablePassCulling(false);
                passRef.EnableNativeRenderPass(false);
                passRef.SetColorAttachment(displayColor, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref UIOverlayPassData passData = ref passRef.GetPassData();
                passData.rendererList = overlay;
                var size = context.dimensions.displaySize;
                passData.viewport = finalTarget ? context.dimensions.outputViewport : new Rect(0, 0, size.x, size.y);
                passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                passRef.SetExecuteFunc((in UIOverlayPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetViewport(passData.viewport);
                    cmdEncoder.SetGlobalVector(PassShaderIDs.ScreenParams, passData.screenParams);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
