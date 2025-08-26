using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;
using System.Xml.Linq;

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

            //Add ForwardPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                
                // 使用新的API：WriteAll表示完全重写颜色附件
                passRef.SetColorAttachment(lightingTexture, 0, EColorAccessFlag.WriteAll);
                
                // ReadOnly表示只读深度，用于深度测试但不写入
                passRef.SetDepthStencilAttachment(depthTexture, EDepthAccessFlag.ReadOnly);

                ref ForwardPassData passData = ref passRef.GetPassData<ForwardPassData>();
                {
                    passData.rendererList = forwardRendererList;
                    passData.meshPassProcessor = m_ForwardMeshProcessor;
                }
                m_ForwardMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(0, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in ForwardPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(cmdEncoder, 2);

                    //UnityDrawPipeline
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
