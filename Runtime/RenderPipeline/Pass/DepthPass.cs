using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FDepthPassUtilityData
    {
        internal static string PassName = "Depth";
        internal static string TextureName = "DepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FDepthPassData
        {
            public Camera camera;
            public FRDGTextureRef depthTexture;
            public CullingResults cullingResults;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderDepth(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            FTextureDescriptor depthDescriptor = new FTextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = FDepthPassUtilityData.TextureName, depthBufferBits = EDepthBits.Depth32 };

            FRDGTextureRef depthTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.DepthBuffer, depthDescriptor);

            //Add DepthPass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<FDepthPassData>(FDepthPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderDepth)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.All, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

                ref FDepthPassData passData = ref passRef.GetPassData<FDepthPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_DepthMeshProcessor;
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                
                m_DepthMeshProcessor.DispatchSetup(cullingData, new FMeshPassDescriptor(2450, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in FDepthPassData passData, in FRDGContext graphContext) =>
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