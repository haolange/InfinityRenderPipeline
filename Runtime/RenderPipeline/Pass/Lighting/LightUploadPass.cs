using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        static readonly ProfilingSampler s_LightUpload = new ProfilingSampler("UploadLightData");
        struct LightUploadPassData { public LightContext context; }

        void UploadLightData(LightContext context)
        {
            using (RGTransferPassHandle pass = m_RGBuilder.AddTransferPass<LightUploadPassData>(s_LightUpload))
            {
                pass.GetPassData<LightUploadPassData>().context = context;
                m_RGScoper.RegisterBuffer(LightShaderIDs.LightRecordBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LightRecordBuffer, "LightRecordBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LightTileRectBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.TileRectBuffer, "LightTileRectBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LightZBinBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ZBinBuffer, "LightZBinBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.EmptyLightTileMaskBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.EmptyTileMaskBuffer, "EmptyLightTileMaskBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LocalShadowMatrixBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowMatrixBuffer, "LocalShadowMatrixBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LocalShadowRectBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.LocalShadowRectBuffer, "LocalShadowRectBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.LightCookieBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.CookieBuffer, "LightCookieBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.ReflectionProbeBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ReflectionProbeBuffer, "ReflectionProbeBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.ProbeTileRectBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ProbeTileRectBuffer, "ProbeTileRectBuffer")));
                m_RGScoper.RegisterBuffer(LightShaderIDs.ProbeZBinBuffer, pass.WriteBuffer(m_RGBuilder.ImportBuffer(context.ProbeZBinBuffer, "ProbeZBinBuffer")));
                pass.SetExecuteFunc((in LightUploadPassData data, in RGTransferEncoder commands) => data.context.Upload(commands));
            }
        }
    }
}
