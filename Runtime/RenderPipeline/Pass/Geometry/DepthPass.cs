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
            public RGDrawListRef draws;
            public RGRendererListRef rendererList;
        }

        void RenderDepth(RenderContext renderContext, Camera camera, in MeshView view, in CullingResults cullingResults)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.Depth);
            TextureDescriptor depthTextureDsc = new TextureDescriptor(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y);
            {
                depthTextureDsc.name = DepthPassUtilityData.TextureName;
                depthTextureDsc.dimension = TextureDimension.Tex2D;
                depthTextureDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureRef depthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthTextureDsc);
            RGDrawListRef depthDraws = m_RGBuilder.CreateDrawList(view, MeshPassId.Depth);
            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.DepthPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListRef depthRendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RenderDepth)))
            {
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref DepthPassData passData = ref passRef.GetPassData<DepthPassData>();
                passData.draws = passRef.UseDrawList(depthDraws);
                passData.rendererList = passRef.UseRendererList(depthRendererList);

                passRef.SetExecuteFunc((in DepthPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.Draw(passData.draws);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            MarkFeatureProduced(EFrameFeature.Depth);
        }
    }
}
