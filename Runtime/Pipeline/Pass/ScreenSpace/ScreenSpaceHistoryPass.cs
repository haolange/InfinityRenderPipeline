using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal sealed class ScreenSpaceHistoryPass
    {
        readonly RGBuilder m_RGBuilder;

        internal ScreenSpaceHistoryPass(RGBuilder graph)
        {
            m_RGBuilder = graph;
        }

        struct CopyHistoryScalarPassData
        {
            public RGTextureHandle sourceHandle;
            public RGTextureHandle historyHandle;
        }

        void CopyHistoryScalar(in ViewContext context, CustomSamplerId samplerId, int sourceId, int historyId, string name)
        {
            RGTextureHandle sourceHandle = m_RGBuilder.scoper.QueryTexture(sourceId);
            TextureDescriptor descriptor = m_RGBuilder.GetTextureDescriptor(sourceHandle);
            descriptor.name = name;
            RGTextureHandle historyHandle = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(historyId, descriptor));
            context.historyCache.MarkProduced(historyId);
            {
                RGTransferPassHandle<CopyHistoryScalarPassData> passHandle = m_RGBuilder.AddTransferPass<CopyHistoryScalarPassData>(ProfilingSampler.Get(samplerId));
                passHandle.ReadTexture(sourceHandle);
                passHandle.WriteTexture(historyHandle);
                ref CopyHistoryScalarPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = sourceHandle;
                passData.historyHandle = historyHandle;
                passHandle.SetExecuteFunc((in CopyHistoryScalarPassData data, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(data.sourceHandle, data.historyHandle);
                });
            }
        }

        struct CopyHistoryRadianceMetaPassData
        {
            public RGTextureHandle radianceSourceHandle;
            public RGTextureHandle radianceHistoryHandle;
            public RGTextureHandle metaSourceHandle;
            public RGTextureHandle metaHistoryHandle;
        }

        // SSR history is the temporal output (before the optional spatial pass, so filtering never feeds back) plus
        // its metadata texture (normal, history age, validity).
        internal void CopyHistorySSR(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SSR))
            {
                return;
            }
            CopyHistoryRadianceMeta(context, CustomSamplerId.CopyHistorySSR, InfinityShaderIDs.SSRTemporalBuffer, InfinityShaderIDs.SSRMetaBuffer, InfinityShaderIDs.HistorySSRRadianceBuffer, InfinityShaderIDs.HistorySSRMetaBuffer, SSRPassUtilityData.HistoryRadianceName, SSRPassUtilityData.HistoryMetaName);
            CopyHistoryScalar(context, CustomSamplerId.CopyHistorySSR, InfinityShaderIDs.SSRVarianceBuffer, InfinityShaderIDs.HistorySSRVarianceBuffer, "HistorySSRVariance");
        }

        // Each denoise stage keeps the combiner mix, not the temporal output.
        internal void CopyHistorySSGI(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SSGI))
            {
                return;
            }
            CopyHistoryRadianceMeta(context, CustomSamplerId.CopyHistorySSGI, InfinityShaderIDs.SSGIFilteredBuffer, InfinityShaderIDs.SSGIMetaBuffer, InfinityShaderIDs.HistorySSGIRadianceBuffer, InfinityShaderIDs.HistorySSGIMetaBuffer, SSGIPassUtilityData.HistoryRadianceName, SSGIPassUtilityData.HistoryMetaName);
            CopyHistoryScalar(context, CustomSamplerId.CopyHistorySSGI, InfinityShaderIDs.SSGICombinerNarrowBuffer, InfinityShaderIDs.HistorySSGINarrowBuffer, "HistorySSGINarrow");
        }

        // History of the stochastic screen-space signals: RGBA16F radiance (bilinear) and R32_UInt surface metadata (point).
        void CopyHistoryRadianceMeta(in ViewContext context, CustomSamplerId samplerId, int radianceSourceId, int metaSourceId, int radianceHistoryId, int metaHistoryId, string radianceName, string metaName)
        {
            RGTextureHandle radianceSourceHandle = m_RGBuilder.scoper.QueryTexture(radianceSourceId);
            RGTextureHandle metaSourceHandle = m_RGBuilder.scoper.QueryTexture(metaSourceId);

            TextureDescriptor radianceDsc = m_RGBuilder.GetTextureDescriptor(radianceSourceHandle);
            radianceDsc.name = radianceName;
            TextureDescriptor metaDsc = m_RGBuilder.GetTextureDescriptor(metaSourceHandle);
            metaDsc.name = metaName;

            RGTextureHandle radianceHistoryHandle = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(radianceHistoryId, radianceDsc));
            RGTextureHandle metaHistoryHandle = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(metaHistoryId, metaDsc));
            context.historyCache.MarkProduced(radianceHistoryId);
            context.historyCache.MarkProduced(metaHistoryId);

            {
                RGTransferPassHandle<CopyHistoryRadianceMetaPassData> passHandle = m_RGBuilder.AddTransferPass<CopyHistoryRadianceMetaPassData>(ProfilingSampler.Get(samplerId));
                passHandle.ReadTexture(radianceSourceHandle);
                passHandle.WriteTexture(radianceHistoryHandle);
                passHandle.ReadTexture(metaSourceHandle);
                passHandle.WriteTexture(metaHistoryHandle);

                ref CopyHistoryRadianceMetaPassData passData = ref passHandle.GetPassData();
                passData.radianceSourceHandle = radianceSourceHandle;
                passData.radianceHistoryHandle = radianceHistoryHandle;
                passData.metaSourceHandle = metaSourceHandle;
                passData.metaHistoryHandle = metaHistoryHandle;

                passHandle.SetExecuteFunc((in CopyHistoryRadianceMetaPassData passData, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(passData.radianceSourceHandle, passData.radianceHistoryHandle);
                    cmdEncoder.CopyTexture(passData.metaSourceHandle, passData.metaHistoryHandle);
                });
            }
        }
    }
}
