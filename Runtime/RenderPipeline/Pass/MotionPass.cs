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
            public RGTextureRef depthTexture;
            public RGTextureRef motionTexture;
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
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<MotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderMotionObject)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.Color, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

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
                passData.motionTexture = passRef.UseColorBuffer(motionTexture, 0);
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passRef.SetExecuteFunc((in MotionPassData passData, in RGContext graphContext) =>
                {
                    //UnityDrawPipeline
                    graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }

            //Add CopyMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<MotionPassData>(ProfilingSampler.Get(CustomSamplerId.CopyMotionDepth)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                ref MotionPassData passData = ref passRef.GetPassData<MotionPassData>();
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.copyDepthTexture = passRef.WriteTexture(copyDepthTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in MotionPassData passData, in RGContext graphContext) =>
                {
                    //graphContext.cmdBuffer.Blit(passData.depthTexture, passData.copyDepthTexture);
                    graphContext.cmdBuffer.CopyTexture(passData.depthTexture, passData.copyDepthTexture);
                });
            }

            //Add ObjectMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<MotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderMotionCamera)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                //Setup Phase
                ref MotionPassData passData = ref passRef.GetPassData<MotionPassData>();
                passData.copyDepthTexture = passRef.ReadTexture(copyDepthTexture);
                passData.motionTexture = passRef.UseColorBuffer(motionTexture, 0);
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passRef.SetExecuteFunc((in MotionPassData passData, in RGContext graphContext) =>
                {
                    graphContext.cmdBuffer.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.copyDepthTexture);
                    graphContext.cmdBuffer.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, 2);
                });
            }
        }

    }
}
