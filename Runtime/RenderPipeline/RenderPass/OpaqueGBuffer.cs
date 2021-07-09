using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FOpaqueGBufferString
    {
        internal static string PassName = "OpaqueGBuffer";
        internal static string TextureAName = "GBufferATexture";
        internal static string TextureBName = "GBufferBTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FOpaqueGBufferData
        {
            public Camera camera;
            public CullingResults cullingResults;
            public RDGTextureRef gbufferTextureA;
            public RDGTextureRef gbufferTextureB;
            public RDGTextureRef depthBufferTexture;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderOpaqueGBuffer(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription GBufferDescriptionA = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueGBufferString.TextureAName, colorFormat = GraphicsFormat.R8G8B8A8_UNorm };
            RDGTextureRef gbufferTexureA = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferA, GBufferDescriptionA);
            TextureDescription GBufferDescriptionB = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueGBufferString.TextureBName, colorFormat = GraphicsFormat.A2B10G10R10_UIntPack32 };
            RDGTextureRef gbufferTexureB = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.GBufferB, GBufferDescriptionB);

            //Add OpaqueGBufferPass
            using (RDGPassBuilder passBuilder = m_GraphBuilder.AddPass<FOpaqueGBufferData>(FOpaqueGBufferString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueGBuffer)))
            {
                //Setup Phase
                ref FOpaqueGBufferData passData = ref passBuilder.GetPassData<FOpaqueGBufferData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_GBufferMeshProcessor;
                passData.gbufferTextureA = passBuilder.UseColorBuffer(gbufferTexureA, 0);
                passData.gbufferTextureB = passBuilder.UseColorBuffer(gbufferTexureB, 1);
                passData.depthBufferTexture = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                
                m_GBufferMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(0, 2999));

                //Execute Phase
                passBuilder.SetRenderFunc((ref FOpaqueGBufferData passData, ref RDGGraphContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(ref graphContext, 1);

                    //UnityDrawPipeline
                    FilteringSettings filteringSettings = new FilteringSettings
                    {
                        renderingLayerMask = 1,
                        layerMask = passData.camera.cullingMask,
                        renderQueueRange = new RenderQueueRange(0, 2999),
                    };
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.OpaqueGBuffer, new SortingSettings(passData.camera) { criteria = SortingCriteria.QuantizedFrontToBack })
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