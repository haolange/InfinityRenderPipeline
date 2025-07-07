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
            TextureDescriptor depthTextureDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                depthTextureDsc.name = DepthPassUtilityData.TextureName;
                depthTextureDsc.dimension = TextureDimension.Tex2D;
                depthTextureDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureRef depthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthTextureDsc);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.DepthPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(2450, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList depthRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            //Add DepthPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RenderDepth)))
            {
                //Setup Phase
                passRef.SetDepthStencilAttachment(depthTexture, EDepthAccess.Write);

                ref DepthPassData passData = ref passRef.GetPassData<DepthPassData>();
                {
                    passData.rendererList = depthRendererList;
                    passData.meshPassProcessor = m_DepthMeshProcessor;
                }
                m_DepthMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(2450, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in DepthPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(cmdEncoder, 0);

                    //UnityDrawPipeline
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}