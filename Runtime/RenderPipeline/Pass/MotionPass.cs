using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
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
        internal static string ObjectPassName = "ObjectMotionPass";
        internal static string CopyPassName = "ObjectMotionPass";
        internal static string CameraPassName = "CameraMotionPass";
    }

    public partial class InfinityRenderPipeline
    {
        struct MotionPassData
        {
            public RendererList rendererList;
            public RDGTextureRef depthTexture;
            public RDGTextureRef motionTexture;
            public RDGTextureRef copyDepthTexture;
        }


        void RenderMotion(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

            TextureDescriptor depthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = MotionPassUtilityData.DepthTextureName, depthBufferBits = EDepthBits.Depth32 };
            TextureDescriptor motionDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = MotionPassUtilityData.MotionTextureName, colorFormat = GraphicsFormat.R16G16_SFloat, depthBufferBits = EDepthBits.None };

            RDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef motionTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionDescriptor);
            RDGTextureRef copyDepthTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionDepthBuffer, depthDescriptor);

            //Add ObjectMotionPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<MotionPassData>(MotionPassUtilityData.ObjectPassName, ProfilingSampler.Get(CustomSamplerId.RenderMotionObject)))
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
                passRef.SetExecuteFunc((in MotionPassData passData, in RDGContext graphContext) =>
                {
                    //UnityDrawPipeline
                    graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }

            //Add CopyMotionPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<MotionPassData>(MotionPassUtilityData.CopyPassName, ProfilingSampler.Get(CustomSamplerId.CopyMotionDepth)))
            {
                //Setup Phase
                ref MotionPassData passData = ref passRef.GetPassData<MotionPassData>();
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.copyDepthTexture = passRef.WriteTexture(copyDepthTexture);

                //Execute Phase
                passRef.SetExecuteFunc((in MotionPassData passData, in RDGContext graphContext) =>
                {
                    graphContext.cmdBuffer.CopyTexture(passData.depthTexture, passData.copyDepthTexture);
                });
            }

            //Add ObjectMotionPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<MotionPassData>(MotionPassUtilityData.CameraPassName, ProfilingSampler.Get(CustomSamplerId.RenderMotionCamera)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.None, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                //Setup Phase
                ref MotionPassData passData = ref passRef.GetPassData<MotionPassData>();
                passData.copyDepthTexture = passRef.ReadTexture(copyDepthTexture);
                passData.motionTexture = passRef.UseColorBuffer(motionTexture, 0);
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);

                //Execute Phase
                passRef.SetExecuteFunc((in MotionPassData passData, in RDGContext graphContext) =>
                {
                    graphContext.cmdBuffer.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.copyDepthTexture);
                    graphContext.cmdBuffer.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, 2);
                });
            }
        }

    }
}
