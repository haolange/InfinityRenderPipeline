using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct CopyHistoryScalarPassData
        {
            public RGTextureHandle source;
            public RGTextureHandle history;
        }

        void CopyHistoryScalar(in CameraRecordContext context, int sourceId, int historyId, string name)
        {
            RGTextureHandle source = m_RGBuilder.scoper.QueryTexture(sourceId);
            TextureDescriptor descriptor = m_RGBuilder.GetTextureDescriptor(source);
            descriptor.name = name;
            RGTextureHandle history = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(historyId, descriptor));
            context.historyCache.MarkProduced(historyId);
            using (var passRef = m_RGBuilder.AddTransferPass<CopyHistoryScalarPassData>(ProfilingSampler.Get(CustomSamplerId.CopyHistorySSR)))
            {
                passRef.ReadTexture(source);
                passRef.WriteTexture(history);
                ref CopyHistoryScalarPassData passData = ref passRef.GetPassData();
                passData.source = source;
                passData.history = history;
                passRef.SetExecuteFunc((in CopyHistoryScalarPassData data, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(data.source, data.history);
                });
            }
        }

        struct CopyHistoryRadianceMetaPassData
        {
            public RGTextureHandle radianceSource;
            public RGTextureHandle radianceHistory;
            public RGTextureHandle metaSource;
            public RGTextureHandle metaHistory;
        }

        // SSR history is the temporal output (before the optional spatial pass, so filtering never feeds back) plus
        // its meta texture (normal, depth, history age).
        void CopyHistorySSR(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SSR))
            {
                return;
            }
            CopyHistoryRadianceMeta(context, CustomSamplerId.CopyHistorySSR, InfinityShaderIDs.SSRTemporalBuffer, InfinityShaderIDs.SSRMetaBuffer, InfinityShaderIDs.HistorySSRRadianceBuffer, InfinityShaderIDs.HistorySSRMetaBuffer, SSRPassUtilityData.HistoryRadianceName, SSRPassUtilityData.HistoryMetaName);
            CopyHistoryScalar(context, InfinityShaderIDs.SSRVarianceBuffer, InfinityShaderIDs.HistorySSRVarianceBuffer, "HistorySSRVariance");
        }

        // Each denoise stage keeps the combiner mix, not the temporal output.
        void CopyHistorySSGI(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SSGI))
            {
                return;
            }
            CopyHistoryRadianceMeta(context, CustomSamplerId.CopyHistorySSGI, InfinityShaderIDs.SSGIFilteredBuffer, InfinityShaderIDs.SSGIMetaBuffer, InfinityShaderIDs.HistorySSGIRadianceBuffer, InfinityShaderIDs.HistorySSGIMetaBuffer, SSGIPassUtilityData.HistoryRadianceName, SSGIPassUtilityData.HistoryMetaName);
            CopyHistoryScalar(context, InfinityShaderIDs.SSGICombinerNarrowBuffer, InfinityShaderIDs.HistorySSGINarrowBuffer, "HistorySSGINarrow");
        }

        // History of the stochastic screen-space signals: RGBA16F radiance (bilinear) and RGBA32F geometric meta (point).
        void CopyHistoryRadianceMeta(in CameraRecordContext context, CustomSamplerId samplerId, int radianceSourceId, int metaSourceId, int radianceHistoryId, int metaHistoryId, string radianceName, string metaName)
        {
            RGTextureHandle radianceSource = m_RGBuilder.scoper.QueryTexture(radianceSourceId);
            RGTextureHandle metaSource = m_RGBuilder.scoper.QueryTexture(metaSourceId);

            TextureDescriptor radianceDsc = m_RGBuilder.GetTextureDescriptor(radianceSource);
            radianceDsc.name = radianceName;
            TextureDescriptor metaDsc = m_RGBuilder.GetTextureDescriptor(metaSource);
            metaDsc.name = metaName;

            RGTextureHandle radianceHistory = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(radianceHistoryId, radianceDsc));
            RGTextureHandle metaHistory = m_RGBuilder.ImportTexture(context.historyCache.GetWriteTexture(metaHistoryId, metaDsc));
            context.historyCache.MarkProduced(radianceHistoryId);
            context.historyCache.MarkProduced(metaHistoryId);

            using (var passRef = m_RGBuilder.AddTransferPass<CopyHistoryRadianceMetaPassData>(ProfilingSampler.Get(samplerId)))
            {
                passRef.ReadTexture(radianceSource);
                passRef.WriteTexture(radianceHistory);
                passRef.ReadTexture(metaSource);
                passRef.WriteTexture(metaHistory);

                ref CopyHistoryRadianceMetaPassData passData = ref passRef.GetPassData();
                passData.radianceSource = radianceSource;
                passData.radianceHistory = radianceHistory;
                passData.metaSource = metaSource;
                passData.metaHistory = metaHistory;

                passRef.SetExecuteFunc((in CopyHistoryRadianceMetaPassData passData, in RGTransferEncoder cmdEncoder) =>
                {
                    cmdEncoder.CopyTexture(passData.radianceSource, passData.radianceHistory);
                    cmdEncoder.CopyTexture(passData.metaSource, passData.metaHistory);
                });
            }
        }
    }
}
