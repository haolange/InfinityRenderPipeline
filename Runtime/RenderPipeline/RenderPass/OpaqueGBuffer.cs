using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Runtime.Rendering.RDG;
using InfinityTech.Runtime.Rendering.Core;
using InfinityTech.Runtime.Rendering.MeshDrawPipeline;

namespace InfinityTech.Runtime.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct FOpaqueGBufferData
        {
            public RendererList RendererList;
            public RDGTextureRef GBufferA;
            public RDGTextureRef GBufferB;
            public RDGTextureRef DepthBuffer;
        }

        void RenderOpaqueGBuffer(Camera RenderCamera, CullingResults CullingResult, NativeArray<FMeshBatch> MeshBatchArray, FCullingData CullingData)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(CullingResult, RenderCamera, InfinityPassIDs.OpaqueGBuffer));
            RDGTextureRef DepthTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_DepthBuffer);

            RDGTextureDesc GBufferDescA = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferATexture", colorFormat = GraphicsFormat.R16G16B16A16_UNorm };
            RDGTextureDesc GBufferDescB = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferBTexture", colorFormat = GraphicsFormat.A2B10G10R10_UIntPack32 };
            RDGTextureRef GBufferTextureA = GraphBuilder.CreateTexture(GBufferDescA, InfinityShaderIDs.RT_ThinGBufferA);
            RDGTextureRef GBufferTextureB = GraphBuilder.CreateTexture(GBufferDescB, InfinityShaderIDs.RT_ThinGBufferB);

            GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_ThinGBufferA, GBufferTextureA);
            GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_ThinGBufferB, GBufferTextureB);

            //Add RenderPass
            GraphBuilder.AddRenderPass<FOpaqueGBufferData>("OpaqueGBuffer", ProfilingSampler.Get(CustomSamplerId.OpaqueGBuffer),
            (ref FOpaqueGBufferData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.GBufferA = PassBuilder.UseColorBuffer(GBufferTextureA, 0);
                PassData.GBufferB = PassBuilder.UseColorBuffer(GBufferTextureB, 1);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.ReadWrite);
            },
            (ref FOpaqueGBufferData PassData, RDGContext GraphContext) =>
            {
                //Draw UnityRenderer
                RendererList GBufferRenderList = PassData.RendererList;
                GBufferRenderList.drawSettings.perObjectData = PerObjectData.Lightmaps;
                GBufferRenderList.drawSettings.enableInstancing = RenderPipelineAsset.EnableInstanceBatch;
                GBufferRenderList.drawSettings.enableDynamicBatching = RenderPipelineAsset.EnableDynamicBatch;
                GBufferRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(0, 2450);
                GraphContext.RenderContext.DrawRenderers(GBufferRenderList.cullingResult, ref GBufferRenderList.drawSettings, ref GBufferRenderList.filteringSettings);

                //Draw MeshBatch
                if (CullingData.ViewMeshBatchList.Length == 0) { return; }

                for (int i = 0; i < CullingData.ViewMeshBatchList.Length; i++)
                {
                    FViewMeshBatch ViewMeshBatch = CullingData.ViewMeshBatchList[i];
                    FMeshBatch MeshBatch = MeshBatchArray[ViewMeshBatch.index];
                    Mesh DrawMesh = GraphContext.World.WorldMeshList.Get(MeshBatch.Mesh);
                    Material DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshBatch.Material);

                    if (DrawMesh && DrawMaterial)
                    {
                        GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatch.Matrix_LocalToWorld, DrawMaterial, MeshBatch.SubmeshIndex, 2);
                    }
                }
            });
        }
    }
}


                    /*var RTV_ThinGBuffer_ID = GraphContext.renderGraphPool.GetTempArray<RenderTargetIdentifier>(2);
                    RTV_ThinGBuffer_ID[0] = GraphContext.resources.GetTexture(PassData.ThinGBufferA);
                    RTV_ThinGBuffer_ID[1] = GraphContext.resources.GetTexture(PassData.ThinGBufferB);
                    CoreUtils.SetRenderTarget(GraphContext.cmd, RTV_ThinGBuffer_ID, GraphContext.resources.GetTexture(PassData.DepthBuffer));*/