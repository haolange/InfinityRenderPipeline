using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Decal;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct ClusteredDecalInputs
    {
        public bool active;
        public RGBufferHandle recordsHandle;
        public RGBufferHandle clusterHeadersHandle;
        public RGBufferHandle clusterIndicesHandle;

        public static ClusteredDecalInputs Query(RGBuilder graph, in ViewContext context)
        {
            bool active = context.decals.ClusteredDecalCount > 0;
            if (!active)
            {
                return default;
            }
            return new ClusteredDecalInputs
            {
                active = true,
                recordsHandle = graph.scoper.QueryBuffer(DecalShaderIDs.DecalRecordBuffer),
                clusterHeadersHandle = graph.scoper.QueryBuffer(DecalShaderIDs.DecalClusterHeaderBuffer),
                clusterIndicesHandle = graph.scoper.QueryBuffer(DecalShaderIDs.DecalClusterIndexBuffer)
            };
        }

        public void Read<THandle>(THandle passHandle) where THandle : struct, IPassHandle
        {
            if (!active)
            {
                return;
            }
            passHandle.ReadBuffer(recordsHandle);
            passHandle.ReadBuffer(clusterHeadersHandle);
            passHandle.ReadBuffer(clusterIndicesHandle);
        }

        public void BindGlobal<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
        {
            if (!active)
            {
                return;
            }
            commands.SetGlobalBuffer(DecalShaderIDs.DecalRecords, recordsHandle);
            commands.SetGlobalBuffer(DecalShaderIDs.DecalClusterHeaders, clusterHeadersHandle);
            commands.SetGlobalBuffer(DecalShaderIDs.DecalClusterIndices, clusterIndicesHandle);
        }
    }

    internal static class DBufferPassUtilityData
    {
        internal static readonly string DBufferAName = "DBufferTextureA";
        internal static readonly string DBufferBName = "DBufferTextureB";
        internal static readonly string DBufferCName = "DBufferTextureC";
    }

    internal sealed class DBufferPass
    {
        static readonly ProfilingSampler s_UploadDecals = new ProfilingSampler("UploadDecalData");

        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;

        internal DBufferPass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings)
        {
            m_RGBuilder = graph;
            m_Settings = settings;
        }

        struct DecalUploadPassData
        {
            public DecalContext context;
        }

        struct DBufferPassData
        {
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle depthTextureHandle;
        }

        struct DirectDecalPassData
        {
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle depthTextureHandle;
        }

        internal void UploadDecalData(DecalContext context)
        {
            if (context.ClusteredDecalCount == 0)
            {
                return;
            }
            RGTransferPassHandle<DecalUploadPassData> passHandle = m_RGBuilder.AddTransferPass<DecalUploadPassData>(s_UploadDecals);
            passHandle.GetPassData().context = context;
            m_RGBuilder.scoper.RegisterBuffer(DecalShaderIDs.DecalRecordBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.RecordBuffer, "DecalRecordBuffer")));
            m_RGBuilder.scoper.RegisterBuffer(DecalShaderIDs.DecalClusterHeaderBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.ClusterHeaderBuffer, "DecalClusterHeaderBuffer")));
            m_RGBuilder.scoper.RegisterBuffer(DecalShaderIDs.DecalClusterIndexBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.ClusterIndexBuffer, "DecalClusterIndexBuffer")));
            passHandle.SetExecuteFunc((in DecalUploadPassData data, in RGTransferEncoder commands) => data.context.Upload(commands));
        }

        internal void RasterDBuffer(in ViewContext context)
        {
            if (context.decals.DBufferDecalCount == 0 || !context.features.ShouldRecord(EFrameFeature.DBuffer))
            {
                return;
            }

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;

            TextureDescriptor dBufferADsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferADsc.name = DBufferPassUtilityData.DBufferAName;
                dBufferADsc.dimension = TextureDimension.Tex2D;
                dBufferADsc.colorFormat = GraphicsFormat.R8G8B8A8_SRGB;
                dBufferADsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferAHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferA, dBufferADsc);

            TextureDescriptor dBufferBDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferBDsc.name = DBufferPassUtilityData.DBufferBName;
                dBufferBDsc.dimension = TextureDimension.Tex2D;
                dBufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferBDsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferBHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferB, dBufferBDsc);

            TextureDescriptor dBufferCDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                dBufferCDsc.name = DBufferPassUtilityData.DBufferCName;
                dBufferCDsc.dimension = TextureDimension.Tex2D;
                dBufferCDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                dBufferCDsc.clearColor = Color.clear;
            }
            RGTextureHandle dBufferCHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DBufferC, dBufferCDsc);

            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            RGRendererListHandle dBufferRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterDBufferPass,
                new RenderQueueRange(2000, 2449), SortingCriteria.RendererPriority | SortingCriteria.CommonOpaque, PerObjectData.None, m_Settings.enableInstanceBatch));

            {
                RGRasterPassHandle<DBufferPassData> passHandle = m_RGBuilder.AddRasterPass<DBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDBuffer));
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(dBufferAHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(dBufferBHandle, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(dBufferCHandle, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref DBufferPassData passData = ref passHandle.GetPassData();
                {
                    passData.rendererListHandle = passHandle.UseRendererList(dBufferRendererListHandle);
                    passData.depthTextureHandle = depthTextureHandle;
                }

                passHandle.SetExecuteFunc((in DBufferPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DepthBuffer, passData.depthTextureHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }

            context.features.MarkProduced(EFrameFeature.DBuffer);
        }

        // Direct decals sample stored GBuffer depth. The MRT binding still requires a read-only depth attachment.
        internal void RasterDirectDecals(in ViewContext context)
        {
            if (context.decals.DirectDecalCount == 0 || !context.features.ShouldRecord(EFrameFeature.DBuffer))
            {
                return;
            }

            RGTextureHandle gBufferAHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureHandle gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            RGRendererListHandle directRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterDirectDecalPass,
                new RenderQueueRange(2000, 2449), SortingCriteria.RendererPriority | SortingCriteria.CommonOpaque, PerObjectData.None, m_Settings.enableInstanceBatch));

            {
                RGRasterPassHandle<DirectDecalPassData> passHandle = m_RGBuilder.AddRasterPass<DirectDecalPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDirectDecals));
                passHandle.EnablePassCulling(false);
                passHandle.EnableNativeRenderPass(false);
                passHandle.SetColorAttachment(gBufferAHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(gBufferBHandle, 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref DirectDecalPassData passData = ref passHandle.GetPassData();
                passData.rendererListHandle = passHandle.UseRendererList(directRendererListHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);

                passHandle.SetExecuteFunc((in DirectDecalPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DepthBuffer, passData.depthTextureHandle, RenderTextureSubElement.Depth);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }

            if (context.decals.DirectNormalDecalCount == 0)
            {
                return;
            }

            RGRendererListHandle normalRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterDirectDecalNormalPass,
                new RenderQueueRange(2000, 2449), SortingCriteria.RendererPriority | SortingCriteria.CommonOpaque, PerObjectData.None, m_Settings.enableInstanceBatch));
            {
                RGRasterPassHandle<DirectDecalPassData> passHandle = m_RGBuilder.AddRasterPass<DirectDecalPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDirectDecalNormals));
                passHandle.EnablePassCulling(false);
                passHandle.EnableNativeRenderPass(false);
                passHandle.SetColorAttachment(gBufferAHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(gBufferBHandle, 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);
                ref DirectDecalPassData passData = ref passHandle.GetPassData();
                passData.rendererListHandle = passHandle.UseRendererList(normalRendererListHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                passHandle.SetExecuteFunc((in DirectDecalPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DepthBuffer, passData.depthTextureHandle, RenderTextureSubElement.Depth);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }
        }
    }
}
