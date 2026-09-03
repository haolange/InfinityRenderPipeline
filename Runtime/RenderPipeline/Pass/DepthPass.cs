using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
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
            public RGDrawListRef draws;
        }

        void RenderDepth(RenderContext renderContext, Camera camera, MeshVisibilityHandle visibility, in CullingResults cullingResults)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.Depth);
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

            MeshFilterProgram depthFilter = BuiltinMeshesPasses.Depth.defaultFilter;
            depthFilter.layerMask = camera.cullingMask;
            depthFilter.renderingLayerMask = (uint)ERenderingLayer.Everything;
            var depthRequest = new MeshDrawRequest
            {
                filter = depthFilter,
                sort = BuiltinMeshesPasses.Depth.defaultSort,
                backendPolicy = EMeshBackendPolicy.Auto,
                shaderPassIndex = BuiltinMeshesPasses.Depth.shaderPassIndex,
                viewPosition = camera.transform.position,
                renderingLayerMask = depthFilter.renderingLayerMask,
                viewKey = UnityEntityId.ToUInt64(camera)
            };
            RGDrawListRef depthDraws = m_RGBuilder.DeclareDrawList(m_DepthMeshProcessor, depthRequest, visibility, m_VisibilityShare);

            //Add DepthPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<DepthPassData>(ProfilingSampler.Get(CustomSamplerId.RenderDepth)))
            {
                //Setup Phase
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref DepthPassData passData = ref passRef.GetPassData<DepthPassData>();
                {
                    passData.rendererList = depthRendererList;
                    passData.draws = passRef.UseDrawList(depthDraws);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in DepthPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    cmdEncoder.Draw(passData.draws);

                    //UnityDrawPipeline
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            MarkFeatureProduced(EFrameFeature.Depth);
        }
    }
}
