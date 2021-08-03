using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FForwardPassString
    {
        internal static string PassName = "ForwardPass";
        internal static string TextureAName = "DiffuseTexture";
        internal static string TextureBName = "SpecularTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FForwardPassData
        {
            public Camera camera;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef diffuseBuffer;
            public RDGTextureRef specularBuffer;
            public CullingResults cullingResults;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderForward(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription diffuseDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FForwardPassString.TextureAName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef diffuseTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, diffuseDescription);
            TextureDescription specularDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FForwardPassString.TextureBName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef specularTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, specularDescription);

            //Add ForwardPass
            using (RDGPassBuilder passBuilder = m_GraphBuilder.AddPass<FForwardPassData>(FForwardPassString.PassName, ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                //Setup Phase
                ref FForwardPassData passData = ref passBuilder.GetPassData<FForwardPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_ForwardMeshProcessor;
                passData.diffuseBuffer = passBuilder.UseColorBuffer(diffuseTexture, 0);
                passData.specularBuffer = passBuilder.UseColorBuffer(specularTexture, 1);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.Read);
                
                m_ForwardMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(0, 2999));

                //Execute Phase
                passBuilder.SetExecuteFunc((ref FForwardPassData passData, ref RDGGraphContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(ref graphContext, 2);

                    //UnityDrawPipeline
                    FilteringSettings filteringSettings = new FilteringSettings
                    {
                        renderingLayerMask = 1,
                        layerMask = passData.camera.cullingMask,
                        renderQueueRange = new RenderQueueRange(0, 2999),
                    };
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.ForwardPass, new SortingSettings(passData.camera) { criteria = SortingCriteria.OptimizeStateChanges })
                    {
                        perObjectData = PerObjectData.Lightmaps,
                        enableInstancing = true,
                        enableDynamicBatching = false
                    };
                    graphContext.renderContext.DrawRenderers(passData.cullingResults, ref drawingSettings, ref filteringSettings);
                });
            }
        }
    }
}
