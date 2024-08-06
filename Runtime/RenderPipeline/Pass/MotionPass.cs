using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class MotionPassUtilityData
    {
        internal static string MotionTextureName = "MotionTexture";
        internal static string DepthTextureName = "MotionDepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ObjectMotionPassData
        {
            public RendererList rendererList;
        }

        struct CameraMotionPassData
        {
            public RGTextureRef depthTexture;
        }

        void RenderMotion(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

            TextureDescriptor motionDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = MotionPassUtilityData.MotionTextureName, colorFormat = GraphicsFormat.R16G16_SFloat, depthBufferBits = EDepthBits.None };

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef motionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionDescriptor);

            //Add ObjectMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ObjectMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderObjectMotion)))
            {
                //Setup Phase
                RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.MotionPass, cullingResults, camera);
                {
                    rendererListDesc.layerMask = camera.cullingMask;
                    rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                    rendererListDesc.sortingCriteria = SortingCriteria.CommonOpaque;
                    rendererListDesc.renderingLayerMask = 1 << 5;
                    rendererListDesc.rendererConfiguration = PerObjectData.MotionVectors;
                    rendererListDesc.excludeObjectMotionVectors = false;
                }

                ref ObjectMotionPassData passData = ref passRef.GetPassData<ObjectMotionPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.Write);
                passRef.SetExecuteFunc((in ObjectMotionPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    //UnityDrawPipeline
                    cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }

            //Add ObjectMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<CameraMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderCameraMotion)))
            {
                //Setup Phase
                ref CameraMotionPassData passData = ref passRef.GetPassData<CameraMotionPassData>();
                passData.depthTexture = depthTexture;

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);
                passRef.SetExecuteFunc((in CameraMotionPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    cmdBuffer.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.depthTexture);
                    cmdBuffer.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, 2);
                });
            }
        }

    }
}
