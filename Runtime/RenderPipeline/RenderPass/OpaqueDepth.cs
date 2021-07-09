using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FOpaqueDepthString
    {
        internal static string PassName = "OpaqueDepth";
        internal static string TextureName = "DepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FOpaqueDepthData
        {
            public Camera camera;
            public CullingResults cullingResults;
            public RDGTextureRef depthBufferTexture;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderOpaqueDepth(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            TextureDescription depthDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueDepthString.TextureName, depthBufferBits = EDepthBits.Depth32 };
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer, depthDescription);

            //Add OpaqueDepthPass
            using (RDGPassBuilder passBuilder = m_GraphBuilder.AddPass<FOpaqueDepthData>(FOpaqueDepthString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueDepth)))
            {
                //Setup Phase
                ref FOpaqueDepthData passData = ref passBuilder.GetPassData<FOpaqueDepthData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_DepthMeshProcessor;
                passData.depthBufferTexture = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.ReadWrite);
                
                m_DepthMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(2450, 2999));

                //Execute Phase
                passBuilder.SetRenderFunc((ref FOpaqueDepthData passData, ref RDGGraphContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(ref graphContext, 0);

                    //UnityDrawPipeline
                    FilteringSettings filteringSettings = new FilteringSettings
                    {
                        renderingLayerMask = 1,
                        layerMask = passData.camera.cullingMask,
                        renderQueueRange = new RenderQueueRange(2450, 2999),
                    };
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.OpaqueDepth, new SortingSettings(passData.camera) { criteria = SortingCriteria.QuantizedFrontToBack })
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