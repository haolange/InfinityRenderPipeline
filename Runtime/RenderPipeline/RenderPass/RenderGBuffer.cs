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
        internal static string TextureCName = "GBufferTextureC";
    }

    public partial class InfinityRenderPipeline
    {
        struct FGBufferPassData
        {
            public Camera camera;
            public FRDGTextureRef gbufferA;
            public FRDGTextureRef gbufferB;
            public FRDGTextureRef gbufferC;
            public FRDGTextureRef depthBuffer;
            public CullingResults cullingResults;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderGBuffer(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            FTextureDescription gbufferADsc = new FTextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FGBufferPassUtilityData.TextureAName, colorFormat = SystemInfo.IsFormatSupported(GraphicsFormat.B5G6R5_UNormPack16, FormatUsage.Render) ? GraphicsFormat.B5G6R5_UNormPack16 : GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = EDepthBits.None };
            FTextureDescription gbufferBDsc = new FTextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FGBufferPassUtilityData.TextureBName, colorFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = EDepthBits.None };
            FTextureDescription gbufferCDsc = new FTextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FGBufferPassUtilityData.TextureCName, colorFormat = GraphicsFormat.R8G8_UNorm, depthBufferBits = EDepthBits.None };

            FRDGTextureRef depthBuffer = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            FRDGTextureRef gbufferA = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferA, gbufferADsc);
            FRDGTextureRef gbufferB = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);
            FRDGTextureRef gbufferC = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferC, gbufferCDsc);
            
            //Add GBufferPass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<FGBufferPassData>(FGBufferPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderGBuffer)))
            {
                //Setup Phase
                ref FGBufferPassData passData = ref passRef.GetPassData<FGBufferPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_GBufferMeshProcessor;
                passData.gbufferA = passRef.UseColorBuffer(gbufferA, 0);
                passData.gbufferB = passRef.UseColorBuffer(gbufferB, 1);
                passData.gbufferC = passRef.UseColorBuffer(gbufferC, 2);
                passData.depthBuffer = passRef.UseDepthBuffer(depthBuffer, EDepthAccess.ReadWrite);
                
                m_GBufferMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(0, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((ref FGBufferPassData passData, ref FRDGContext graphContext) =>
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
                    graphContext.renderContext.DrawRenderers(passData.cullingResults, ref drawingSettings, ref filteringSettings);
                });
            }
        }
    }
}