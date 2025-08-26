using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class GBufferPassUtilityData
    {
        internal static string TextureAName = "GBufferTextureA";
        internal static string TextureBName = "GBufferTextureB";
    }

    public partial class InfinityRenderPipeline
    {
        struct GBufferPassData
        {
            public RendererList rendererList;
            public MeshPassProcessor meshPassProcessor;
        }

        void RenderGBuffer(RenderContext renderContext, Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor gbufferADsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                gbufferADsc.name = GBufferPassUtilityData.TextureAName;
                gbufferADsc.dimension = TextureDimension.Tex2D;
                gbufferADsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                gbufferADsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef gbufferTextureA = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferA, gbufferADsc);

            TextureDescriptor gbufferBDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                gbufferBDsc.name = GBufferPassUtilityData.TextureBName;
                gbufferBDsc.dimension = TextureDimension.Tex2D;
                gbufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                gbufferBDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef gbufferTextureB = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.GBufferPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList gbufferRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            //Add GBufferPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<GBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RenderGBuffer)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                
                // 使用新的API：通过访问标志自动推导Load/Store Action
                // WriteAll表示完全重写整个附件，无需保留之前内容
                passRef.SetColorAttachment(gbufferTextureA, 0, EColorAccessFlag.WriteAll);
                passRef.SetColorAttachment(gbufferTextureB, 1, EColorAccessFlag.WriteAll);
                
                // ReadWrite表示需要读取和写入深度值
                passRef.SetDepthStencilAttachment(depthTexture, EDepthAccessFlag.ReadWrite);

                ref GBufferPassData passData = ref passRef.GetPassData<GBufferPassData>();
                {
                    passData.rendererList = gbufferRendererList;
                    passData.meshPassProcessor = m_GBufferMeshProcessor;
                }
                m_GBufferMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(0, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in GBufferPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(cmdEncoder, 1);

                    //UnityDrawPipeline
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}