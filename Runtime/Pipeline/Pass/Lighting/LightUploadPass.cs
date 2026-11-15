using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        static readonly ProfilingSampler s_LightUpload = new ProfilingSampler("UploadLightData");
        struct LightUploadPassData
        {
            public LightContext context;
        }

        void UploadLightData(LightContext context)
        {
            using (var pass = m_RGBuilder.AddTransferPass<LightUploadPassData>(s_LightUpload))
            {
                pass.GetPassData().context = context;
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightRecordBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LightRecordBuffer, "LightRecordBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightTileRectBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.TileRectBuffer, "LightTileRectBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightZBinBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ZBinBuffer, "LightZBinBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.EmptyLightTileMaskBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.EmptyTileMaskBuffer, "EmptyLightTileMaskBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LocalShadowMatrixBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowMatrixBuffer, "LocalShadowMatrixBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LocalShadowRectBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowRectBuffer, "LocalShadowRectBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.LightCookieBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.CookieBuffer, "LightCookieBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.ReflectionProbeBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ReflectionProbeBuffer, "ReflectionProbeBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.ProbeTileRectBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ProbeTileRectBuffer, "ProbeTileRectBuffer")));
                m_RGBuilder.scoper.RegisterBuffer(LightShaderIDs.ProbeZBinBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ProbeZBinBuffer, "ProbeZBinBuffer")));
                pass.SetExecuteFunc((in LightUploadPassData data, in RGTransferEncoder commands) => data.context.Upload(commands));
            }
        }
    }
}
