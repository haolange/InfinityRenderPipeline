using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ScreenSpaceHistoryUtility
    {
        internal const int TemporalResetRampFrames = 8;

        internal static float RampTemporalWeight(float configuredWeight, ref int validFrames, bool resetHistory)
        {
            if (resetHistory)
            {
                validFrames = 0;
            }

            float ramp = TemporalResetRampFrames <= 1
                ? 1.0f
                : Mathf.Clamp01(validFrames / (float)TemporalResetRampFrames);
            float weight = configuredWeight * ramp;
            if (validFrames < TemporalResetRampFrames)
            {
                ++validFrames;
            }

            return weight;
        }

        internal static TextureDescriptor CreateFilterDescriptor(int width, int height, string name, GraphicsFormat format, bool randomWrite)
        {
            TextureDescriptor descriptor = new TextureDescriptor(width, height);
            descriptor.name = name;
            descriptor.dimension = TextureDimension.Tex2D;
            descriptor.colorFormat = format;
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.enableRandomWrite = randomWrite;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            return descriptor;
        }

        internal static TextureDescriptor CreateHistoryDescriptor(int width, int height, string name, GraphicsFormat format)
        {
            return CreateFilterDescriptor(width, height, name, format, false);
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct CopyHistoryRadianceMetaPassData
        {
            public RGTextureRef radianceSource;
            public RGTextureRef radianceHistory;
            public RGTextureRef metaSource;
            public RGTextureRef metaHistory;
        }

        // SSR history is the temporal output (before the optional spatial pass, so filtering never feeds back) plus
        // its meta texture (normal, depth, history age).
        void CopyHistorySSR(RenderContext renderContext, HistoryCache historyCache, Camera camera)
        {
            CopyHistoryRadianceMeta(historyCache, CustomSamplerId.CopyHistorySSR,
                InfinityShaderIDs.SSRTemporalBuffer, InfinityShaderIDs.SSRMetaBuffer,
                InfinityShaderIDs.HistorySSRRadianceBuffer, InfinityShaderIDs.HistorySSRMetaBuffer,
                SSRPassUtilityData.HistoryRadianceName, SSRPassUtilityData.HistoryMetaName);
        }

        // SSGI keeps the same history shape as SSR.
        void CopyHistorySSGI(RenderContext renderContext, HistoryCache historyCache, Camera camera)
        {
            CopyHistoryRadianceMeta(historyCache, CustomSamplerId.CopyHistorySSGI,
                InfinityShaderIDs.SSGITemporalBuffer, InfinityShaderIDs.SSGIMetaBuffer,
                InfinityShaderIDs.HistorySSGIRadianceBuffer, InfinityShaderIDs.HistorySSGIMetaBuffer,
                SSGIPassUtilityData.HistoryRadianceName, SSGIPassUtilityData.HistoryMetaName);
        }

        // History of the stochastic screen-space signals: RGBA16F radiance (bilinear) and RGBA16F meta (point).
        void CopyHistoryRadianceMeta(HistoryCache historyCache, CustomSamplerId samplerId, int radianceSourceId, int metaSourceId,
            int radianceHistoryId, int metaHistoryId, string radianceName, string metaName)
        {
            if (!m_RGScoper.TryQueryTexture(radianceSourceId, out RGTextureRef radianceSource) ||
                !m_RGScoper.TryQueryTexture(metaSourceId, out RGTextureRef metaSource))
            {
                return;
            }

            int width = m_ActiveFrameState.dimensions.internalSize.x;
            int height = m_ActiveFrameState.dimensions.internalSize.y;
            TextureDescriptor radianceDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, radianceName, GraphicsFormat.R16G16B16A16_SFloat);
            TextureDescriptor metaDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, metaName, GraphicsFormat.R16G16B16A16_SFloat);
            metaDsc.filterMode = FilterMode.Point;

            RGTextureRef radianceHistory = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(radianceHistoryId, radianceDsc));
            RGTextureRef metaHistory = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(metaHistoryId, metaDsc));
            historyCache.MarkProduced(radianceHistoryId);
            historyCache.MarkProduced(metaHistoryId);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryRadianceMetaPassData>(ProfilingSampler.Get(samplerId)))
            {
                passRef.ReadTexture(radianceSource);
                passRef.WriteTexture(radianceHistory);
                passRef.ReadTexture(metaSource);
                passRef.WriteTexture(metaHistory);

                ref CopyHistoryRadianceMetaPassData passData = ref passRef.GetPassData<CopyHistoryRadianceMetaPassData>();
                passData.radianceSource = radianceSource;
                passData.radianceHistory = radianceHistory;
                passData.metaSource = metaSource;
                passData.metaHistory = metaHistory;

                passRef.SetExecuteFunc((in CopyHistoryRadianceMetaPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.radianceSource, passData.radianceHistory);
                    cmdEncoder.CopyTexture(passData.metaSource, passData.metaHistory);
                });
            }
        }

        void ResolveOpaqueSceneColor()
        {
            if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer, out _))
            {
                return;
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.LightingBuffer, out _))
            {
                throw new System.InvalidOperationException("InfinityRP: OpaqueSceneColor has no DeferredBase LightingBuffer to adopt.");
            }

            m_RGScoper.MoveTexture(InfinityShaderIDs.LightingBuffer, InfinityShaderIDs.OpaqueSceneColorBuffer);
        }
    }
}
