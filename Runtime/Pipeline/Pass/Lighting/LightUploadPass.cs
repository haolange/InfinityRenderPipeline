using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    internal sealed class LightUploadPass
    {
        readonly RGBuilder m_RGBuilder;

        internal LightUploadPass(RGBuilder graph)
        {
            m_RGBuilder = graph;
        }

        static readonly ProfilingSampler s_LightUpload = new ProfilingSampler("UploadLightData");
        struct LightUploadPassData
        {
            public LightContext context;
        }

        internal void UploadLightData(LightContext context)
        {
            {
                RGTransferPassHandle<LightUploadPassData> passHandle = m_RGBuilder.AddTransferPass<LightUploadPassData>(s_LightUpload);
                passHandle.GetPassData().context = context;
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightRecordBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.LightRecordBuffer, "LightRecordBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightTileRectBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.TileRectBuffer, "LightTileRectBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightZBinBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.ZBinBuffer, "LightZBinBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.EmptyLightTileMaskBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.EmptyTileMaskBuffer, "EmptyLightTileMaskBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LocalShadowMatrixBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowMatrixBuffer, "LocalShadowMatrixBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LocalShadowRectBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowRectBuffer, "LocalShadowRectBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightCookieBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.CookieBuffer, "LightCookieBuffer")));
                passHandle.SetExecuteFunc((in LightUploadPassData data, in RGTransferEncoder commands) => data.context.Upload(commands));
            }
        }
    }
}
