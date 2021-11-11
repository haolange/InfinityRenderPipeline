using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class FForwardPassUtilityData
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
            public FRDGTextureRef depthBuffer;
            public FRDGTextureRef diffuseBuffer;
            public FRDGTextureRef specularBuffer;
            public CullingResults cullingResults;
            public FMeshPassProcessor meshPassProcessor;
        }

        void RenderForward(Camera camera, in FCullingData cullingData, in CullingResults cullingResults)
        {
            FTextureDescription diffuseDescription = new FTextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FForwardPassUtilityData.TextureAName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None };
            FTextureDescription specularDescription = new FTextureDescription(camera.pixelWidth, camera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = FForwardPassUtilityData.TextureBName, colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, depthBufferBits = EDepthBits.None };

            FRDGTextureRef depthTexture = m_GraphScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            FRDGTextureRef diffuseTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.DiffuseBuffer, diffuseDescription);
            FRDGTextureRef specularTexture = m_GraphScoper.CreateAndRegisterTexture(InfinityShaderIDs.SpecularBuffer, specularDescription);

            //Add ForwardPass
            using (FRDGPassRef passRef = m_GraphBuilder.AddPass<FForwardPassData>(FForwardPassUtilityData.PassName, ProfilingSampler.Get(CustomSamplerId.RenderForward)))
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
                passRef.SetExecuteFunc((in FForwardPassData passData, in FRDGContext graphContext) =>
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
                        perObjectData = PerObjectData.Lightmaps | PerObjectData.LightProbe,
                        enableInstancing = true,
                        enableDynamicBatching = false
                    };
                    graphContext.renderContext.DrawRenderers(passData.cullingResults, ref drawingSettings, ref filteringSettings);
                });
            }
        }
    }
}
