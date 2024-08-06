using UnityEngine;
using UnityEngine.Rendering;
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
            public MeshPassProcessor meshPassProcessor;
        }

        void RenderForward(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            TextureDescriptor textureDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = ForwardPassUtilityData.TextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None };

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef lightingTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, textureDescriptor);

            //Add ForwardPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                //Setup Phase
                RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.ForwardPass, cullingResults, camera);
                {
                    rendererListDesc.layerMask = camera.cullingMask;
                    rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                    rendererListDesc.sortingCriteria = SortingCriteria.OptimizeStateChanges;
                    rendererListDesc.renderingLayerMask = 1;
                    rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume;
                    rendererListDesc.excludeObjectMotionVectors = false;
                }

                ref ForwardPassData passData = ref passRef.GetPassData<ForwardPassData>();
                passData.rendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);
                passData.meshPassProcessor = m_ForwardMeshProcessor;
                
                m_ForwardMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(0, 2999));

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(lightingTexture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);
                passRef.SetExecuteFunc((in ForwardPassData passData, CommandBuffer cmdBuffer, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(cmdBuffer, 2);

                    //UnityDrawPipeline
                    cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
