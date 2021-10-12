using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct FForwardPassString
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
            TextureDescription diffuseDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FForwardPassString.TextureAName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None };
            TextureDescription specularDescription = new TextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FForwardPassString.TextureBName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None };
            
            RDGTextureRef depthTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DepthBuffer);
            RDGTextureRef diffuseTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.DiffuseBuffer, diffuseDescription);     
            RDGTextureRef specularTexture = m_GraphBuilder.ScopeTexture(InfinityShaderIDs.SpecularBuffer, specularDescription);

            //Add ForwardPass
            using (RDGPassRef passRef = m_GraphBuilder.AddPass<FForwardPassData>(FForwardPassString.PassName, ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                //Setup Phase
                ref FForwardPassData passData = ref passRef.GetPassData<FForwardPassData>();
                passData.camera = camera;
                passData.cullingResults = cullingResults;
                passData.meshPassProcessor = m_ForwardMeshProcessor;
                passData.diffuseBuffer = passRef.UseColorBuffer(diffuseTexture, 0);
                passData.specularBuffer = passRef.UseColorBuffer(specularTexture, 1);
                passData.depthBuffer = passRef.UseDepthBuffer(depthTexture, EDepthAccess.Read);
                
                m_ForwardMeshProcessor.DispatchSetup(cullingData, new FMeshPassDesctiption(0, 2999));

                //Execute Phase
                passRef.SetExecuteFunc((ref FForwardPassData passData, ref RDGContext graphContext) =>
                {
                    //MeshDrawPipeline
                    passData.meshPassProcessor.DispatchDraw(graphContext, 2);

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
