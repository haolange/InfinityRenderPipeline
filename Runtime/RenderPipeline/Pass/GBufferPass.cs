using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FGBufferPassUtilityData
    {
        internal static string PassName = "GBufferPass";
        internal static string TextureAName = "GBufferTextureA";
        internal static string TextureBName = "GBufferTextureB";
    }

    public partial class InfinityRenderPipeline
    {
        struct FGBufferPassData
        {
            public Camera camera;

            public FRDGTextureRef depthTexture;
            public FRDGTextureRef gbufferTextureA;
            public FRDGTextureRef gbufferTextureB;
            public CullingResults cullingResults;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderGBuffer(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            FTextureDescriptor gbufferADsc = new FTextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = FGBufferPassUtilityData.TextureAName, colorFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = EDepthBits.None };
            FTextureDescriptor gbufferBDsc = new FTextureDescriptor(camera.pixelWidth, camera.pixelHeight) { dimension = TextureDimension.Tex2D, name = FGBufferPassUtilityData.TextureBName, colorFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = EDepthBits.None };
                    
            FRDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            FRDGTextureRef gbufferTextureA = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferA, gbufferADsc);
            FRDGTextureRef gbufferTextureB = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);
            
            //Add GBufferPass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<FGBufferPassData>(FGBufferPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderGBuffer)))
            {
                //Setup Phase
                passRef.SetOption(ClearFlag.Color, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                ref FGBufferPassData passData = ref passRef.GetPassData<FGBufferPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_GBufferMeshProcessor;
                passData.gbufferTextureA = passRef.UseColorBuffer(gbufferTextureA, 0);
                passData.gbufferTextureB = passRef.UseColorBuffer(gbufferTextureB, 1);
                passData.depthTexture = passRef.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                
                m_GBufferMeshProcessor.DispatchSetup(cullingData, new FMeshPassDescriptor(0, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((in FGBufferPassData passData, in FRDGContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(graphContext, 1);

                    //UnityDrawPipeline
                    FilteringSettings filteringSettings = new FilteringSettings
                    {
                        renderingLayerMask = 1,
                        layerMask = passData.camera.cullingMask,
                        renderQueueRange = new RenderQueueRange(0, 2999),
                    };
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.GBufferPass, new SortingSettings(passData.camera) { criteria = SortingCriteria.QuantizedFrontToBack })
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