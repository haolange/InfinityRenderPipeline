using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct FGBufferPassString
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
            public CullingResults cullingResults;
            public RDGTextureRef gbufferA;
            public RDGTextureRef gbufferB;
            public RDGTextureRef gbufferC;
            public RDGTextureRef depthBuffer;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderGBuffer(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            RDGTextureRef depthBuffer = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription gbufferADsc = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FGBufferPassString.TextureAName, colorFormat = SystemInfo.IsFormatSupported(GraphicsFormat.B5G6R5_UNormPack16, FormatUsage.Render) ? GraphicsFormat.B5G6R5_UNormPack16 : GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = EDepthBits.None };
            RDGTextureRef gbufferA = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferA, gbufferADsc);
            TextureDescription gbufferBDsc = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FGBufferPassString.TextureBName, colorFormat = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = EDepthBits.None };
            RDGTextureRef gbufferB = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);
            TextureDescription gbufferCDsc = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FGBufferPassString.TextureCName, colorFormat = GraphicsFormat.R8G8_UNorm, depthBufferBits = EDepthBits.None };
            RDGTextureRef gbufferC = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferC, gbufferCDsc);

            //Add GBufferPass
            using (RDGPassRef passBuilder = m_GraphBuilder.AddPass<FGBufferPassData>(FGBufferPassString.PassName, ProfilingSampler.Get(CustomSamplerId.RenderGBuffer)))
            {
                //Setup Phase
                ref FGBufferPassData passData = ref passBuilder.GetPassData<FGBufferPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_GBufferMeshProcessor;
                passData.gbufferA = passBuilder.UseColorBuffer(gbufferA, 0);
                passData.gbufferB = passBuilder.UseColorBuffer(gbufferB, 1);
                passData.gbufferC = passBuilder.UseColorBuffer(gbufferC, 2);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthBuffer, EDepthAccess.ReadWrite);
                
                m_GBufferMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(0, 2999));

                //Execute Phase
                passBuilder.SetExecuteFunc((ref FGBufferPassData passData, ref RDGContext graphContext) =>
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