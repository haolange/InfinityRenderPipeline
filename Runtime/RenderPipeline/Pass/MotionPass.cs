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
        struct MotionPassData
        {
            public RendererList rendererList;
            public RGTextureRef copyDepthTexture;
        }

        struct CopyMotionDepthPassData
        {
            public RGTextureRef depthTexture;
            public RGTextureRef copyDepthTexture;
        }

        void RenderMotion(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

            TextureDescriptor depthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = MotionPassUtilityData.DepthTextureName, depthBufferBits = EDepthBits.Depth32 };
            TextureDescriptor motionDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = MotionPassUtilityData.MotionTextureName, colorFormat = GraphicsFormat.R16G16_SFloat, depthBufferBits = EDepthBits.None };

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef motionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionDescriptor);
            RGTextureRef copyDepthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionDepthBuffer, depthDescriptor);

            //Add ObjectMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<MotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderObjectMotion)))
            {
                //Setup Phase
                passRef.UseDepthBuffer(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.Write);
                passRef.UseColorBuffer(motionTexture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);

                RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.MotionPass, cullingResults, camera);
                {
                    rendererListDesc.layerMask = camera.cullingMask;
                    rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                    rendererListDesc.sortingCriteria = SortingCriteria.CommonOpaque;
                    rendererListDesc.renderingLayerMask = 1 << 5;
                    rendererListDesc.rendererConfiguration = PerObjectData.MotionVectors;
                    rendererListDesc.excludeObjectMotionVectors = false;
                }

                ref MotionPassData passData = ref passRef.GetPassData<MotionPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

                //Execute Phase
                passRef.SetExecuteFunc((in MotionPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    //UnityDrawPipeline
                    cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }

            //Add CopyMotionPass
            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyMotionDepthPassData>(ProfilingSampler.Get(CustomSamplerId.CopyMotionDepth)))
            {
                //Setup Phase
                ref CopyMotionDepthPassData passData = ref passRef.GetPassData<CopyMotionDepthPassData>();
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.copyDepthTexture = passRef.WriteTexture(copyDepthTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in CopyMotionDepthPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    #if UNITY_EDITOR
                        cmdBuffer.DrawFullScreen(passData.depthTexture, passData.copyDepthTexture);
                    #else
                        cmdBuffer.CopyTexture(passData.depthTexture, passData.copyDepthTexture);
                    #endif
                });
            }

            //Add ObjectMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<MotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderCameraMotion)))
            {
                //Setup Phase
                passRef.UseDepthBuffer(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);
                passRef.UseColorBuffer(motionTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref MotionPassData passData = ref passRef.GetPassData<MotionPassData>();
                passData.copyDepthTexture = passRef.ReadTexture(copyDepthTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in MotionPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    cmdBuffer.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.copyDepthTexture);
                    cmdBuffer.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, 2);
                });
            }
        }

    }
}
