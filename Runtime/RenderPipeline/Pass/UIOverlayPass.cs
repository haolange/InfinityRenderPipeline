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
        }

        void RenderUIOverlay(RenderContext renderContext, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || camera.targetTexture != null)
            {
                return;
            }

            RendererList overlay = renderContext.scriptableRenderContext.CreateUIOverlayRendererList(camera);
            RGTextureRef displayColor = m_RGScoper.QueryTexture(InfinityShaderIDs.DisplayColorBuffer);
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<UIOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RenderUIOverlay)))
            {
                passRef.EnablePassCulling(false);
                passRef.EnableNativeRenderPass(false);
                passRef.SetColorAttachment(displayColor, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref UIOverlayPassData passData = ref passRef.GetPassData<UIOverlayPassData>();
                passData.rendererList = overlay;
                var size = m_ActiveFrameState.dimensions.displaySize;
                passData.screenParams = new Vector4(size.x, size.y, 1f + 1f / size.x, 1f + 1f / size.y);
                passRef.SetExecuteFunc((in UIOverlayPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ScreenParams"), passData.screenParams);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
