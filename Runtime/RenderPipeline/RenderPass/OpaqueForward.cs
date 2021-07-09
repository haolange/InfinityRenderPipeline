using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FOpaqueForwardString
    {
        internal static string PassName = "OpaqueForward";
        internal static string TextureAName = "DiffuseTexture";
        internal static string TextureBName = "SpecularTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct FOpaqueForwardData
        {
            public Camera camera;
            public RDGTextureRef depthBuffer;
            public RDGTextureRef diffuseBuffer;
            public RDGTextureRef specularBuffer;
            public CullingResults cullingResults;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderOpaqueForward(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            TextureDescription diffuseDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueForwardString.TextureAName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef diffuseTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, diffuseDescription);
            TextureDescription specularDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FOpaqueForwardString.TextureBName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32 };
            RDGTextureRef specularTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, specularDescription);

            //Add OpaqueForwardPass
            using (RDGPassBuilder passBuilder = m_GraphBuilder.AddPass<FOpaqueForwardData>(FOpaqueForwardString.PassName, ProfilingSampler.Get(CustomSamplerId.OpaqueForward)))
            {
                //Setup Phase
                ref FOpaqueForwardData passData = ref passBuilder.GetPassData<FOpaqueForwardData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_ForwardMeshProcessor;
                passData.diffuseBuffer = passBuilder.UseColorBuffer(diffuseTexture, 0);
                passData.specularBuffer = passBuilder.UseColorBuffer(specularTexture, 1);
                passData.depthBuffer = passBuilder.UseDepthBuffer(depthTexture, EDepthAccess.Read);
                
                m_ForwardMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(0, 2999));

                //Execute Phase
                passBuilder.SetRenderFunc((ref FOpaqueForwardData passData, ref RDGGraphContext graphContext) =>
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
                    DrawingSettings drawingSettings = new DrawingSettings(InfinityPassIDs.ForwardPlus, new SortingSettings(passData.camera) { criteria = SortingCriteria.OptimizeStateChanges })
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
