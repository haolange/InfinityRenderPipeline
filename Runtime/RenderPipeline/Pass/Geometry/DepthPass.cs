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
            public RGMeshDrawListHandle meshDrawList;
            public RGRendererListHandle rendererList;
        }

        void RasterDepth(RenderContext renderContext, Camera camera, in MeshView view, in CullingResults cullingResults)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.Depth);
            TextureDescriptor depthTextureDsc = new TextureDescriptor(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                depthTextureDsc.name = DepthPassUtilityData.TextureName;
                depthTextureDsc.dimension = TextureDimension.Tex2D;
                depthTextureDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureHandle depthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthTextureDsc);
            RGMeshDrawListHandle depthMeshDrawList = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterDepthPass));
            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.RasterDepthPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListHandle depthRendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDepth)))
            {
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref DepthPassData passData = ref passRef.GetPassData<DepthPassData>();
                passData.meshDrawList = passRef.UseMeshDrawList(depthMeshDrawList);
                passData.rendererList = passRef.UseRendererList(depthRendererList);

                passRef.SetExecuteFunc((in DepthPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawList);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            MarkFeatureProduced(EFrameFeature.Depth);
        }
    }
}
