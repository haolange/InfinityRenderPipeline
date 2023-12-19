using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ForwardPassUtilityData
    {
        internal static string PassName = "ForwardPass";
        internal static string TextureName = "LightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ForwardPassData
        {
            public RendererList rendererList;
            public RDGTextureRef depthTexture;
            public RDGTextureRef lightingTexture;
            public MeshPassProcessor meshPassProcessor;
        }

        void RenderForward(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            TextureDescriptor textureDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = ForwardPassUtilityData.TextureName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None };

            RDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef lightingTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, textureDescriptor);

            //Add ForwardPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<ForwardPassData>(ForwardPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.Color, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

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
                passData.lightingTexture = passRef.UseColorBuffer(lightingTexture, 0);
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);
                
                m_ForwardMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(0, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in ForwardPassData passData, in RDGContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(graphContext, 2);

                    //UnityDrawPipeline
                    graphContext.cmdBuffer.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
