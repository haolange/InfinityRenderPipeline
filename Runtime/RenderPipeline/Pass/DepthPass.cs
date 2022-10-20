using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DepthPassUtilityData
    {
        internal static string PassName = "Depth";
        internal static string TextureName = "DepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct DepthPassData
        {
            public Camera camera;
            public RDGTextureRef depthTexture;
            public CullingResults cullingResults;
            public MeshPassProcessor meshPassProcessor;
        }

        void RenderDepth(Camera camera, in CullingDatas cullingDatas, in CullingResults cullingResults)
        {
            TextureDescriptor depthDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = DepthPassUtilityData.TextureName, depthBufferBits = EDepthBits.Depth32 };

            RDGTextureRef depthTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthDescriptor);

            //Add DepthPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<DepthPassData>(DepthPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderDepth)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.All, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

                ref DepthPassData passData = ref passRef.GetPassData<DepthPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_DepthMeshProcessor;
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                
                m_DepthMeshProcessor.DispatchSetup(cullingDatas, new MeshPassDescriptor(2450, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in DepthPassData passData, in RDGContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(graphContext, 0);

                    //UnityDrawPipeline
                    FilteringSettings filteringSettings = new FilteringSettings
                    {
                        renderingLayerMask = 1,
                        layerMask = passData.camera.cullingMask,
                        renderQueueRange = new RenderQueueRange(2450, 2999),
                    };
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.DepthPass, new SortingSettings(passData.camera) { criteria = SortingCriteria.QuantizedFrontToBack })
                    {
                        enableInstancing = true,
                        enableDynamicBatching = false
                    };
                    graphContext.renderContext.scriptableRenderContext.DrawRenderers(passData.cullingResults, ref drawingSettings, ref filteringSettings);
                });
            }
        }
    }
}