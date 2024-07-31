using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DepthPassUtilityData
    {
        internal static string TextureName = "DepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct DepthPassData
        {
            public RendererList rendererList;
            public MeshPassProcessor meshPassProcessor;
        }

        void RenderDepth(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            TextureDescriptor depthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = DepthPassUtilityData.TextureName, depthBufferBits = EDepthBits.Depth32 };

            RGTextureRef depthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthDescriptor);

            //Add DepthPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RenderDepth)))
            {
                //Setup Phase
                passRef.UseDepthBuffer(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.DepthPass, cullingResults, camera);
                {
                    rendererListDesc.layerMask = camera.cullingMask;
                    rendererListDesc.renderQueueRange = new RenderQueueRange(2450, 2999);
                    rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                    rendererListDesc.renderingLayerMask = 1;
                    rendererListDesc.rendererConfiguration = PerObjectData.None;
                    rendererListDesc.excludeObjectMotionVectors = false;
                }

                ref DepthPassData passData = ref passRef.GetPassData<DepthPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);
                passData.meshPassProcessor = m_DepthMeshProcessor;
                
                m_DepthMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(2450, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in DepthPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(cmdBuffer, 0);

                    //UnityDrawPipeline
                    cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}