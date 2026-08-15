using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ForwardPassUtilityData
    {
        internal static string TextureName = "LightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ForwardPassData
        {
            public RendererList rendererList;
            public RGDrawListRef draws;
        }

        void RenderForward(RenderContext renderContext, Camera camera, MeshVisibilityHandle visibility, in CullingResults cullingResults)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor lightingTextureDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                lightingTextureDsc.name = ForwardPassUtilityData.TextureName;
                lightingTextureDsc.dimension = TextureDimension.Tex2D;
                lightingTextureDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                lightingTextureDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef lightingTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, lightingTextureDsc);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.ForwardPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.OptimizeStateChanges;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList forwardRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            MeshFilterProgram forwardFilter = BuiltinMeshesPasses.Forward.defaultFilter;
            forwardFilter.layerMask = camera.cullingMask;
            forwardFilter.renderingLayerMask = (uint)ERenderingLayer.Everything;
            var forwardRequest = new MeshDrawRequest
            {
                filter = forwardFilter,
                sort = BuiltinMeshesPasses.Forward.defaultSort,
                backendPolicy = EMeshBackendPolicy.Auto,
                shaderPassIndex = BuiltinMeshesPasses.Forward.shaderPassIndex,
                viewPosition = camera.transform.position,
                renderingLayerMask = forwardFilter.renderingLayerMask,
                viewKey = UnityEntityId.ToUInt64(camera)
            };
            RGDrawListRef forwardDraws = m_RGBuilder.DeclareDrawList(m_ForwardMeshProcessor, forwardRequest, visibility, m_VisibilityShare);

            //Add ForwardPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(lightingTexture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);

                ref ForwardPassData passData = ref passRef.GetPassData<ForwardPassData>();
                {
                    passData.rendererList = forwardRendererList;
                    passData.draws = passRef.UseDrawList(forwardDraws);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ForwardPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    cmdEncoder.Draw(passData.draws);

                    //UnityDrawPipeline
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
