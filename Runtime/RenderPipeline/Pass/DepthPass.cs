using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DepthPassUtilityData
    {
        internal static string PassName = "DepthPass";
        internal static string TextureName = "DepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct DepthPassData
        {
            public RendererList rendererList;
            public RDGTextureRef depthTexture;
            public MeshPassProcessor meshPassProcessor;
        }

        void RenderDepth(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            TextureDescriptor depthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = DepthPassUtilityData.TextureName, depthBufferBits = EDepthBits.Depth32 };

            RDGTextureRef depthTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthDescriptor);

            //Add DepthPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<DepthPassData>(DepthPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderDepth)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.All, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

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
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                
                m_DepthMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(2450, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in DepthPassData passData, in RDGContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(graphContext, 0);

                    //UnityDrawPipeline
                    graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}