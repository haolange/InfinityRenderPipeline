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
        }

        void RenderUIOverlay(RenderContext renderContext, Camera camera)
        {
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
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
                passRef.SetExecuteFunc((in UIOverlayPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
