using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.MeshDrawPipeline;

namespace InfinityTech.Rendering.Pipeline
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

        void RenderOpaqueGBuffer(Camera RenderCamera, CullingResults CullingResult, NativeArray<FMeshBatch> MeshBatchs, FCullingData CullingData)
        {
            //Request Resource
            RendererList RenderList = RendererList.Create(CreateRendererListDesc(CullingResult, RenderCamera, InfinityPassIDs.OpaqueGBuffer, new RenderQueueRange(0, 2450), PerObjectData.Lightmaps));

            RDGTextureRef DepthTexture = GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_DepthBuffer);

            RDGTextureDesc GBufferADesc = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferATexture", colorFormat = GraphicsFormat.R16G16B16A16_UNorm };
            RDGTextureRef GBufferATexure = GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_ThinGBufferA, GBufferADesc);

            RDGTextureDesc GBufferBDesc = new RDGTextureDesc(RenderCamera.pixelWidth, RenderCamera.pixelHeight) { clearBuffer = true, clearColor = Color.clear, dimension = TextureDimension.Tex2D, enableMSAA = false, bindTextureMS = false, name = "GBufferBTexture", colorFormat = GraphicsFormat.A2B10G10R10_UIntPack32 };
            RDGTextureRef GBufferBTexure = GraphBuilder.ScopeTexture(InfinityShaderIDs.RT_ThinGBufferB, GBufferBDesc);

            //Add OpaqueGBufferPass
            GraphBuilder.AddPass<FOpaqueGBufferData>("OpaqueGBuffer", ProfilingSampler.Get(CustomSamplerId.OpaqueGBuffer),
            (ref FOpaqueGBufferData PassData, ref RDGPassBuilder PassBuilder) =>
            {
                PassData.RendererList = RenderList;
                PassData.GBufferA = PassBuilder.UseColorBuffer(GBufferATexure, 0);
                PassData.GBufferB = PassBuilder.UseColorBuffer(GBufferBTexure, 1);
                PassData.DepthBuffer = PassBuilder.UseDepthBuffer(DepthTexture, EDepthAccess.ReadWrite);
            },
            (ref FOpaqueGBufferData PassData, RDGContext GraphContext) =>
            {
                //UnityRenderer
                RendererList GBufferRenderList = PassData.RendererList;
                GBufferRenderList.drawSettings.perObjectData = PerObjectData.Lightmaps;
                GBufferRenderList.drawSettings.enableInstancing = RenderPipelineAsset.EnableInstanceBatch;
                GBufferRenderList.drawSettings.enableDynamicBatching = RenderPipelineAsset.EnableDynamicBatch;
                GBufferRenderList.filteringSettings.renderQueueRange = new RenderQueueRange(0, 2999);
                GraphContext.RenderContext.DrawRenderers(GBufferRenderList.cullingResult, ref GBufferRenderList.drawSettings, ref GBufferRenderList.filteringSettings);

                //MeshDrawPipeline
                FMeshBatchProcessor GBufferMeshProcessor = GraphContext.ObjectPool.Get<FMeshBatchProcessor>();
                FMeshPassDesctiption GBufferMeshPassDesc = new FMeshPassDesctiption() { RenderQueueMin = 0, RenderQueueMax = 2450, RenderLayerMask = 0, ExcludeMotionVectorObjects = true};
                GBufferMeshProcessor.DispatchMesh(GraphContext, MeshBatchs, CullingData, GBufferMeshPassDesc);
            });
        }
    }
}


/*var RTV_ThinGBuffer_ID = GraphContext.renderGraphPool.GetTempArray<RenderTargetIdentifier>(2);
RTV_ThinGBuffer_ID[0] = GraphContext.resources.GetTexture(PassData.ThinGBufferA);
RTV_ThinGBuffer_ID[1] = GraphContext.resources.GetTexture(PassData.ThinGBufferB);
CoreUtils.SetRenderTarget(GraphContext.cmd, RTV_ThinGBuffer_ID, GraphContext.resources.GetTexture(PassData.DepthBuffer));*/

/*for (int i = 0; i < CullingData.ViewMeshBatchs.Length; i++)
{
    Mesh DrawMesh;
    Material DrawMaterial;
    FMeshBatch MeshBatch;

    switch (CullingData.CullMethod)
    {
        case ECullingMethod.VisibleMark:
            if (CullingData.ViewMeshBatchs[i] != 0)
            {
                MeshBatch = MeshBatchs[i];
                DrawMesh = GraphContext.World.WorldMeshList.Get(MeshBatch.Mesh);
                DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshBatch.Material);

                if (DrawMesh && DrawMaterial)
                {
                    GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatch.Matrix_LocalToWorld, DrawMaterial, MeshBatch.SubmeshIndex, 2);
                }
            }
            break;

        case ECullingMethod.FillterList:
            MeshBatch = MeshBatchs[CullingData.ViewMeshBatchs[i]];
            DrawMesh = GraphContext.World.WorldMeshList.Get(MeshBatch.Mesh);
            DrawMaterial = GraphContext.World.WorldMaterialList.Get(MeshBatch.Material);

            if (DrawMesh && DrawMaterial)
            {
                GraphContext.CmdBuffer.DrawMesh(DrawMesh, MeshBatch.Matrix_LocalToWorld, DrawMaterial, MeshBatch.SubmeshIndex, 2);
            }
            break;
    }
}*/