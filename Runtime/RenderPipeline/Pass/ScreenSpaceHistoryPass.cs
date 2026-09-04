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

        internal static TextureDescriptor CreateRadianceDescriptor(int width, int height, string name)
        {
            return CreateFilterDescriptor(width, height, name, GraphicsFormat.R16G16B16A16_SFloat, true);
        }

        internal static TextureDescriptor CreateMomentsDescriptor(int width, int height, string name)
        {
            return CreateFilterDescriptor(width, height, name, GraphicsFormat.R16G16B16A16_SFloat, true);
        }

        internal static TextureDescriptor CreateDepthNormalDescriptor(int width, int height, string name)
        {
            return CreateFilterDescriptor(width, height, name, GraphicsFormat.R16G16B16A16_SFloat, true);
        }

        internal static TextureDescriptor CreateHistoryDescriptor(int width, int height, string name, GraphicsFormat format)
        {
            return CreateFilterDescriptor(width, height, name, format, false);
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct CopyHistoryScreenSpacePassData
        {
            public RGTextureRef radianceSource;
            public RGTextureRef radianceHistory;
            public RGTextureRef momentsSource;
            public RGTextureRef momentsHistory;
            public RGTextureRef depthNormalSource;
            public RGTextureRef depthNormalHistory;
        }

        void CopyHistoryScreenSpace(
            HistoryCache historyCache,
            Camera camera,
            CustomSamplerId samplerId,
            int radianceSourceId,
            int momentsSourceId,
            int depthNormalSourceId,
            int radianceHistoryId,
            int momentsHistoryId,
            int depthNormalHistoryId,
            string radianceName,
            string momentsName,
            string depthNormalName)
        {
            if (!m_RGScoper.TryQueryTexture(radianceSourceId, out RGTextureRef radianceSource) ||
                !m_RGScoper.TryQueryTexture(momentsSourceId, out RGTextureRef momentsSource) ||
                !m_RGScoper.TryQueryTexture(depthNormalSourceId, out RGTextureRef depthNormalSource))
            {
                return;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat;

            TextureDescriptor radianceDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, radianceName, format);
            TextureDescriptor momentsDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, momentsName, format);
            TextureDescriptor depthNormalDsc = ScreenSpaceHistoryUtility.CreateHistoryDescriptor(width, height, depthNormalName, format);

            RGTextureRef radianceHistory = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(radianceHistoryId, radianceDsc));
            RGTextureRef momentsHistory = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(momentsHistoryId, momentsDsc));
            RGTextureRef depthNormalHistory = m_RGBuilder.ImportTexture(historyCache.GetWriteTexture(depthNormalHistoryId, depthNormalDsc));
            historyCache.MarkProduced(radianceHistoryId);
            historyCache.MarkProduced(momentsHistoryId);
            historyCache.MarkProduced(depthNormalHistoryId);

            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<CopyHistoryScreenSpacePassData>(ProfilingSampler.Get(samplerId)))
            {
                passRef.ReadTexture(radianceSource);
                passRef.WriteTexture(radianceHistory);
                passRef.ReadTexture(momentsSource);
                passRef.WriteTexture(momentsHistory);
                passRef.ReadTexture(depthNormalSource);
                passRef.WriteTexture(depthNormalHistory);

                ref CopyHistoryScreenSpacePassData passData = ref passRef.GetPassData<CopyHistoryScreenSpacePassData>();
                passData.radianceSource = radianceSource;
                passData.radianceHistory = radianceHistory;
                passData.momentsSource = momentsSource;
                passData.momentsHistory = momentsHistory;
                passData.depthNormalSource = depthNormalSource;
                passData.depthNormalHistory = depthNormalHistory;

                passRef.SetExecuteFunc((in CopyHistoryScreenSpacePassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.CopyTexture(passData.radianceSource, passData.radianceHistory);
                    cmdEncoder.CopyTexture(passData.momentsSource, passData.momentsHistory);
                    cmdEncoder.CopyTexture(passData.depthNormalSource, passData.depthNormalHistory);
                });
            }
        }

        void CopyHistorySSR(RenderContext renderContext, HistoryCache historyCache, Camera camera)
        {
            CopyHistoryScreenSpace(
                historyCache,
                camera,
                CustomSamplerId.CopyHistorySSR,
                InfinityShaderIDs.SSRTemporalBuffer,
                InfinityShaderIDs.SSRMomentsBuffer,
                InfinityShaderIDs.SSRDepthNormalBuffer,
                InfinityShaderIDs.HistorySSRRadianceBuffer,
                InfinityShaderIDs.HistorySSRMomentsBuffer,
                InfinityShaderIDs.HistorySSRDepthNormalBuffer,
                "HistorySSRRadiance",
                "HistorySSRMoments",
                "HistorySSRDepthNormal");
        }

        void CopyHistorySSGI(RenderContext renderContext, HistoryCache historyCache, Camera camera)
        {
            CopyHistoryScreenSpace(
                historyCache,
                camera,
                CustomSamplerId.CopyHistorySSGI,
                InfinityShaderIDs.SSGITemporalBuffer,
                InfinityShaderIDs.SSGIMomentsBuffer,
                InfinityShaderIDs.SSGIDepthNormalBuffer,
                InfinityShaderIDs.HistorySSGIRadianceBuffer,
                InfinityShaderIDs.HistorySSGIMomentsBuffer,
                InfinityShaderIDs.HistorySSGIDepthNormalBuffer,
                "HistorySSGIRadiance",
                "HistorySSGIMoments",
                "HistorySSGIDepthNormal");
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
